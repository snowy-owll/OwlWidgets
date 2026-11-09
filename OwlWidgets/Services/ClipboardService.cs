using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OwlWidgets.Services;

/// <summary>The Windows clipboard.</summary>
internal interface IClipboardService
{
    void SetText(string text);
}

internal sealed class ClipboardService : IClipboardService
{
    private readonly ILogger<ClipboardService> _log;

    public ClipboardService(ILogger<ClipboardService> log) => _log = log;

    public void SetText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (COMException exception)
        {
            // Another process holds the clipboard open; copying is not worth more than a log line.
            _log.LogWarning(exception, "Clipboard is busy, text of {Length} characters not copied", text.Length);
        }
    }
}
