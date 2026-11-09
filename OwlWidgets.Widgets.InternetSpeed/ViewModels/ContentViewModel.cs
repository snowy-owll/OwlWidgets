using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using OwlWidgets.Common;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.InternetSpeed.Measurement;
using OwlWidgets.Widgets.InternetSpeed.Properties;

namespace OwlWidgets.Widgets.InternetSpeed.ViewModels;

internal sealed class ContentViewModel : ViewModelBase, ISpeedTestObserver
{
    private const string Dash = "—";

    /// <summary>An automatic test does not start sooner than this after the card: the network may still be coming up.</summary>
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions LogJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Config _config;
    private readonly ISpeedTestProvider? _provider;
    private readonly InstanceFile<SpeedTestResult>? _lastResult;
    private readonly InstanceFile<CardState>? _cardState;
    private readonly IWidgetLogger? _logger;
    private SpeedTestResult? _last;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _schedule;
    private CancellationTokenSource? _test;

    [Obsolete("Design-time only", true)]
    public ContentViewModel()
    {
        _config = new Config();
        StartCommand = new RelayCommand(() => { });
        StopCommand = new RelayCommand(() => { });
        RefreshCommand = StartCommand;
        ToggleCollapsedCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
        MoveCommand = new RelayCommand(() => { });
        _last = new SpeedTestResult("Cloudflare", "ARN", DateTimeOffset.Now, 18.4, 2.1, 94.3e6, 41.7e6);
        ShowResult(_last);
        Status = LastStatus(_last);
        Phase = SpeedTestPhase.Download;
        IsRunning = true;
        Speed = 87.2;
        SpeedText = FormatSpeed(87.2e6);
        PhaseText = Strings.Phase_Download;
    }

    /// <param name="enterMoveMode">Asks the wrapper for the move mode of the card.</param>
    public ContentViewModel(Config config, ISpeedTestProvider provider, InstanceFile<SpeedTestResult> lastResult,
        InstanceFile<CardState> cardState, IWidgetLogger logger, Action enterMoveMode)
    {
        _config = config;
        _provider = provider;
        _lastResult = lastResult;
        _cardState = cardState;
        _logger = logger;
        StartCommand = new RelayCommand(StartTest);
        StopCommand = new RelayCommand(StopTest);
        RefreshCommand = StartCommand;
        ToggleCollapsedCommand = new RelayCommand(ToggleCollapsed);
        MoveCommand = new RelayCommand(enterMoveMode);
        IsCollapsed = cardState.Read()?.Collapsed ?? false;
        _last = lastResult.Read();
        if (_last != null)
            ShowResult(_last);
        else
            ClearResult();
        Status = LastStatus(_last);
    }

    public SpeedTestPhase Phase
    {
        get => GetValue(() => Phase);
        set => SetValue(() => Phase, value);
    }

    public bool IsRunning
    {
        get => GetValue(() => IsRunning);
        set => SetValue(() => IsRunning, value);
    }

    /// <summary>Megabits per second on the gauge.</summary>
    public double Speed
    {
        get => GetValue(() => Speed);
        set => SetValue(() => Speed, value);
    }

    public string? SpeedText
    {
        get => GetValue(() => SpeedText);
        set => SetValue(() => SpeedText, value);
    }

    public string? PhaseText
    {
        get => GetValue(() => PhaseText);
        set => SetValue(() => PhaseText, value);
    }

    /// <summary>Provider and server of the test going on or of the result shown.</summary>
    public string? Server
    {
        get => GetValue(() => Server);
        set => SetValue(() => Server, value);
    }

    public string? Ping
    {
        get => GetValue(() => Ping);
        set => SetValue(() => Ping, value);
    }

    /// <summary>Jitter with its unit under the ping; null when the provider does not measure it.</summary>
    public string? Jitter
    {
        get => GetValue(() => Jitter);
        set => SetValue(() => Jitter, value);
    }

    public string? Download
    {
        get => GetValue(() => Download);
        set => SetValue(() => Download, value);
    }

    public string? Upload
    {
        get => GetValue(() => Upload);
        set => SetValue(() => Upload, value);
    }

    public string? Status
    {
        get => GetValue(() => Status);
        set => SetValue(() => Status, value);
    }

    /// <summary>The last test failed: the refresh button is shown.</summary>
    public bool Failed
    {
        get => GetValue(() => Failed);
        set => SetValue(() => Failed, value);
    }

    /// <summary>Hidden: only the icon and the buttons are left of the card. A test goes on meanwhile.</summary>
    public bool IsCollapsed
    {
        get => GetValue(() => IsCollapsed);
        set => SetValue(() => IsCollapsed, value);
    }

    /// <summary>The anchor corner of the instance, from the <c>anchor</c> command of the wrapper.</summary>
    public CardCorner Anchor
    {
        get => GetValue(() => Anchor);
        set
        {
            SetValue(() => Anchor, value);
            OnPropertyChanged(nameof(ButtonCorner));
        }
    }

    /// <summary>Corner of the buttons: the one of the settings, else the anchor, which stays put when the card is hidden.</summary>
    public CardCorner ButtonCorner => _config.ButtonCorner ?? Anchor;

    /// <summary>The last result in one line, the tooltip of the icon of a hidden card; null before the first test.</summary>
    public string? Summary
    {
        get => GetValue(() => Summary);
        set => SetValue(() => Summary, value);
    }

    public ICommand ToggleCollapsedCommand { get; }

    public ICommand MoveCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    /// <summary>Tests again after a failure; the same as the start button.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Runs on the UI thread: the test reports there and the values are set in place.</summary>
    internal void Start()
    {
        _lifetime?.Cancel();
        _lifetime = new CancellationTokenSource();
        Schedule();
    }

    internal void Stop()
    {
        _lifetime?.Cancel();
        _lifetime = null;
        _schedule = null;
        _test = null;
    }

    void ISpeedTestObserver.PhaseStarted(SpeedTestPhase phase)
    {
        Phase = phase;
        Speed = 0;
        SpeedText = phase is SpeedTestPhase.Download or SpeedTestPhase.Upload ? FormatSpeed(0) : null;
        PhaseText = phase switch
        {
            SpeedTestPhase.Connecting => Strings.Phase_Connecting,
            SpeedTestPhase.Latency => Strings.Phase_Latency,
            SpeedTestPhase.Download => Strings.Phase_Download,
            SpeedTestPhase.Upload => Strings.Phase_Upload,
            _ => null
        };
    }

    void ISpeedTestObserver.ServerFound(string server) => Server = $"{_provider!.Name} · {server}";

    void ISpeedTestObserver.LatencyMeasured(LatencyResult latency)
    {
        Ping = FormatMilliseconds(latency.Ping);
        Jitter = latency.Jitter is { } jitter ? string.Format(CultureInfo.CurrentCulture, Strings.Jitter, $"{FormatMilliseconds(jitter)} {Strings.Unit_Ms}") : null;
    }

    void ISpeedTestObserver.SpeedMeasured(SpeedTestPhase phase, double bitsPerSecond)
    {
        Speed = bitsPerSecond / 1e6;
        SpeedText = FormatSpeed(bitsPerSecond);
        if (phase == SpeedTestPhase.Download)
            Download = SpeedText;
        else
            Upload = SpeedText;
    }

    private void ToggleCollapsed()
    {
        IsCollapsed = !IsCollapsed;
        _cardState!.Write(new CardState { Collapsed = IsCollapsed });
    }

    private void StartTest()
    {
        if (IsRunning || _lifetime == null)
            return;
        _ = RunTestAsync(_lifetime.Token);
    }

    private void StopTest()
    {
        if (!IsRunning)
            return;
        _logger?.Log(WidgetLogLevel.Information, "Test stopped by the user.");
        _test?.Cancel();
    }

    /// <summary>
    /// Waits for the next automatic test: the interval after the last result, but not sooner than a minute
    /// after the start. Started over after every test, whoever started it.
    /// </summary>
    private void Schedule()
    {
        _schedule?.Cancel();
        _schedule = null;
        if (_config.AutoInterval <= 0 || _lifetime == null)
            return;
        _schedule = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = ScheduleAsync(_schedule.Token);
    }

    private async Task ScheduleAsync(CancellationToken token)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(_config.AutoInterval, Config.MinAutoInterval));
        var now = DateTimeOffset.Now;
        var due = _last == null ? now : _last.Time + interval;
        if (due < now + StartDelay && !IsRunning)
            due = now + StartDelay;
        try
        {
            await WallClock.DelayAsync(due - now, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!IsRunning && _lifetime != null)
        {
            _logger?.Log(WidgetLogLevel.Information, "Automatic test started.");
            await RunTestAsync(_lifetime.Token);
        }
    }

    private async Task RunTestAsync(CancellationToken lifetime)
    {
        IsRunning = true;
        Failed = false;
        ClearResult();
        Server = _provider!.Name;
        Status = null;
        using var test = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _test = test;
        var options = new SpeedTestOptions(TimeSpan.FromSeconds(Math.Clamp(_config.Duration, Config.MinDuration, Config.MaxDuration)),
            Math.Clamp(_config.Streams, Config.MinStreams, Config.MaxStreams));
        try
        {
            var result = await new SpeedTestRunner(_provider, options, this).RunAsync(test.Token);
            _last = result;
            _lastResult!.Write(result);
            _logger?.Log(WidgetLogLevel.Information, "[speedtest-data] " + JsonSerializer.Serialize(new
            {
                result.Provider,
                result.Server,
                result.Time,
                result.Ping,
                result.Jitter,
                Download = result.Download / 1e6,
                Upload = result.Upload / 1e6
            }, LogJson));
            ShowResult(result);
            Status = LastStatus(result);
        }
        catch (OperationCanceledException) when (test.IsCancellationRequested)
        {
            if (lifetime.IsCancellationRequested)
                return;
            RestoreLast(_last == null ? Strings.Status_Stopped : Strings.Status_StoppedLast);
        }
        catch (Exception exception)
        {
            _logger?.Log(WidgetLogLevel.Warning, $"Test with {_provider.Name} failed.", exception);
            Failed = true;
            RestoreLast(_last == null ? Strings.Status_Failed : Strings.Status_FailedLast);
        }
        finally
        {
            if (_test == test)
                _test = null;
            if (!lifetime.IsCancellationRequested)
            {
                IsRunning = false;
                Phase = SpeedTestPhase.Idle;
                Speed = 0;
                SpeedText = null;
                PhaseText = null;
                Schedule();
            }
        }
    }

    /// <summary>A test that did not end keeps nothing of its own: the last result comes back.</summary>
    private void RestoreLast(string status)
    {
        if (_last != null)
            ShowResult(_last);
        else
            ClearResult();
        Status = _last == null ? status : string.Format(CultureInfo.CurrentCulture, status, FormatTime(_last.Time));
    }

    private void ShowResult(SpeedTestResult result)
    {
        Server = $"{result.Provider} · {result.Server}";
        Ping = result.Ping is { } ping ? FormatMilliseconds(ping) : Dash;
        Jitter = result.Jitter is { } jitter ? string.Format(CultureInfo.CurrentCulture, Strings.Jitter, $"{FormatMilliseconds(jitter)} {Strings.Unit_Ms}") : null;
        Download = FormatSpeed(result.Download);
        Upload = FormatSpeed(result.Upload);
        Summary = string.Format(CultureInfo.CurrentCulture, Strings.Collapsed_Result, Ping, Download, Upload);
    }

    /// <summary>The summary stays: a hidden card still names the last result while a new test goes.</summary>
    private void ClearResult()
    {
        Server = _provider?.Name;
        Ping = Dash;
        Jitter = null;
        Download = Dash;
        Upload = Dash;
    }

    private static string LastStatus(SpeedTestResult? result) =>
        result == null ? Strings.Status_Never : string.Format(CultureInfo.CurrentCulture, Strings.Status_Last, FormatTime(result.Time));

    /// <summary>Megabits per second with fewer decimals as the number grows: 4.37, 43.7, 437.</summary>
    private static string FormatSpeed(double bitsPerSecond)
    {
        var mbps = bitsPerSecond / 1e6;
        return mbps.ToString(mbps < 10 ? "N2" : mbps < 100 ? "N1" : "N0", CultureInfo.CurrentCulture);
    }

    private static string FormatMilliseconds(double milliseconds) =>
        milliseconds.ToString(milliseconds < 10 ? "N1" : "N0", CultureInfo.CurrentCulture);

    /// <summary>Time only for today, the date as well for an older test.</summary>
    private static string FormatTime(DateTimeOffset time)
    {
        var local = time.LocalDateTime;
        return local.ToString(local.Date == DateTime.Today ? "t" : "g", CultureInfo.CurrentCulture);
    }
}
