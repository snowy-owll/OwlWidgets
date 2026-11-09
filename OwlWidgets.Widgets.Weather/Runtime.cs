using System.Text.Json;
using System.Windows;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.Weather.Providers;
using OwlWidgets.Widgets.Weather.ViewModels;

namespace OwlWidgets.Widgets.Weather;

public sealed class Runtime : IWidgetRuntime
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private IWidgetHostContext? _context;
    private ContentViewModel? _viewModel;
    private Content? _content;

    public FrameworkElement? View { get; private set; }

    public void Start(IWidgetHostContext context)
    {
        _context = context;
        var config = Read(context.Settings, context);
        Show(config);
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
        // Read again on every apply: the settings may name another pack, and the old one may be deleted.
        var icons = new WeatherIcons(IconPacks.FolderPath(context.InstanceDataFolder, config.IconPack), context.Logger);
        _viewModel = new ContentViewModel(config, CreateProvider(config), icons, context.Logger, () =>
            context.Send(new WidgetCommand(WidgetCommandCodes.OpenSettings, WidgetCommandTarget.Manager)));
        _content ??= new Content();
        _content.DataContext = _viewModel;
        View = _content;
        _viewModel.Start();
    }

    /// <summary>null when the settings lack what the provider needs: no place, or no key for OpenWeatherMap.</summary>
    private static IWeatherProvider? CreateProvider(Config config)
    {
        if (config.Place is not { } place)
            return null;
        return config.Provider switch
        {
            WeatherProviderKind.OpenMeteo => new OpenMeteoProvider(place),
            _ when string.IsNullOrWhiteSpace(config.ApiKey) => null,
            _ => new OpenWeatherMapProvider(place, config.ApiKey.Trim())
        };
    }

    private Config Read(string? settings, IWidgetHostContext context)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Config();

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
        return JsonSerializer.Deserialize<Config>(settings, Json) ?? new Config();
    }
}
