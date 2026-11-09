using System.Text;
using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;

namespace OwlWidgets.Protocol;

public static class LogSources
{
    public const string Manager = "Manager";
    public const string Host = "Host";
    public const string Widget = "Widget";
}

/// <summary>
/// One log entry. The wrapper sends it to the manager as the body of a <see cref="ChannelCodes.Log"/> frame;
/// only the manager writes entries to disk.
/// </summary>
public sealed class LogRecord
{
    public DateTimeOffset Timestamp { get; set; }

    public WidgetLogLevel Level { get; set; } = WidgetLogLevel.Information;

    public string Source { get; set; } = LogSources.Manager;

    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }
}

public static class LogLevels
{
    /// <summary>One level for the whole application, chosen by the build configuration.</summary>
    public const WidgetLogLevel BuildMinimum =
#if DEBUG
        WidgetLogLevel.Debug;
#else
        WidgetLogLevel.Information;
#endif

    public static WidgetLogLevel ToWidget(LogLevel level) => level switch
    {
        LogLevel.Trace => WidgetLogLevel.Trace,
        LogLevel.Debug => WidgetLogLevel.Debug,
        LogLevel.Information => WidgetLogLevel.Information,
        LogLevel.Warning => WidgetLogLevel.Warning,
        LogLevel.Error => WidgetLogLevel.Error,
        _ => WidgetLogLevel.Critical
    };
}

/// <summary>
/// <see cref="ILogger"/> that turns each call into a <see cref="LogRecord"/> and hands it to a sink.
/// </summary>
public sealed class RecordLogger : ILogger
{
    private readonly string _category;
    private readonly string _source;
    private readonly Func<WidgetLogLevel, bool> _isEnabled;
    private readonly Action<LogRecord> _write;

    public RecordLogger(string category, string source, Func<WidgetLogLevel, bool> isEnabled, Action<LogRecord> write)
    {
        _category = ShortCategory(category);
        _source = source;
        _isEnabled = isEnabled;
        _write = write;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && _isEnabled(LogLevels.ToWidget(logLevel));

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        _write(new LogRecord
        {
            Timestamp = DateTimeOffset.Now,
            Level = LogLevels.ToWidget(logLevel),
            Source = _source,
            Category = _category,
            Message = formatter(state, exception),
            Exception = exception?.ToString()
        });
    }

    public static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot < 0 ? category : category[(dot + 1)..];
    }
}

/// <summary>
/// Text form of a command for debug logs.
/// </summary>
public static class CommandText
{
    /// <summary>A body is logged as text only up to this size and only when it is valid UTF-8.</summary>
    public const int MaxBodyTextBytes = 2 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Describe(WidgetCommand command) =>
        $"\"{command.Code}\" to {command.Target}{DescribeBody(command.Body)}";

    private static string DescribeBody(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
            return string.Empty;

        if (body.Length <= MaxBodyTextBytes)
        {
            try
            {
                var text = StrictUtf8.GetString(body.Span);
                if (!text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                    return ": " + text.Replace("\r", "\\r").Replace("\n", "\\n");
            }
            catch (DecoderFallbackException)
            {
                // Not UTF-8: fall through to the length.
            }
        }

        return $" ({body.Length} bytes)";
    }
}
