using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using OwlWidgets.Common;
using OwlWidgets.Common.Bluetooth;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.BluetoothAudio.Properties;

namespace OwlWidgets.Widgets.BluetoothAudio.ViewModels;

internal enum DeviceState
{
    Unknown,
    NotFound,
    Unavailable,
    Disconnected,
    Connected
}

/// <summary>
/// Runs on the UI thread. Core Audio is asked on the thread pool; endpoint notifications come on a Core Audio
/// thread and are posted here. A request is followed by a short poll as well, in case a notification is missed.
/// </summary>
internal sealed class ContentViewModel : ViewModelBase
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PendingPoll = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NotificationDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FallbackPoll = TimeSpan.FromSeconds(5);

    private readonly string? _address;
    private readonly string? _title;
    private readonly IWidgetLogger? _logger;
    private Dispatcher? _dispatcher;
    private CancellationTokenSource? _cancellation;
    private AudioEndpointWatcher? _watcher;
    private BluetoothAudioDevice? _device;
    private DeviceState _state;

    /// <summary>The state a request waits for: true connected, false disconnected; null: no request.</summary>
    private bool? _pending;

    private int _operation;
    private string? _failure;
    private string? _failureHint;
    private bool _refreshing;
    private bool _refreshAgain;
    private bool _refreshScheduled;

    [Obsolete("Design-time only", true)]
    public ContentViewModel()
    {
        ToggleCommand = new RelayCommand(() => { });
        OpenSettingsCommand = new RelayCommand(() => { });
        Title = "soundcore Space Q45";
        Status = Strings.State_Connected;
        IsConnected = true;
        CanToggle = true;
        ActionText = Strings.Action_Disconnect;
    }

    public ContentViewModel(Config config, IWidgetLogger logger, Action openSettings)
    {
        var address = BluetoothAddress.Normalize(config.Address);
        _address = address.Length == 0 ? null : address;
        _title = string.IsNullOrWhiteSpace(config.Title) ? null : config.Title.Trim();
        _logger = logger;
        IsEmpty = _address == null;
        ToggleCommand = new RelayCommand(Toggle);
        OpenSettingsCommand = new RelayCommand(openSettings);
        UpdateView();
    }

    public bool IsEmpty { get; }

    public string? Title
    {
        get => GetValue(() => Title);
        set => SetValue(() => Title, value);
    }

    public string? Status
    {
        get => GetValue(() => Status);
        set => SetValue(() => Status, value);
    }

    /// <summary>
    /// The tooltip of the status: the status keeps one line so the card does not change its height,
    /// what does not fit is said here. Null: no tooltip.
    /// </summary>
    public string? StatusHint
    {
        get => GetValue(() => StatusHint);
        set => SetValue(() => StatusHint, value);
    }

    public bool IsConnected
    {
        get => GetValue(() => IsConnected);
        set => SetValue(() => IsConnected, value);
    }

    public bool IsBusy
    {
        get => GetValue(() => IsBusy);
        set => SetValue(() => IsBusy, value);
    }

    /// <summary>The last request did not reach its state.</summary>
    public bool IsFailed
    {
        get => GetValue(() => IsFailed);
        set => SetValue(() => IsFailed, value);
    }

    public bool CanToggle
    {
        get => GetValue(() => CanToggle);
        set => SetValue(() => CanToggle, value);
    }

    public string? ActionText
    {
        get => GetValue(() => ActionText);
        set => SetValue(() => ActionText, value);
    }

    public ICommand ToggleCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    internal void Start()
    {
        if (IsEmpty)
            return;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        // Through the dispatcher: the awaits in it come back to the UI thread wherever Start is called from.
        _dispatcher.InvokeAsync(() => WatchAsync(token));
    }

    internal void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
        var watcher = _watcher;
        _watcher = null;
        if (watcher != null)
        {
            watcher.Changed -= OnEndpointsChanged;
            _ = Task.Run(watcher.Dispose);
        }
    }

    private async Task WatchAsync(CancellationToken token)
    {
        AudioEndpointWatcher? watcher = null;
        try
        {
            watcher = await Task.Run(() => new AudioEndpointWatcher());
        }
        catch (Exception exception)
        {
            _logger?.Log(WidgetLogLevel.Warning,
                $"Audio endpoint notifications are unavailable, the state is checked every {FallbackPoll.TotalSeconds:0} s.", exception);
        }

        if (token.IsCancellationRequested)
        {
            if (watcher != null)
                _ = Task.Run(watcher.Dispose);
            return;
        }

        if (watcher != null)
        {
            _watcher = watcher;
            watcher.Changed += OnEndpointsChanged;
        }

        await RefreshAsync(token);
        if (watcher != null)
            return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FallbackPoll, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RefreshAsync(token);
        }
    }

    /// <summary>Core Audio thread: one connection changes several endpoints, so the refresh waits a little.</summary>
    private void OnEndpointsChanged(object? sender, EventArgs e) =>
        _dispatcher?.BeginInvoke(() =>
        {
            var token = _cancellation?.Token;
            if (_refreshScheduled || token is not { IsCancellationRequested: false } live)
                return;
            _refreshScheduled = true;
            _ = RefreshLaterAsync(live);
        });

    private async Task RefreshLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(NotificationDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _refreshScheduled = false;
        }

        await RefreshAsync(token);
    }

    /// <summary>A refresh asked for while one runs is done once after it.</summary>
    private async Task RefreshAsync(CancellationToken token)
    {
        if (_address == null)
            return;
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                BluetoothAudioDevice? device = null;
                DeviceState state;
                try
                {
                    var address = _address;
                    device = await Task.Run(() => BluetoothAudioDevices.Find(address));
                    state = device == null ? DeviceState.NotFound : device.IsConnected ? DeviceState.Connected : DeviceState.Disconnected;
                }
                catch (Exception exception)
                {
                    if (_state != DeviceState.Unavailable && !token.IsCancellationRequested)
                        _logger?.Log(WidgetLogLevel.Warning, $"Audio devices of {_address} were not read.", exception);
                    state = DeviceState.Unavailable;
                }

                if (token.IsCancellationRequested)
                    return;
                Apply(device, state);
            }
            while (_refreshAgain);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(BluetoothAudioDevice? device, DeviceState state)
    {
        _device = device ?? _device;
        if (state != _state)
        {
            _logger?.Log(WidgetLogLevel.Information, $"{DeviceText()}: {_state} -> {state}.");
            if (_state != DeviceState.Unknown)
                _failure = null;
            _state = state;
        }

        if (_pending is bool target && state == (target ? DeviceState.Connected : DeviceState.Disconnected))
            _pending = null;
        UpdateView();
    }

    private void Toggle()
    {
        if (_address == null || _pending != null || _cancellation is not { } cancellation)
            return;
        if (_state is not (DeviceState.Connected or DeviceState.Disconnected))
            return;

        var connect = _state != DeviceState.Connected;
        _pending = connect;
        _failure = null;
        var operation = ++_operation;
        UpdateView();
        _logger?.Log(WidgetLogLevel.Information, $"{DeviceText()}: {(connect ? "connection" : "disconnection")} requested.");
        _ = RequestAsync(operation, connect, cancellation.Token);
    }

    /// <summary>
    /// The driver takes a request even when the device does not follow, for example when it is busy with a phone:
    /// only the endpoints tell the result, so the request waits for them until its timeout.
    /// </summary>
    private async Task RequestAsync(int operation, bool connect, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        var address = _address!;
        try
        {
            await Task.Run(() => BluetoothAudioDevices.RequestConnection(address, connect));
        }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested || operation != _operation)
                return;
            _logger?.Log(WidgetLogLevel.Warning, $"{DeviceText()}: the request was not taken.", exception);
            Fail(connect);
            return;
        }

        var timeout = connect ? ConnectTimeout : DisconnectTimeout;
        while (operation == _operation && _pending != null && elapsed.Elapsed < timeout)
        {
            try
            {
                await Task.Delay(PendingPoll, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RefreshAsync(token);
        }

        if (operation != _operation || token.IsCancellationRequested)
            return;
        if (_pending == null)
        {
            _logger?.Log(WidgetLogLevel.Debug, $"{DeviceText()}: {(connect ? "connected" : "disconnected")} in {elapsed.ElapsedMilliseconds} ms.");
            return;
        }

        _logger?.Log(WidgetLogLevel.Warning,
            $"{DeviceText()}: not {(connect ? "connected" : "disconnected")} in {timeout.TotalSeconds:0} s.");
        Fail(connect);
    }

    private void Fail(bool connect)
    {
        _pending = null;
        _failure = connect ? Strings.State_ConnectFailed : Strings.State_DisconnectFailed;
        _failureHint = connect ? Strings.State_ConnectFailedHint : null;
        UpdateView();
    }

    private void UpdateView()
    {
        Title = _title ?? _device?.Name ?? (_address == null ? Strings.Name : BluetoothAddress.Format(_address));
        IsConnected = _state == DeviceState.Connected;
        IsBusy = _pending != null;
        IsFailed = _pending == null && _failure != null;
        CanToggle = _pending == null && _state is DeviceState.Connected or DeviceState.Disconnected;
        ActionText = _pending switch
        {
            true => Strings.Action_Connect,
            false => Strings.Action_Disconnect,
            null => IsConnected ? Strings.Action_Disconnect : Strings.Action_Connect
        };
        Status = _pending switch
        {
            true => Strings.State_Connecting,
            false => Strings.State_Disconnecting,
            null => _failure ?? _state switch
            {
                DeviceState.Connected => Strings.State_Connected,
                DeviceState.Disconnected => Strings.State_Disconnected,
                DeviceState.NotFound => Strings.State_NotFound,
                DeviceState.Unavailable => Strings.State_Unavailable,
                _ => null
            }
        };
        // Long states may be cut by the ellipsis, the tooltip gives them in full.
        StatusHint = _pending != null ? null
            : _failure != null ? _failureHint
            : _state is DeviceState.NotFound or DeviceState.Unavailable ? Status
            : null;
    }

    private string DeviceText() => $"{_device?.Name ?? "device"} ({_address})";
}
