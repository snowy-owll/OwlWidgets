using Microsoft.Extensions.Logging;

namespace OwlWidgets.Services;

/// <summary>
/// Folders of files the widgets keep for their instances, <c>Data/storage/{instanceId}</c>. The manager only gives
/// the path and deletes the folder; what lies in it is the widget's business.
/// </summary>
internal interface IInstanceStorage
{
    string Folder(Guid instanceId);

    /// <summary>The folder with everything in it; a failure is logged and does not stop the caller.</summary>
    void Delete(Guid instanceId);
}

internal sealed class InstanceStorage : IInstanceStorage
{
    private readonly AppPaths _paths;
    private readonly ILogger<InstanceStorage> _log;

    public InstanceStorage(AppPaths paths, ILogger<InstanceStorage> log)
    {
        _paths = paths;
        _log = log;
    }

    public string Folder(Guid instanceId) => _paths.InstanceDataFolder(instanceId);

    public void Delete(Guid instanceId)
    {
        var folder = Folder(instanceId);
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception)
        {
            _log.LogWarning(exception, "Folder {Path} was not deleted", folder);
        }
    }
}
