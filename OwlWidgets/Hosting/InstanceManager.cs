using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using OwlWidgets.Logging;
using OwlWidgets.Services;
using OwlWidgets.Settings;

namespace OwlWidgets.Hosting;

/// <summary>
/// The instances of this manager: loading, creation, deletion and the start and stop of all of them.
/// </summary>
internal sealed class InstanceManager : IMovableInstances
{
    private readonly ManagerLog _managerLog;
    private readonly ILogger<InstanceManager> _log;
    private readonly InstanceRegistry _registry;
    private readonly IWidgetCatalog _catalog;
    private readonly IInstanceSessionFactory _sessions;
    private readonly IInstanceSettingsStore _settings;
    private readonly IInstanceStorage _storage;
    private readonly InstanceSeeder _seeder;
    private readonly IDesktopLayer _desktop;
    private readonly ThemeService _theme;

    public InstanceManager(InstanceRegistry registry, IWidgetCatalog catalog, IInstanceSessionFactory sessions,
        IInstanceSettingsStore settings, IInstanceStorage storage, InstanceSeeder seeder, IDesktopLayer desktop,
        ThemeService theme, ManagerLog managerLog, ILogger<InstanceManager> log)
    {
        _registry = registry;
        _catalog = catalog;
        _sessions = sessions;
        _settings = settings;
        _storage = storage;
        _seeder = seeder;
        _desktop = desktop;
        _theme = theme;
        _managerLog = managerLog;
        _log = log;
    }

    public ObservableCollection<InstanceSession> Instances { get; } = [];

    /// <summary>An instance asked to show its settings in the manager window.</summary>
    public event Action<InstanceSession>? SettingsRequested;

    /// <summary>An instance changed its state.</summary>
    public event Action<IMovableInstance>? InstanceStateChanged;

    /// <summary>An instance left the move mode; true: its position was saved.</summary>
    public event Action<IMovableInstance, bool>? MoveModeEnded;

    public IEnumerable<IMovableInstance> Movable => Instances;

    public IReadOnlyList<WidgetPackage> Packages => _catalog.Packages;

    /// <summary>Completes at once unless there is no registry: the first instance may ask the network for its settings.</summary>
    public async Task LoadAsync()
    {
        _catalog.Load();

        try
        {
            _registry.Load();
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Instance registry was not read, the list is empty");
        }

        if (!_registry.Existed)
        {
            _log.LogInformation("Instance registry not found, creating the first instance");
            await _seeder.SeedAsync(Packages).ConfigureAwait(true);
        }

        var byId = Packages.ToDictionary(package => package.Descriptor.Id);
        foreach (var record in _registry.Instances)
        {
            if (!byId.TryGetValue(record.WidgetId, out var package))
            {
                _log.LogWarning("Package {WidgetId} of instance {InstanceId} not found, the instance is hidden",
                    record.WidgetId, record.InstanceId);
                continue;
            }

            Attach(_sessions.Create(package, record));
        }

        _log.LogInformation("Loaded {Count} instances: {Instances}", Instances.Count,
            string.Join(", ", Instances.Select(instance => $"{instance.Name} {instance.InstanceId:N}")));

        _desktop.WidgetPlacement += OnPlacement;
        _theme.Changed += OnThemeChanged;
    }

    public void StartWithManager()
    {
        foreach (var instance in Instances.ToArray())
        {
            if (instance.StartWithManager)
                instance.Start("the start with manager policy");
            else
                instance.Log.LogInformation("Not started with the manager by policy");
        }
    }

    public bool CanAdd(WidgetPackage package) =>
        package.Descriptor.AllowMultipleInstances
        || Instances.All(instance => instance.WidgetId != package.Descriptor.Id);

    public IReadOnlyList<WidgetPackage> AddablePackages =>
        Packages.Where(CanAdd).OrderBy(package => package.Descriptor.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    /// <summary>
    /// Creates an instance and starts it. <paramref name="settings"/> null: no settings file, the widget uses its defaults.
    /// <paramref name="tag"/> empty: the instance is shown under the widget type name.
    /// </summary>
    public InstanceSession Add(WidgetPackage package, Guid instanceId, string? settings, string? tag)
    {
        var record = new InstanceRecord
        {
            InstanceId = instanceId,
            WidgetId = package.Descriptor.Id,
            Tag = InstanceSession.NormalizeTag(tag),
            X = 80 + Instances.Count * 36,
            Y = 80 + Instances.Count * 24
        };
        if (settings != null)
            _settings.Write(instanceId, settings);
        _registry.Instances.Add(record);
        _registry.Save();
        var session = _sessions.Create(package, record);
        Attach(session);
        session.Log.LogInformation("Instance of {Widget} created at ({X}, {Y}), tag \"{Tag}\", {Settings}", session.Name,
            record.X, record.Y, record.Tag,
            settings == null ? "default settings" : $"settings of {settings.Length} characters");
        session.Start("instance creation");
        return session;
    }

    public async Task DeleteAsync(InstanceSession session)
    {
        await session.Stop().ConfigureAwait(true);
        _registry.Instances.RemoveAll(record => record.InstanceId == session.InstanceId);
        _registry.Save();
        Detach(session);
        _settings.Delete(session.InstanceId);
        _storage.Delete(session.InstanceId);
        _managerLog.RemoveInstance(session.InstanceId);
        _log.LogInformation("Instance {InstanceId} of {Widget} deleted with its settings, files and log",
            session.InstanceId, session.Name);
    }

    /// <summary>
    /// The add dialog closed without creating <paramref name="instanceId"/>, or switched to another type:
    /// files the settings screen wrote for it go away. A registered instance keeps its files.
    /// </summary>
    public void DiscardFiles(Guid instanceId)
    {
        if (_registry.Instances.Any(record => record.InstanceId == instanceId))
            return;
        _storage.Delete(instanceId);
    }

    public async Task ShutdownAsync()
    {
        _desktop.WidgetPlacement -= OnPlacement;
        _theme.Changed -= OnThemeChanged;
        var stopping = Instances.ToArray();
        _log.LogInformation("Stopping {Count} instances before exit",
            stopping.Count(instance => instance.CanStop));
        await Task.WhenAll(stopping.Select(instance => instance.StopForManagerExit())).ConfigureAwait(true);
    }

    private void Attach(InstanceSession session)
    {
        session.StateChanged += OnInstanceStateChanged;
        session.MoveModeEnded += OnMoveModeEnded;
        session.SettingsRequested += OnSettingsRequested;
        Instances.Add(session);
    }

    private void Detach(InstanceSession session)
    {
        session.StateChanged -= OnInstanceStateChanged;
        session.MoveModeEnded -= OnMoveModeEnded;
        session.SettingsRequested -= OnSettingsRequested;
        Instances.Remove(session);
    }

    private void OnInstanceStateChanged(InstanceSession session) => InstanceStateChanged?.Invoke(session);

    private void OnMoveModeEnded(InstanceSession session, bool positionSaved) => MoveModeEnded?.Invoke(session, positionSaved);

    private void OnSettingsRequested(InstanceSession session) => SettingsRequested?.Invoke(session);

    private void OnPlacement(IntPtr cover)
    {
        foreach (var instance in Instances)
            instance.Place(cover);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _log.LogInformation("Application theme is now {Theme}, sending it to the instances", _theme.IsDark ? "dark" : "light");
        foreach (var instance in Instances)
            instance.OnApplicationThemeChanged();
    }
}
