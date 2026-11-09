using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OwlWidgets.Hosting;
using OwlWidgets.Logging;
using OwlWidgets.Settings;
using OwlWidgets.Updates;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Services;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Everything the manager is made of. <see cref="IAppLifetime"/> and <see cref="IUiDispatcher"/>
    /// belong to the application object and are registered by it.
    /// </summary>
    public static IServiceCollection AddManager(this IServiceCollection services)
    {
        services.AddSingleton(_ => new AppPaths(AppContext.BaseDirectory));
        services.AddSingleton<LogFileStore>();
        services.AddSingleton<LogFeed>();
        services.AddSingleton<ManagerLog>();
        services.AddLogging(builder => builder.ClearProviders().SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<ILoggerProvider, ManagerLoggerProvider>();

        services.AddSingleton<ManagerSettingsStore>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<IAutostartService, AutostartService>();

        services.AddSingleton<IWidgetCatalog, WidgetCatalog>();
        services.AddSingleton<InstanceRegistry>();
        services.AddSingleton<IInstanceSettingsStore, InstanceSettingsStore>();
        services.AddSingleton<IInstanceStorage, InstanceStorage>();
        services.AddSingleton<IInstanceSessionFactory, InstanceSessionFactory>();
        services.AddSingleton<ISettingsEditorFactory, WidgetSettingsEditorFactory>();
        services.AddSingleton<IDesktopLayer, DesktopLayerService>();
        services.AddSingleton<InstanceSeeder>();
        services.AddSingleton<InstanceManager>();
        services.AddSingleton<IMovableInstances>(provider => provider.GetRequiredService<InstanceManager>());
        services.AddSingleton<MassMoveCoordinator>();

        services.AddSingleton(_ => new NotifyIcon());
        services.AddSingleton<INotificationService, TrayNotificationService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<IMainWindowService, MainWindowService>();

        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IReleaseSource, GitHubReleaseSource>();
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogHostService>();

        services.AddSingleton<WidgetsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton(provider => new Lazy<MainWindow>(provider.GetRequiredService<MainWindow>));
        return services;
    }
}
