using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OwlWidgets.Protocol;
using OwlWidgets.WidgetHost.Logging;

namespace OwlWidgets.WidgetHost;

public partial class App : Application
{
    private readonly HostOptions? _options;
    private ServiceProvider? _services;
    private HostSession? _session;

    /// <summary>
    /// The UI language comes from the command line and is applied before <c>Run</c>: WPF pins the UI culture
    /// of the thread when <c>Run</c> starts and gives it back after every dispatcher callback, so a language set
    /// later (it used to come in Welcome) held only inside that callback, and texts built afterwards, on a command
    /// or a mouse event, came in the language of Windows.
    /// </summary>
    public App()
    {
        if (HostSession.TryParse(Environment.GetCommandLineArgs()[1..], out var options))
        {
            _options = options;
            UiLanguage.Apply(options.Language);
        }

        UseRegionalFormats();
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        if (_options is not { } options)
        {
            Shutdown(1);
            return;
        }

        _services = ConfigureServices(options);
        _session = _services.GetRequiredService<HostSession>();
        _session.Start();
    }

    /// <summary>
    /// WPF formats bindings by <see cref="FrameworkElement.Language"/>, which is en-US unless set, not by
    /// <see cref="CultureInfo.CurrentCulture"/>. Formats follow the Windows regional settings, as in the manager.
    /// </summary>
    private static void UseRegionalFormats() =>
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

    private static ServiceProvider ConfigureServices(HostOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton<ChannelLog>();
        services.AddLogging(builder => builder.ClearProviders().SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<ILoggerProvider, ChannelLoggerProvider>();
        services.AddSingleton<HostSession>();
        return services.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _session?.ReportCrash(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _session?.ReportCrash(exception);
    }
}
