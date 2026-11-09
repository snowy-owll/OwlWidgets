namespace OwlWidgets.Contracts;

public interface IWidgetLogger
{
    void Log(WidgetLogLevel level, string message, Exception? exception = null);
}

public enum WidgetLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
