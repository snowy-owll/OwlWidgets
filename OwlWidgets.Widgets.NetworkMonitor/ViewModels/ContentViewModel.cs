using System.Globalization;
using System.Windows.Input;
using OwlWidgets.Common;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.NetworkMonitor.Properties;
using OwlWidgets.Widgets.NetworkMonitor.Traffic;

namespace OwlWidgets.Widgets.NetworkMonitor.ViewModels;

internal sealed class ContentViewModel : ViewModelBase
{
    private readonly TrafficSampler? _sampler;
    private readonly IWidgetLogger? _logger;
    private readonly string? _chosenName;
    private CancellationTokenSource? _stopping;

    [Obsolete("Design-time only", true)]
    public ContentViewModel()
    {
        MoveCommand = new RelayCommand(() => { });
        Download = new TrafficDirection(isUpload: false, SpeedUnit.Bits);
        Upload = new TrafficDirection(isUpload: true, SpeedUnit.Bits);
        Header = "Wi-Fi";
        HeaderToolTip = "Intel(R) Wi-Fi 6E AX211 160MHz";
        for (var i = 0; i < TrafficDirection.HistoryLength; i++)
        {
            Download.Add(2_500_000 * (1 + Math.Sin(i / 6.0)) + 300_000 * (i % 7));
            Upload.Add(i is > 20 and < 35 ? 900_000 : 40_000 + 10_000 * (i % 5));
        }
    }

    /// <param name="enterMoveMode">Asks the wrapper for the move mode of the card.</param>
    public ContentViewModel(TrafficSampler sampler, Config config, IWidgetLogger logger, Action enterMoveMode)
    {
        _sampler = sampler;
        _logger = logger;
        _chosenName = config.AdapterName;
        MoveCommand = new RelayCommand(enterMoveMode);
        Download = new TrafficDirection(isUpload: false, config.Unit);
        Upload = new TrafficDirection(isUpload: true, config.Unit);
    }

    public ICommand MoveCommand { get; }

    public TrafficDirection Download { get; }

    public TrafficDirection Upload { get; }

    /// <summary>The name of the connection being watched, or why none is.</summary>
    public string? Header
    {
        get => GetValue(() => Header);
        set => SetValue(() => Header, value);
    }

    /// <summary>The device of the adapter.</summary>
    public string? HeaderToolTip
    {
        get => GetValue(() => HeaderToolTip);
        set => SetValue(() => HeaderToolTip, value);
    }

    internal void Start()
    {
        if (_sampler == null || _stopping != null)
            return;
        _stopping = new CancellationTokenSource();
        _ = RunAsync(_sampler, _stopping.Token);
    }

    /// <summary>The sampler is disposed when the reading in progress, if any, ends.</summary>
    internal void Stop()
    {
        _stopping?.Cancel();
    }

    /// <summary>
    /// Reads off the UI thread once a second. <see cref="Task.Delay(int, CancellationToken)"/> stands while the computer
    /// sleeps, which is right here: the first reading after waking has no speed (<see cref="TrafficSampler"/>), and the graph goes on.
    /// </summary>
    private async Task RunAsync(TrafficSampler sampler, CancellationToken stopping)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    Apply(await Task.Run(sampler.Read, CancellationToken.None));
                }
                catch (Exception exception)
                {
                    _logger?.Log(WidgetLogLevel.Error, "Reading the network counters failed.", exception);
                }

                await Task.Delay(1000 - DateTime.Now.Millisecond, stopping);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            sampler.Dispose();
        }
    }

    private void Apply(TrafficSample sample)
    {
        Header = sample.State switch
        {
            TrafficState.Connected => sample.AdapterName,
            TrafficState.NotConnected => string.Format(CultureInfo.CurrentCulture, Strings.Header_NotConnected, _chosenName),
            _ => Strings.Header_NoConnection
        };
        HeaderToolTip = sample.State == TrafficState.Connected ? sample.AdapterDescription : null;
        Download.Add(sample.Download);
        Upload.Add(sample.Upload);
    }
}
