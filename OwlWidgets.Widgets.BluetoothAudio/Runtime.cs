using System.Text.Json;
using System.Windows;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.BluetoothAudio.ViewModels;

namespace OwlWidgets.Widgets.BluetoothAudio;

public sealed class Runtime : IWidgetRuntime
{
    private IWidgetHostContext? _context;
    private ContentViewModel? _viewModel;
    private Content? _content;

    public FrameworkElement? View { get; private set; }

    public void Start(IWidgetHostContext context)
    {
        _context = context;
        Show(Read(context.Settings, context));
    }

    public void Receive(WidgetCommand command)
    {
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
        _viewModel = new ContentViewModel(config, context.Logger, () =>
            context.Send(new WidgetCommand(WidgetCommandCodes.OpenSettings, WidgetCommandTarget.Manager)));
        _content ??= new Content();
        _content.DataContext = _viewModel;
        View = _content;
        _viewModel.Start();
    }

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
