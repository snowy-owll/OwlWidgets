using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using OwlWidgets.Hosting;
using OwlWidgets.Logging;
using OwlWidgets.Services;
using OwlWidgets.Settings;
using OwlWidgets.Updates;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Tests;

/// <summary>
/// The real instance manager over a temporary folder. Instances are loaded from the registry and never started,
/// so no wrapper process is launched.
/// </summary>
internal sealed class ManagerFixture : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OwlWidgets.Tests", Guid.NewGuid().ToString("N"));
    private readonly ManagerLog _log;
    private readonly ThemeService _theme;

    public ManagerFixture(IEnumerable<WidgetPackage> packages, params InstanceRecord[] records)
    {
        // Coordinates are written and read by the Windows regional settings; the tests fix them.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        Paths = new AppPaths(_root);
        Catalog.Packages = packages.ToArray();
        Registry = new InstanceRegistry(Paths);
        Registry.Instances.AddRange(records);
        Registry.Save();

        _log = new ManagerLog(new LogFileStore(Paths), Feed);
        var settings = new InstanceSettingsStore(Paths, NullLogger<InstanceSettingsStore>.Instance);
        _theme = new ThemeService(NullLogger<ThemeService>.Instance, Ui);
        var language = new LanguageService(NullLogger<LanguageService>.Instance);
        var sessions = new InstanceSessionFactory(Registry, _log, Ui, Paths, settings, Desktop, new FakeNotifications(),
            _theme, language);
        var seeder = new InstanceSeeder(Registry, settings, NullLogger<InstanceSeeder>.Instance);
        Storage = new InstanceStorage(Paths, NullLogger<InstanceStorage>.Instance);
        Editors = new WidgetSettingsEditorFactory(Storage);
        Manager = new InstanceManager(Registry, Catalog, sessions, settings, Storage, seeder, Desktop, _theme, _log,
            NullLogger<InstanceManager>.Instance);
        // The registry exists, so the load completes at once.
        Manager.LoadAsync().GetAwaiter().GetResult();
        MassMove = new MassMoveCoordinator(Manager, Desktop, NullLogger<MassMoveCoordinator>.Instance);
    }

    public AppPaths Paths { get; }

    public FakeUi Ui { get; } = new();

    /// <summary>The records of the session the manager log keeps for the log screen.</summary>
    public LogFeed Feed { get; } = new();

    public ManagerLog Log => _log;

    public FakeShell Shell { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public FakeCatalog Catalog { get; } = new();

    public FakeDesktop Desktop { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public FakeWindow Window { get; } = new();

    public InstanceRegistry Registry { get; }

    public InstanceManager Manager { get; }

    public MassMoveCoordinator MassMove { get; }

    public static InstanceRecord Record(WidgetPackage package, double x = 0, double y = 0) => new()
    {
        InstanceId = Guid.NewGuid(),
        WidgetId = package.Descriptor.Id,
        X = x,
        Y = y
    };

    /// <summary>The real factory: the guards around the widget's screen are part of what is tested.</summary>
    public ISettingsEditorFactory Editors { get; }

    public IInstanceStorage Storage { get; }

    /// <param name="massMove">Another coordinator, for example one over running fake instances.</param>
    public WidgetsViewModel CreateWidgets(MassMoveCoordinator? massMove = null) =>
        new(Manager, massMove ?? MassMove, Dialogs, Window, Ui, Editors, NullLogger<AddWidgetViewModel>.Instance);

    public InstanceViewModel CreateInstance(int index) => new(Session(index), Ui, Editors);

    public AddWidgetViewModel CreateAddDialog() =>
        new(Manager, Dialogs, Editors, NullLogger<AddWidgetViewModel>.Instance);

    /// <param name="feed">Another feed, for example a smaller one; records of the manager log still go to <see cref="Feed"/>.</param>
    public LogViewModel CreateLog(LogFeed? feed = null) =>
        new(feed ?? Feed, Manager, Ui, _log, Shell, _theme, Dialogs, Clipboard);

    public FakeReleaseSource Releases { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    /// <summary>A checker over <see cref="Releases"/>; this build is <paramref name="currentVersion"/>.</summary>
    public UpdateChecker CreateUpdates(string currentVersion = "0.12.1")
    {
        var settings = new ManagerSettingsStore(Paths, NullLogger<ManagerSettingsStore>.Instance);
        settings.Load();
        return new UpdateChecker(Releases, settings, Notifications, Shell, Ui, NullLogger<UpdateChecker>.Instance, currentVersion);
    }

    public AboutViewModel CreateAbout(UpdateChecker? updates = null) => new(Catalog, Paths, _log, Shell, updates ?? CreateUpdates());

    public InstanceSession Session(int index) => Manager.Instances[index];

    /// <summary>The registry as it is on disk now.</summary>
    public InstanceRecord Stored(Guid instanceId)
    {
        var registry = new InstanceRegistry(Paths);
        registry.Load();
        return registry.Instances.Single(record => record.InstanceId == instanceId);
    }

    public void Dispose()
    {
        MassMove.Dispose();
        _theme.Dispose();
        _log.Dispose();
        CultureInfo.CurrentCulture = _culture;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A log file may still be closing; the folder is in the temporary directory anyway.
        }
    }
}
