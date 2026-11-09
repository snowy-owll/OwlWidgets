using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OwlWidgets.Services;

/// <summary>
/// Windows Explorer and other things outside the manager.
/// </summary>
internal interface IShellService
{
    /// <summary>Opens the folder in Explorer, creating it first when it does not exist yet.</summary>
    void OpenFolder(string path);

    /// <summary>Opens the web page in the default browser.</summary>
    void OpenWebPage(string url);
}

internal sealed class ShellService(ILogger<ShellService> logger) : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void OpenWebPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Win32Exception exception)
        {
            logger.LogWarning(exception, "No app opened {Url}", url);
        }
    }
}
