using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.SystemMonitor.Sensors;

/// <summary>
/// Logs the failure of a source read every second once, until it works again, so a lasting failure
/// does not fill the log.
/// </summary>
internal sealed class SourceLog(IWidgetLogger logger, string source)
{
    private bool _failing;

    public void Failed(Exception exception)
    {
        if (_failing)
            return;
        _failing = true;
        logger.Log(WidgetLogLevel.Warning, $"{source} failed; it is read again every second.", exception);
    }

    public void Succeeded()
    {
        if (!_failing)
            return;
        _failing = false;
        logger.Log(WidgetLogLevel.Information, $"{source} works again.");
    }
}
