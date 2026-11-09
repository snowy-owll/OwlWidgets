using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OwlWidgets.Hosting;
using OwlWidgets.Logging;
using OwlWidgets.Services;
using OwlWidgets.Settings;
using OwlWidgets.Updates;

namespace OwlWidgets
{
    public partial class App : System.Windows.Application, IAppLifetime
    {
        private const string RestartArgument = "--restart";
        private static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(15);
        private SingleInstanceGuard? _guard;
        private ServiceProvider? _services;
        private ILogger<App>? _log;
        private InstanceManager? _instances;
        private bool _closing;
        private bool _restart;
        private bool _restarted;
        private bool _firstRun;

        public bool IsClosing => _closing;

        public void Close() => Dispatcher.BeginInvoke(new Action(CloseCore));

        /// <summary>
        /// The new process waits for this one to release the single-instance mutex.
        /// </summary>
        public void Restart()
        {
            _restart = true;
            Close();
        }

        private async void CloseCore()
        {
            if (_closing)
                return;
            _closing = true;
            _log?.LogInformation(_restart ? "Manager is restarting" : "Manager is shutting down");

            try
            {
                if (_instances != null)
                    await _instances.ShutdownAsync();
            }
            catch (Exception exception)
            {
                _log?.LogCritical(exception, "Manager shutdown failed");
            }

            _services?.GetRequiredService<IDesktopLayer>().Stop();
            _services?.GetRequiredService<TrayIconService>().Hide();
            _guard?.Dispose();
            if (_restart)
                StartNewManager();
            _log?.LogInformation("Manager stopped");
            _services?.Dispose();
            Shutdown();
        }

        /// <summary>
        /// The single-instance check, the log, the settings and the UI language, before <c>Run</c>.
        /// WPF pins the UI culture of the thread when <c>Run</c> starts and gives it back after every dispatcher
        /// callback, <c>Startup</c> included: a language set in <c>Startup</c> held only until the handler returned,
        /// and the texts built later (states, the settings screen, the tray menu, the descriptors) came in the
        /// language of Windows.
        /// </summary>
        public App()
        {
            _restarted = Environment.GetCommandLineArgs().Contains(RestartArgument);
            _guard = new SingleInstanceGuard();
            if (!_guard.TryAcquire(_restarted ? RestartWait : TimeSpan.Zero))
            {
                // Another manager is already running: ask it to open its window; Startup exits.
                _guard.SignalRunning();
                _guard.Dispose();
                _guard = null;
                return;
            }

            _services = ConfigureServices();
            // Before the log: its files are the first thing to create the data folder.
            _firstRun = !Directory.Exists(_services.GetRequiredService<AppPaths>().DataRoot);
            _services.GetRequiredService<ManagerLog>().Initialize();
            _log = _services.GetRequiredService<ILogger<App>>();
            LogStartup(_services.GetRequiredService<ManagerLog>());
            if (_firstRun)
                _log.LogInformation("No data folder: the first start of the application");

            // The language comes first: the tray menu, the screens and the descriptors read their texts once.
            _services.GetRequiredService<ManagerSettingsStore>().Load();
            _services.GetRequiredService<LanguageService>()
                .Apply(_services.GetRequiredService<ManagerSettingsStore>().Current.Language);
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            if (_guard == null || _services == null || _log == null)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            var settings = _services.GetRequiredService<ManagerSettingsStore>();
            // Theme resources go into Application.Resources, which App.xaml fills after the constructor.
            _services.GetRequiredService<ThemeService>().Apply(settings.Current.Theme);

            _services.GetRequiredService<IDesktopLayer>().Start();
            _services.GetRequiredService<TrayIconService>().Show();
            _instances = _services.GetRequiredService<InstanceManager>();
            // Follows the instances from their first start: the mass move and the raised desktop.
            _services.GetRequiredService<MassMoveCoordinator>();
            await _instances.LoadAsync();
            if (_closing)
                return;
            MainWindow = _services.GetRequiredService<MainWindow>();
            var window = _services.GetRequiredService<IMainWindowService>();
            _instances.SettingsRequested += window.ShowSettings;
            _instances.StartWithManager();
            _guard.ShowRequested += () => Dispatcher.BeginInvoke(new Action(window.Show));
            _guard.Listen();
            if (_restarted || _firstRun || settings.Current.ShowWindowOnStart)
                window.Show();
            _services.GetRequiredService<UpdateChecker>().Start();
        }

        private ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAppLifetime>(this);
            services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(Dispatcher));
            services.AddManager();
            return services.BuildServiceProvider();
        }

        private void StartNewManager()
        {
            try
            {
                var path = Environment.ProcessPath ?? throw new InvalidOperationException("The manager executable path is unknown.");
                var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
                start.ArgumentList.Add(RestartArgument);
                Process.Start(start);
                _log?.LogInformation("New manager process started");
            }
            catch (Exception exception)
            {
                _log?.LogError(exception, "The manager was not started again");
            }
        }

        private void LogStartup(ManagerLog managerLog)
        {
            _log?.LogInformation(
                "Manager started: version {Version}, process {ProcessId}, {Framework}, {OS}, folder {BaseDirectory}",
                AppInfo.Version, Environment.ProcessId, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Environment.OSVersion, AppContext.BaseDirectory);
            _log?.LogInformation("Logs: {Path}, minimum level {Level}, build {Configuration}",
                managerLog.Root, managerLog.MinimumLevel, AppInfo.BuildConfiguration);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _log?.LogCritical(e.Exception, "Unhandled manager exception on the UI thread");
            e.Handled = true;
            Close();
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
                _log?.LogCritical(exception, "Unhandled manager exception");
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
