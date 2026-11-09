using System.Diagnostics;

namespace OwlWidgets.Widgets.InternetSpeed.Measurement;

/// <summary>
/// Counts the bytes of one transfer. The connections add to it from any thread; the runner samples it
/// on its own thread and turns the samples into the current speed and the result.
/// </summary>
internal sealed class ThroughputMeter
{
    /// <summary>
    /// The current speed is the average over this window: one slow read does not drop the needle,
    /// and a total reported by the server, which comes every quarter of a second or less often, does not jerk it.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The start of a transfer is not counted in the result: TCP is still growing its window,
    /// and an upload fills the send buffers faster than the line takes it.
    /// </summary>
    private static readonly TimeSpan MaxWarmUp = TimeSpan.FromSeconds(2);

    private readonly Stopwatch _clock = new();
    private readonly List<(TimeSpan Time, long Bytes)> _samples = [];
    private long _bytes;

    private volatile Func<long?>? _acknowledged;
    private long _acknowledgedFrom;

    /// <summary>Bytes received or written by this side.</summary>
    public void Add(long bytes) => Interlocked.Add(ref _bytes, bytes);

    /// <summary>
    /// Counts an upload by what the other side acknowledged, read at every sample, e.g. from
    /// <see cref="TestSockets.Acknowledged"/>; called before the first byte goes. A write completes as soon as the bytes
    /// are in the send buffer, so counting writes runs ahead while the buffers fill and stands still while they drain.
    /// <see cref="Add"/> goes on alongside: when the source gives null, the meter falls back to it for good.
    /// </summary>
    public void CountAcknowledged(Func<long?> acknowledged)
    {
        if (acknowledged() is not { } start)
            return;
        _acknowledgedFrom = start;
        _acknowledged = acknowledged;
    }

    internal void Start()
    {
        _samples.Clear();
        _clock.Restart();
        _samples.Add((TimeSpan.Zero, Total()));
    }

    internal void Sample() => _samples.Add((_clock.Elapsed, Total()));

    private long Total()
    {
        if (_acknowledged is { } acknowledged)
        {
            if (acknowledged() is { } total)
                return total - _acknowledgedFrom;
            _acknowledged = null;
        }

        return Interlocked.Read(ref _bytes);
    }

    /// <summary>Bits per second over the last second of samples.</summary>
    internal double Current
    {
        get
        {
            var last = _samples[^1];
            var first = _samples.FindLast(sample => last.Time - sample.Time >= Window);
            if (first == default)
                first = _samples[0];
            return Rate(first, last);
        }
    }

    /// <summary>
    /// Bits per second from the end of the warm-up to the last sample; null when nothing was moved.
    /// The warm-up is 2 seconds, or a quarter of a shorter transfer, when the server ended it early.
    /// </summary>
    internal double? Result
    {
        get
        {
            var last = _samples[^1];
            if (last.Bytes == 0 || last.Time <= TimeSpan.Zero)
                return null;
            var warmUp = TimeSpan.FromTicks(Math.Min(MaxWarmUp.Ticks, last.Time.Ticks / 4));
            var first = _samples.Find(sample => sample.Time >= warmUp);
            if (first == default || first == last)
                first = _samples[0];
            return Rate(first, last);
        }
    }

    private static double Rate((TimeSpan Time, long Bytes) from, (TimeSpan Time, long Bytes) to)
    {
        var seconds = (to.Time - from.Time).TotalSeconds;
        return seconds <= 0 ? 0 : (to.Bytes - from.Bytes) * 8 / seconds;
    }
}
