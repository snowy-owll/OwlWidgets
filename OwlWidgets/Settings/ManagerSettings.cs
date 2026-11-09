using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OwlWidgets.Services;

namespace OwlWidgets.Settings;

internal enum AppTheme
{
    System,
    Light,
    Dark
}

internal enum AppLanguage
{
    System,
    English,
    Russian
}

/// <summary>
/// Settings of the application as a whole, stored in <c>Data/manager.json</c>.
/// </summary>
internal sealed class ManagerSettings
{
    public int Version { get; set; } = 1;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Open the manager window when the application starts; the very first start opens it anyway.</summary>
    public bool ShowWindowOnStart { get; set; }

    /// <summary>Ask GitHub for the latest release at start and once a day.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>The newer version the tray has already told about, so that it does so once.</summary>
    public string? NotifiedUpdate { get; set; }
}

internal sealed class ManagerSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;
    private readonly ILogger<ManagerSettingsStore> _log;

    public ManagerSettingsStore(AppPaths paths, ILogger<ManagerSettingsStore> log)
    {
        _path = paths.ManagerSettingsPath;
        _log = log;
    }

    public ManagerSettings Current { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Current = new ManagerSettings();
            _log.LogInformation("Manager settings not found, using defaults: theme {Theme}, language {Language}",
                Current.Theme, Current.Language);
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(_path), Options) ?? new ManagerSettings();
            _log.LogInformation("Manager settings loaded: theme {Theme}, language {Language}", Current.Theme, Current.Language);
        }
        catch (Exception exception)
        {
            Current = new ManagerSettings();
            _log.LogWarning(exception, "Manager settings could not be read from {Path}, using defaults", _path);
        }
    }

    public void Save()
    {
        try
        {
            AtomicFile.Write(_path, JsonSerializer.Serialize(Current, Options));
            _log.LogInformation("Manager settings saved: theme {Theme}, language {Language}", Current.Theme, Current.Language);
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Manager settings could not be written to {Path}", _path);
        }
    }
}
