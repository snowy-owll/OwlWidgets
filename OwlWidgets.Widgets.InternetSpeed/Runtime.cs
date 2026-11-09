using System.Text.Json;
using System.Windows;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.InternetSpeed.Measurement;
using OwlWidgets.Widgets.InternetSpeed.Providers;
using OwlWidgets.Widgets.InternetSpeed.ViewModels;

namespace OwlWidgets.Widgets.InternetSpeed;

public sealed class Runtime : IWidgetRuntime
{
    private IWidgetHostContext? _context;
    private ContentViewModel? _viewModel;
    private Content? _content;
    // Kept for the view model of new settings: the wrapper sends the anchor only when it changes.
    private CardCorner _anchor;

    public FrameworkElement? View { get; private set; }

    public void Start(IWidgetHostContext context)
    {
        _context = context;
        Show(Read(context.Settings, context));
    }

    public void Receive(WidgetCommand command)
    {
        if (command.Code == WidgetCommandCodes.Anchor)
        {
            if (WidgetCommandJson.Deserialize<AnchorBody>(command.Body) is { } anchor)
            {
                _anchor = anchor.Anchor;
                if (_viewModel != null)
                    _viewModel.Anchor = _anchor;
            }

            return;
        }

        if (command.Code != WidgetCommandCodes.ApplySettings || _context == null)
            return;

        Config config;
        try
        {
            config = ReadStrict(WidgetCommandJson.ReadUtf8(command.Body));
        }
        catch (Exception exception)
        {
            _context.Logger.Log(WidgetLogLevel.Error, "Settings were not applied, the previous state is kept.", exception);
            return;
        }

        // A test going on stops: it ran with the previous servers and options.
        _viewModel?.Stop();
        Show(config);
    }

    public void Stop()
    {
        _viewModel?.Stop();
        _viewModel = null;
        View = null;
    }

    public void Dispose() => Stop();

    private void Show(Config config)
    {
        var context = _context!;
        _viewModel = new ContentViewModel(config, CreateProvider(config),
            new InstanceFile<SpeedTestResult>(context.InstanceDataFolder, "last-result.json", context.Logger),
            new InstanceFile<CardState>(context.InstanceDataFolder, "card-state.json", context.Logger),
            context.Logger,
            () => context.Send(new WidgetCommand(WidgetCommandCodes.EnterMoveMode, WidgetCommandTarget.Wrapper)))
        {
            Anchor = _anchor
        };
        _content ??= new Content();
        _content.DataContext = _viewModel;
        View = _content;
        _viewModel.Start();
    }

    /// <summary>The one place that knows the providers: a new one is added here and in <see cref="SpeedTestProviderKind"/>.</summary>
    private static ISpeedTestProvider CreateProvider(Config config) => config.Provider switch
    {
        SpeedTestProviderKind.MLab => new MLabProvider(),
        _ => new CloudflareProvider()
    };

    private static Config Read(string? settings, IWidgetHostContext context)
    {
        try
        {
            return ReadStrict(settings);
        }
        catch (Exception exception)
        {
            context.Logger.Log(WidgetLogLevel.Error, "Settings were not read, defaults are used.", exception);
            return new Config();
        }
    }

    private static Config ReadStrict(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Config();
        return JsonSerializer.Deserialize<Config>(settings) ?? new Config();
    }
}
