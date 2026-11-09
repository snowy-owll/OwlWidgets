using OwlWidgets.Logging;
using OwlWidgets.Services;
using OwlWidgets.Settings;

namespace OwlWidgets.Hosting;

internal interface IInstanceSessionFactory
{
    InstanceSession Create(WidgetPackage package, InstanceRecord record);
}

internal sealed class InstanceSessionFactory : IInstanceSessionFactory
{
    private readonly InstanceRegistry _registry;
    private readonly ManagerLog _managerLog;
    private readonly IUiDispatcher _ui;
    private readonly AppPaths _paths;
    private readonly IInstanceSettingsStore _settings;
    private readonly IDesktopLayer _desktop;
    private readonly INotificationService _notifications;
    private readonly ThemeService _theme;
    private readonly LanguageService _language;

    public InstanceSessionFactory(InstanceRegistry registry, ManagerLog managerLog, IUiDispatcher ui, AppPaths paths,
        IInstanceSettingsStore settings, IDesktopLayer desktop, INotificationService notifications, ThemeService theme,
        LanguageService language)
    {
        _registry = registry;
        _managerLog = managerLog;
        _ui = ui;
        _paths = paths;
        _settings = settings;
        _desktop = desktop;
        _notifications = notifications;
        _theme = theme;
        _language = language;
    }

    public InstanceSession Create(WidgetPackage package, InstanceRecord record) =>
        new(package, record, _registry, _managerLog, _ui, _paths, _settings, _desktop, _notifications, _theme, _language);
}
