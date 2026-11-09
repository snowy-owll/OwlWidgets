using Microsoft.Extensions.Logging;

namespace OwlWidgets.Services;

/// <summary>
/// Settings files of the instances, <c>Data/settings/{instanceId}.json</c>. The manager does not parse them.
/// </summary>
internal interface IInstanceSettingsStore
{
    /// <summary>The file as it is on disk; null when there is no file.</summary>
    string? Read(Guid instanceId);

    void Write(Guid instanceId, string settings);

    /// <summary>A failure is logged and does not stop the caller.</summary>
    void Delete(Guid instanceId);
}

internal sealed class InstanceSettingsStore : IInstanceSettingsStore
{
    private readonly AppPaths _paths;
    private readonly ILogger<InstanceSettingsStore> _log;

    public InstanceSettingsStore(AppPaths paths, ILogger<InstanceSettingsStore> log)
    {
        _paths = paths;
        _log = log;
    }

    public string? Read(Guid instanceId)
    {
        var path = _paths.SettingsPath(instanceId);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Write(Guid instanceId, string settings) => AtomicFile.Write(_paths.SettingsPath(instanceId), settings);

    public void Delete(Guid instanceId)
    {
        var path = _paths.SettingsPath(instanceId);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception)
        {
            _log.LogWarning(exception, "File {Path} was not deleted", path);
        }
    }
}
