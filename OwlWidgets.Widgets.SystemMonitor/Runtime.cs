using System.Text.Json;
using System.Windows;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.SystemMonitor.Providers;
using OwlWidgets.Widgets.SystemMonitor.Sensors;
using OwlWidgets.Widgets.SystemMonitor.ViewModels;

namespace OwlWidgets.Widgets.SystemMonitor;

public sealed class Runtime : IWidgetRuntime
{
    private IWidgetHostContext? _context;
    private ContentViewModel? _viewModel;
    /// <summary>Made once, in the style of the settings the card started with: another style needs a restart.</summary>
    private FrameworkElement? _content;
    /// <summary>What the wrapper said last; a model made for new settings starts from it.</summary>
    private CardCorner _anchor;
    private bool _topmost;

    public FrameworkElement? View { get; private set; }

    public void Start(IWidgetHostContext context)
    {
        _context = context;
        Show(Read(context.Settings, context));
    }

    public void Receive(WidgetCommand command)
    {
        switch (command.Code)
        {
            case WidgetCommandCodes.Anchor:
                if (WidgetCommandJson.Deserialize<AnchorBody>(command.Body) is { } anchor)
                {
                    _anchor = anchor.Anchor;
                    if (_viewModel != null)
                        _viewModel.Anchor = _anchor;
                }

                return;
            case WidgetCommandCodes.Topmost:
                if (WidgetCommandJson.Deserialize<TopmostBody>(command.Body) is { } topmost)
                {
                    _topmost = topmost.Topmost;
                    if (_viewModel != null)
                        _viewModel.IsTopmost = _topmost;
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
        _viewModel = new ContentViewModel(new SystemSampler(config, CreateProviders(config), context.Logger), config, context.Logger,
            new InstanceFile<CardState>(context.InstanceDataFolder, "card-state.json", context.Logger),
            () => context.Send(new WidgetCommand(WidgetCommandCodes.EnterMoveMode, WidgetCommandTarget.Wrapper)),
            // The manager keeps it with the instance and answers with topmost; the button changes only then.
            topmost => context.Send(new WidgetCommand(WidgetCommandCodes.SetTopmost, WidgetCommandTarget.Manager,
                WidgetCommandJson.Serialize(new TopmostBody { Topmost = topmost }))))
        {
            Anchor = _anchor,
            IsTopmost = _topmost
        };
        _content ??= config.Style switch
        {
            CardView.Classic => new Content(),
            CardView.ImHex => new ImHexCard(),
            _ => new TerminalCard { Layout = config.Style }
        };
        _content.DataContext = _viewModel;
        View = _content;
        _viewModel.Start();
    }

    /// <summary>
    /// The one place that knows the providers: a new one is added here, in <see cref="SensorProviderKind"/>
    /// and in the list of the settings screen. Automatic takes them in this order.
    /// </summary>
    private static IReadOnlyList<ISensorProvider> CreateProviders(Config config) => config.Provider switch
    {
        _ when !config.ShowSystem && !config.ShowTemperatures => [],
        SensorProviderKind.Windows => [],
        SensorProviderKind.HWiNFO => [new HWiNFOProvider()],
        SensorProviderKind.Afterburner => [new AfterburnerProvider()],
        SensorProviderKind.LibreHardwareMonitor => [new LibreHardwareMonitorProvider(config.LibreHardwareMonitor)],
        _ => [new HWiNFOProvider(), new AfterburnerProvider(), new LibreHardwareMonitorProvider(config.LibreHardwareMonitor)]
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
