using OwlWidgets.Common;
using OwlWidgets.Widgets.NetworkMonitor.Traffic;

namespace OwlWidgets.Widgets.NetworkMonitor.ViewModels;

/// <summary>The download or the upload: the current speed and the graph of the last minute.</summary>
internal sealed class TrafficDirection : ViewModelBase
{
    /// <summary>Readings in the graph: a minute at one a second.</summary>
    public const int HistoryLength = 60;

    private readonly Queue<double> _history = new(HistoryLength);
    private readonly SpeedUnit _unit;

    public TrafficDirection(bool isUpload, SpeedUnit unit)
    {
        IsUpload = isUpload;
        _unit = unit;
        Speed = "—";
        Top = SpeedFormat.Top(0, unit);
        TopText = SpeedFormat.Speed(Top, unit);
    }

    public bool IsUpload { get; }

    public string? Speed
    {
        get => GetValue(() => Speed);
        set => SetValue(() => Speed, value);
    }

    /// <summary>Bytes per second at the top of the graph.</summary>
    public double Top
    {
        get => GetValue(() => Top);
        set => SetValue(() => Top, value);
    }

    public string? TopText
    {
        get => GetValue(() => TopText);
        set => SetValue(() => TopText, value);
    }

    /// <summary>Bytes per second, the oldest first; a new array after every reading.</summary>
    public double[] History
    {
        get => GetValue(() => History) ?? [];
        set => SetValue(() => History, value);
    }

    /// <summary>
    /// A reading without a speed (the first one, after a sleep, with no adapter) goes to the graph as 0
    /// and shows a dash, so the graph keeps moving with the clock.
    /// </summary>
    public void Add(double? bytesPerSecond)
    {
        if (_history.Count == HistoryLength)
            _history.Dequeue();
        _history.Enqueue(bytesPerSecond ?? 0);
        Speed = bytesPerSecond is { } value ? SpeedFormat.Speed(value, _unit) : "—";
        var top = SpeedFormat.Top(_history.Max(), _unit);
        if (top != Top)
        {
            Top = top;
            TopText = SpeedFormat.Speed(top, _unit);
        }

        History = [.. _history];
    }
}
