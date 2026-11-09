using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;
using OwlWidgets.Protocol;

namespace OwlWidgets.Logging;

/// <summary>
/// The only place that writes logs to disk. Manager records go to <c>Data/logs/manager</c>,
/// records of an instance (from the manager, the wrapper or the widget) go to <c>Data/logs/{instanceId}</c>.
/// Every record written also goes to <see cref="LogFeed"/>, the records of this session for the log screen.
/// </summary>
internal sealed class ManagerLog : IDisposable
{
    public const string ManagerFolder = "manager";

    private readonly LogFileStore _store;
    private readonly LogFeed _feed;

    public ManagerLog(LogFileStore store, LogFeed feed)
    {
        _store = store;
        _feed = feed;
    }

    public WidgetLogLevel MinimumLevel => LogLevels.BuildMinimum;

    public string Root => _store.Root;

    public bool IsEnabled(WidgetLogLevel level) => level >= MinimumLevel;

    public void Initialize()
    {
        try
        {
            _store.RemoveLegacyFiles();
            _store.Cleanup();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Log cleanup failed: {exception}");
        }
    }

    /// <summary>
    /// Writes a record to the manager log. <paramref name="instanceId"/> marks lines that concern an instance.
    /// </summary>
    public void WriteManager(LogRecord record, Guid? instanceId = null)
    {
        if (!IsEnabled(record.Level))
            return;
        Append(ManagerFolder, LogLineFormatter.Format(record, instanceId));
        _feed.AddManager(record, instanceId);
    }

    public void WriteInstance(Guid instanceId, LogRecord record)
    {
        if (!IsEnabled(record.Level))
            return;
        Append(InstanceFolder(instanceId), LogLineFormatter.Format(record, instanceId: null));
        _feed.AddInstance(instanceId, record);
    }

    /// <summary>
    /// Logger for manager code that works with one instance. Its records go to the instance log
    /// and, when <paramref name="alsoToManager"/> is set, to the manager log as well.
    /// </summary>
    public ILogger CreateInstanceLogger(string category, Guid instanceId, bool alsoToManager) =>
        new RecordLogger(category, LogSources.Manager, IsEnabled, record =>
        {
            WriteInstance(instanceId, record);
            if (alsoToManager)
                WriteManager(record, instanceId);
        });

    public void RemoveInstance(Guid instanceId)
    {
        _feed.RemoveInstance(instanceId);
        _store.RemoveFolder(InstanceFolder(instanceId));
    }

    public void Dispose() => _store.Dispose();

    private static string InstanceFolder(Guid instanceId) => instanceId.ToString("N");

    private void Append(string folder, string text)
    {
        try
        {
            _store.Append(folder, text);
        }
        catch (Exception exception)
        {
            // A failed write must never break the caller.
            Debug.WriteLine($"Log write to {folder} failed: {exception.Message}{Environment.NewLine}{text}");
        }
    }
}

internal static class LogLineFormatter
{
    /// <summary>
    /// <c>2026-10-04 14:23:05.123 +03:00 [INF] [Manager] {instanceId} Category: message</c>,
    /// followed by the exception on the next lines.
    /// </summary>
    public static string Format(LogRecord record, Guid? instanceId)
    {
        var line = new StringBuilder(128);
        line.Append(record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        line.Append(" [").Append(LevelText(record.Level)).Append("] [").Append(record.Source).Append("] ");
        if (instanceId is { } id)
            line.Append('{').Append(id.ToString("N")).Append("} ");
        if (!string.IsNullOrEmpty(record.Category))
            line.Append(record.Category).Append(": ");
        line.Append(record.Message).AppendLine();
        if (!string.IsNullOrEmpty(record.Exception))
            line.AppendLine(record.Exception);
        return line.ToString();
    }

    private static string LevelText(WidgetLogLevel level) => level switch
    {
        WidgetLogLevel.Trace => "TRC",
        WidgetLogLevel.Debug => "DBG",
        WidgetLogLevel.Information => "INF",
        WidgetLogLevel.Warning => "WRN",
        WidgetLogLevel.Error => "ERR",
        _ => "CRT"
    };
}

/// <summary>
/// Provider behind <see cref="ILogger{TCategoryName}"/> in the manager: writes to the manager log.
/// </summary>
internal sealed class ManagerLoggerProvider : ILoggerProvider
{
    private readonly ManagerLog _log;

    public ManagerLoggerProvider(ManagerLog log)
    {
        _log = log;
    }

    public ILogger CreateLogger(string categoryName) =>
        new RecordLogger(categoryName, LogSources.Manager, _log.IsEnabled, record => _log.WriteManager(record));

    public void Dispose()
    {
    }
}
