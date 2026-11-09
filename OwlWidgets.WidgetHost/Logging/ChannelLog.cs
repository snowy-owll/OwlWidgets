using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;
using OwlWidgets.Protocol;

namespace OwlWidgets.WidgetHost.Logging;

/// <summary>
/// The wrapper does not write log files: every record goes to the manager over the channel.
/// Records made before the channel is open wait in a short queue.
/// </summary>
internal sealed class ChannelLog
{
    private const int MaxPending = 1000;

    private readonly object _gate = new();
    private readonly Queue<LogRecord> _pending = new();
    private Action<LogRecord>? _send;

    /// <summary>Everything is queued until the manager sends its level in Welcome.</summary>
    public WidgetLogLevel MinimumLevel { get; set; } = WidgetLogLevel.Trace;

    public bool IsEnabled(WidgetLogLevel level) => level >= MinimumLevel;

    public void Write(LogRecord record)
    {
        if (!IsEnabled(record.Level))
            return;

        lock (_gate)
        {
            if (_send != null)
            {
                Deliver(record);
                return;
            }

            if (_pending.Count == MaxPending)
                _pending.Dequeue();
            _pending.Enqueue(record);
        }
    }

    /// <summary>Starts sending records, beginning with the queued ones.</summary>
    public void Attach(Action<LogRecord> send)
    {
        lock (_gate)
        {
            _send = send;
            while (_pending.TryDequeue(out var record))
            {
                if (IsEnabled(record.Level))
                    Deliver(record);
            }
        }
    }

    private void Deliver(LogRecord record)
    {
        try
        {
            _send!(record);
        }
        catch (Exception exception)
        {
            // The channel is gone; the debugger output is the last place the record can go.
            Debug.WriteLine($"Log record was not sent: {exception.Message}{Environment.NewLine}"
                + $"[{record.Level}] [{record.Source}] {record.Category}: {record.Message}{Environment.NewLine}{record.Exception}");
        }
    }
}

internal sealed class ChannelLoggerProvider : ILoggerProvider
{
    private readonly ChannelLog _log;

    public ChannelLoggerProvider(ChannelLog log)
    {
        _log = log;
    }

    public ILogger CreateLogger(string categoryName) =>
        new RecordLogger(categoryName, LogSources.Host, _log.IsEnabled, _log.Write);

    public void Dispose()
    {
    }
}

/// <summary>
/// <see cref="IWidgetLogger"/> given to the widget through its host context.
/// </summary>
internal sealed class WidgetLogger : IWidgetLogger
{
    private readonly ChannelLog _log;
    private readonly string _category;

    public WidgetLogger(ChannelLog log, string category)
    {
        _log = log;
        _category = RecordLogger.ShortCategory(category);
    }

    public void Log(WidgetLogLevel level, string message, Exception? exception = null) =>
        _log.Write(new LogRecord
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Source = LogSources.Widget,
            Category = _category,
            Message = message,
            Exception = exception?.ToString()
        });
}
