using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;
using OwlWidgets.Logging;
using OwlWidgets.Properties;
using OwlWidgets.Protocol;
using OwlWidgets.Services;
using OwlWidgets.Settings;

namespace OwlWidgets.Hosting;

internal enum InstanceState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    RestartBackoff,
    Faulted
}

internal sealed class InstanceSession : INotifyPropertyChanged, IMovableInstance
{
    private readonly WidgetPackage _package;
    private readonly InstanceRecord _record;
    private readonly InstanceRegistry _registry;
    private readonly ManagerLog _managerLog;
    private readonly IUiDispatcher _ui;
    private readonly AppPaths _paths;
    private readonly IInstanceSettingsStore _settings;
    private readonly IDesktopLayer _desktop;
    private readonly INotificationService _notifications;
    private readonly ThemeService _theme;
    private readonly LanguageService _language;
    private readonly ILogger _log;
    private readonly ILogger _channel;

    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private CancellationTokenSource? _io;
    private CancellationTokenSource? _backoff;
    private string? _welcomeSettings;
    private CardCorner _welcomeAnchor;
    private string? _settingsJson;
    private bool _settingsJsonRead;
    private IntPtr _hwnd;
    private int _generation;
    private int _crashCount;
    private bool _expectExit;
    private bool _welcomed;
    private bool _moveMode;
    private InstanceState _state = InstanceState.Stopped;

    public InstanceSession(WidgetPackage package, InstanceRecord record, InstanceRegistry registry, ManagerLog managerLog,
        IUiDispatcher ui, AppPaths paths, IInstanceSettingsStore settings, IDesktopLayer desktop,
        INotificationService notifications, ThemeService theme, LanguageService language)
    {
        _package = package;
        _record = record;
        _registry = registry;
        _managerLog = managerLog;
        _theme = theme;
        _language = language;
        _ui = ui;
        _paths = paths;
        _settings = settings;
        _desktop = desktop;
        _notifications = notifications;
        _log = managerLog.CreateInstanceLogger(nameof(InstanceSession), record.InstanceId, alsoToManager: true);
        _channel = managerLog.CreateInstanceLogger("Channel", record.InstanceId, alsoToManager: false);
    }

    /// <summary>Instance events: written to the instance log and to the manager log.</summary>
    public ILogger Log => _log;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after <see cref="State"/> changed and its dependent properties were notified.</summary>
    public event Action<InstanceSession>? StateChanged;

    /// <summary>The wrapper left the move mode; true: the position was saved.</summary>
    public event Action<InstanceSession, bool>? MoveModeEnded;

    /// <summary>The widget asked to show its settings in the manager window.</summary>
    public event Action<InstanceSession>? SettingsRequested;

    /// <summary>
    /// The widget wrote its settings itself (<c>persist-settings</c>); an open settings screen shows them,
    /// unsaved changes on it are dropped. The file is already written.
    /// </summary>
    public event Action<string>? SettingsPersisted;

    public Guid InstanceId => _record.InstanceId;

    public Guid WidgetId => _record.WidgetId;

    /// <summary>The widget type name.</summary>
    public string Name => _package.Descriptor.Name;

    /// <summary>
    /// The user's name of the instance, trimmed; null when it is not set. Uniqueness is not required.
    /// </summary>
    public string? Tag
    {
        get => _record.Tag;
        set
        {
            var tag = NormalizeTag(value);
            if (_record.Tag == tag)
                return;
            _log.LogInformation("Tag changed: \"{OldTag}\" -> \"{NewTag}\"", _record.Tag, tag);
            _record.Tag = tag;
            Notify(nameof(Tag));
            Notify(nameof(Title));
            Notify(nameof(Caption));
            _registry.Save();
        }
    }

    /// <summary>What the instance is called on screen: its tag, otherwise the widget type name.</summary>
    public string Title => Tag ?? Name;

    /// <summary>The second line in the instance list: the type, when the tag hides it, and the state.</summary>
    public string Caption => Tag == null ? StateText : $"{Name} · {StateText}";

    public string Description => _package.Descriptor.Description;

    /// <summary>The icon of the widget type; null when it has none.</summary>
    public ImageSource? Icon => _package.Icon;

    public string Version => _package.Descriptor.Version;

    public string Author => _package.Descriptor.Author;

    public bool AllowMultipleInstances => _package.Descriptor.AllowMultipleInstances;

    public InstanceState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            _log.LogInformation("State changed: {OldState} -> {NewState}", _state, value);
            _state = value;
            Notify(nameof(State));
            Notify(nameof(StateText));
            Notify(nameof(Caption));
            Notify(nameof(CanStart));
            Notify(nameof(CanStop));
            Notify(nameof(CanMove));
            Notify(nameof(CanEditPosition));
            StateChanged?.Invoke(this);
        }
    }

    public string StateText => State switch
    {
        InstanceState.Running => Strings.State_Running,
        InstanceState.Starting => Strings.State_Starting,
        InstanceState.Stopping => Strings.State_Stopping,
        InstanceState.RestartBackoff => Strings.State_RestartBackoff,
        InstanceState.Faulted => Strings.State_Faulted,
        _ => Strings.State_Stopped
    };

    public bool CanStart => State is InstanceState.Stopped or InstanceState.Faulted or InstanceState.RestartBackoff;

    public bool CanStop => State is InstanceState.Starting or InstanceState.Running or InstanceState.RestartBackoff;

    public bool CanMove => State == InstanceState.Running && !_moveMode;

    public bool CanEditPosition => !_moveMode;

    public bool MoveMode
    {
        get => _moveMode;
        private set
        {
            if (_moveMode == value)
                return;
            _log.LogInformation(value ? "Move mode entered" : "Move mode left");
            _moveMode = value;
            Notify(nameof(MoveMode));
            Notify(nameof(CanMove));
            Notify(nameof(CanEditPosition));
        }
    }

    public double X => _record.X;

    public double Y => _record.Y;

    /// <summary>The corner of the card that <see cref="X"/> and <see cref="Y"/> are given for.</summary>
    public CardCorner Anchor => _record.Anchor;

    /// <summary>
    /// Gives the position for another corner without moving the card. A running card is asked: the wrapper knows
    /// its size and answers with the new position. Otherwise the position is converted by the last reported size;
    /// without one the numbers stay, and the card comes up with the new corner there.
    /// Ignored in the move mode, as the position is.
    /// </summary>
    public void SetAnchor(CardCorner anchor)
    {
        if (_moveMode || _record.Anchor == anchor)
            return;
        var old = _record.Anchor;
        _record.Anchor = anchor;
        if (State == InstanceState.Running)
        {
            _log.LogInformation("Anchor changed: {OldAnchor} -> {NewAnchor}, asking the card for its position", old, anchor);
            SendAnchor();
        }
        else if (_record.CardWidth > 0 && _record.CardHeight > 0)
        {
            var x = _record.X + Offset(anchor.IsRight(), old.IsRight(), _record.CardWidth);
            var y = _record.Y + Offset(anchor.IsBottom(), old.IsBottom(), _record.CardHeight);
            _log.LogInformation("Anchor changed: {OldAnchor} -> {NewAnchor}, position ({OldX}, {OldY}) -> ({NewX}, {NewY}) by the size {Width}x{Height}",
                old, anchor, _record.X, _record.Y, x, y, _record.CardWidth, _record.CardHeight);
            _record.X = x;
            _record.Y = y;
            Notify(nameof(X));
            Notify(nameof(Y));
        }
        else
        {
            _log.LogInformation("Anchor changed: {OldAnchor} -> {NewAnchor}, the size of the card is unknown, the position stays", old, anchor);
        }

        Notify(nameof(Anchor));
        _registry.Save();
    }

    /// <summary>How far the new corner is from the old one along one axis, in whole units.</summary>
    private static double Offset(bool toFar, bool fromFar, double size) =>
        toFar == fromFar ? 0 : Math.Round(toFar ? size : -size, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The position from the settings screen: stored and, when the card is shown, sent to it in one move.
    /// Ignored in the move mode, which holds the position until it is confirmed or cancelled.
    /// </summary>
    public void SetPosition(double x, double y)
    {
        if (_moveMode || (_record.X == x && _record.Y == y))
            return;
        _log.LogInformation("Position changed on the settings screen: ({OldX}, {OldY}) -> ({NewX}, {NewY})",
            _record.X, _record.Y, x, y);
        _record.X = x;
        _record.Y = y;
        Notify(nameof(X));
        Notify(nameof(Y));
        _registry.Save();
        if (State == InstanceState.Running)
            SendPosition();
    }

    /// <summary>
    /// The card above all windows instead of under regular ones. A running card leaves the desktop layer meanwhile:
    /// the layer would take a topmost widget for one the desktop had raised and put it back down.
    /// </summary>
    public bool Topmost
    {
        get => _record.Topmost;
        set => SetTopmost(value, "the settings screen");
    }

    private void SetTopmost(bool topmost, string initiator)
    {
        if (_record.Topmost == topmost)
            return;
        _log.LogInformation("Topmost changed by {Initiator}: {OldValue} -> {NewValue}", initiator, _record.Topmost, topmost);
        _record.Topmost = topmost;
        Notify(nameof(Topmost));
        _registry.Save();

        // Pinned: out of the layer first, so its placement does not reach the card. Unpinned: the wrapper hears it
        // first, so the placement that follows the registration is not ignored.
        if (topmost && _hwnd != IntPtr.Zero)
            _desktop.Unregister(_hwnd);
        SendTopmost();
        if (!topmost && _hwnd != IntPtr.Zero)
            _desktop.Register(_hwnd);
    }

    public bool StartWithManager
    {
        get => _record.StartWithManager;
        set
        {
            if (_record.StartWithManager == value)
                return;
            _log.LogInformation("Start with manager changed: {OldValue} -> {NewValue}", _record.StartWithManager, value);
            _record.StartWithManager = value;
            Notify(nameof(StartWithManager));
            _registry.Save();
        }
    }

    public bool RestartOnCrash
    {
        get => _record.RestartOnCrash;
        set
        {
            if (_record.RestartOnCrash == value)
                return;
            _log.LogInformation("Restart on crash changed: {OldValue} -> {NewValue}", _record.RestartOnCrash, value);
            _record.RestartOnCrash = value;
            Notify(nameof(RestartOnCrash));
            _registry.Save();
        }
    }

    public HostThemeInheritance HostThemeDeclared => _package.Descriptor.HostTheme;

    /// <summary>The user may switch inheritance only when the widget does not require the theme.</summary>
    public bool CanChangeInheritHostTheme => HostThemeDeclared != HostThemeInheritance.Required;

    public bool InheritHostTheme
    {
        get => HostThemeDeclared == HostThemeInheritance.Required
            || (_record.InheritHostTheme ?? HostThemeDeclared == HostThemeInheritance.On);
        set
        {
            if (!CanChangeInheritHostTheme || InheritHostTheme == value)
                return;
            _log.LogInformation("Material Design inheritance changed: {OldValue} -> {NewValue}", InheritHostTheme, value);
            _record.InheritHostTheme = value;
            Notify(nameof(InheritHostTheme));
            _registry.Save();
            SendTheme();
        }
    }

    public InstanceTheme Theme
    {
        get => _record.Theme;
        set
        {
            if (_record.Theme == value)
                return;
            _log.LogInformation("Theme changed: {OldTheme} -> {NewTheme}", _record.Theme, value);
            _record.Theme = value;
            Notify(nameof(Theme));
            _registry.Save();
            SendTheme();
        }
    }

    /// <summary>The light or dark theme the instance shows after its own choice is applied.</summary>
    public bool IsDarkTheme => _record.Theme switch
    {
        InstanceTheme.Light => false,
        InstanceTheme.Dark => true,
        _ => _theme.IsDark
    };

    /// <summary>The widget package of the instance: its settings screen is made from it.</summary>
    public WidgetPackage Package => _package;

    /// <summary>The settings file as it is on disk; <c>null</c> when there is no file.</summary>
    public string? SettingsJson
    {
        get
        {
            if (!_settingsJsonRead)
            {
                _settingsJson = ReadSettings();
                _settingsJsonRead = true;
            }

            return _settingsJson;
        }
    }

    /// <summary>Whitespace around the tag is dropped; an empty tag is no tag.</summary>
    public static string? NormalizeTag(string? tag) => string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();

    /// <summary>
    /// Settings from the settings screen: written to the file and, when the card is shown, sent to it.
    /// </summary>
    /// <param name="restart">
    /// The card takes them only when it starts: a running or starting instance is stopped and started again with the new file.
    /// </param>
    public void SaveSettings(string settings, bool restart = false)
    {
        WriteSettings(settings);
        _log.LogInformation("Settings saved from the settings screen ({Length} characters)", settings.Length);
        if (restart && State is InstanceState.Starting or InstanceState.Running)
            _ = RestartAsync();
        else if (State == InstanceState.Running)
            Send(new WidgetCommand(WidgetCommandCodes.ApplySettings, WidgetCommandTarget.Widget, Encoding.UTF8.GetBytes(settings)));
    }

    /// <summary>Starts again only when nothing else happened to the instance while it was stopping.</summary>
    private async Task RestartAsync()
    {
        _log.LogInformation("The saved settings need a restart of the card");
        await StopAsync("restart");
        OnUi(() =>
        {
            if (State == InstanceState.Stopped)
                BeginLaunch(resetCrashes: true);
        });
    }

    /// <summary>The settings file read from disk now; <c>null</c> when there is no file.</summary>
    public string? ReadSettings() => _settings.Read(InstanceId);

    public void Start(string initiator = "the user")
    {
        if (!CanStart)
            return;
        _log.LogInformation("Start requested by {Initiator}", initiator);
        _backoff?.Cancel();
        BeginLaunch(resetCrashes: true);
    }

    public Task Stop()
    {
        if (CanStop)
            _log.LogInformation("Stop requested by the user");
        return StopAsync("user");
    }

    public void EnterMoveMode(string initiator = "the settings screen")
    {
        if (!CanMove)
            return;
        _log.LogInformation("Move mode requested by {Initiator}", initiator);
        Send(new WidgetCommand(WidgetCommandCodes.EnterMoveMode, WidgetCommandTarget.Wrapper));
    }

    public void Place(IntPtr cover)
    {
        if (State != InstanceState.Running || _hwnd == IntPtr.Zero || _record.Topmost)
            return;
        Send(new WidgetCommand(WidgetCommandCodes.Place, WidgetCommandTarget.Wrapper,
            WidgetCommandJson.Serialize(new PlaceBody { Hwnd = cover.ToInt64().ToString() })));
    }

    public void CancelMoveMode()
    {
        if (State != InstanceState.Running || !_moveMode)
            return;
        Send(new WidgetCommand(WidgetCommandCodes.CancelMoveMode, WidgetCommandTarget.Wrapper));
    }

    /// <summary>
    /// The end of the mass move. Sent to every running instance, also to one that has not reported
    /// the mode yet: a wrapper outside the mode ignores it.
    /// </summary>
    public void EndMoveMode(bool save)
    {
        if (State != InstanceState.Running)
            return;
        Send(new WidgetCommand(save ? WidgetCommandCodes.ConfirmMoveMode : WidgetCommandCodes.CancelMoveMode,
            WidgetCommandTarget.Wrapper));
    }

    public Task StopForManagerExit() => StopAsync("manager-exit");

    /// <summary>
    /// Every instance hears about a change of the application theme, even one with its own theme.
    /// </summary>
    public void OnApplicationThemeChanged() => SendTheme();

    private void BeginLaunch(bool resetCrashes)
    {
        if (resetCrashes)
            _crashCount = 0;

        ClosePipe();
        _expectExit = false;
        _welcomed = false;
        _hwnd = IntPtr.Zero;
        MoveMode = false;
        var generation = ++_generation;
        State = InstanceState.Starting;

        var pipeName = PipeNames.ForInstance(Environment.ProcessId, InstanceId);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _io = new CancellationTokenSource();
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 512 * 1024, 512 * 1024);

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = _paths.HostExe,
                WorkingDirectory = _package.Folder,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--run");
            start.ArgumentList.Add("--instance");
            start.ArgumentList.Add(InstanceId.ToString());
            start.ArgumentList.Add("--pipe");
            start.ArgumentList.Add(pipeName);
            start.ArgumentList.Add("--token");
            start.ArgumentList.Add(token);
            // On the command line, not in Welcome: the wrapper applies it before WPF starts, see its App().
            start.ArgumentList.Add("--language");
            start.ArgumentList.Add(_language.Current);

            _log.LogInformation("Launching the wrapper for {Widget} {Version} after {CrashCount} crashes in a row",
                Name, Version, _crashCount);
            _process = Process.Start(start) ?? throw new InvalidOperationException("The wrapper process was not created.");
            _log.LogInformation("Wrapper process {ProcessId} started", _process.Id);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => OnUi(() =>
            {
                if (generation == _generation)
                    OnProcessExited();
            });
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Failed to launch the wrapper");
            ClosePipe();
            HandleCrash();
            return;
        }

        var pipe = _pipe;
        var io = _io.Token;
        _ = Task.Run(() => TalkAsync(pipe, token, generation, io));
    }

    private async Task TalkAsync(NamedPipeServerStream pipe, string token, int generation, CancellationToken cancellationToken)
    {
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(helloTimeout.Token).ConfigureAwait(false);
            if (await CommandFrame.ReadAsync(pipe, helloTimeout.Token).ConfigureAwait(false) is not { } helloFrame)
            {
                OnChannelClosed(generation, null);
                return;
            }

            _channel.LogDebug("Received {Command}", CommandText.Describe(helloFrame));
            var hello = WidgetCommandJson.Deserialize<HelloBody>(helloFrame.Body);
            if (helloFrame.Code != HandshakeCodes.Hello || hello == null || hello.InstanceId != InstanceId
                || hello.ProtocolVersion != 1 || !string.Equals(hello.Token, token, StringComparison.Ordinal))
            {
                OnUi(() => RejectHandshake(generation));
                return;
            }

            var settings = ReadSettings();
            OnUi(() => SendWelcome(generation, settings));

            while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
            {
                if (await CommandFrame.ReadAsync(pipe, cancellationToken).ConfigureAwait(false) is not { } command)
                {
                    OnChannelClosed(generation, null);
                    return;
                }

                if (command.Code == ChannelCodes.Log)
                {
                    WriteWrapperRecord(command.Body);
                    continue;
                }

                _channel.LogDebug("Received {Command}", CommandText.Describe(command));
                var copy = command;
                OnUi(() =>
                {
                    if (generation == _generation)
                        Accept(copy);
                });
            }
        }
        catch (OperationCanceledException)
        {
            OnUi(() =>
            {
                if (generation == _generation && State == InstanceState.Starting && !_expectExit)
                {
                    _log.LogError("Hello did not arrive within 10 seconds");
                    KillProcess();
                }
            });
        }
        catch (Exception exception)
        {
            OnChannelClosed(generation, exception);
        }
    }

    /// <summary>Quiet when the exit was asked for; otherwise the wrapper is gone or broke the channel.</summary>
    private void OnChannelClosed(int generation, Exception? exception)
    {
        OnUi(() =>
        {
            if (generation != _generation || _expectExit || State is not (InstanceState.Starting or InstanceState.Running))
                return;
            _log.LogWarning(exception, "The instance channel was closed");
            KillProcess();
        });
    }

    private void SendWelcome(int generation, string? settings)
    {
        if (generation != _generation)
            return;
        _welcomeSettings = settings;
        _welcomeAnchor = _record.Anchor;
        _welcomed = true;
        Send(new WidgetCommand(HandshakeCodes.Welcome, WidgetCommandTarget.Wrapper, WidgetCommandJson.Serialize(new WelcomeBody
        {
            RuntimeAssembly = _package.RuntimeAssemblyPath,
            RuntimeType = _package.RuntimeTypeName,
            Settings = settings,
            X = _record.X,
            Y = _record.Y,
            Anchor = _record.Anchor,
            Topmost = _record.Topmost,
            ContentRoot = _package.Folder,
            InstanceDataFolder = _paths.InstanceDataFolder(InstanceId),
            WidgetId = WidgetId,
            LogLevel = _managerLog.MinimumLevel,
            Theme = CreateThemeBody()
        })));
    }

    private void RejectHandshake(int generation)
    {
        if (generation != _generation)
            return;
        _log.LogError("Handshake rejected");
        KillProcess();
    }

    private void Accept(WidgetCommand command)
    {
        switch (command.Code)
        {
            case HandshakeCodes.Ready:
                OnReady(command);
                break;
            case HandshakeCodes.Failed:
                var failed = WidgetCommandJson.Deserialize<FailedBody>(command.Body);
                _log.LogError("The wrapper reported a startup failure: {Message}", failed?.Message ?? "no details");
                break;
            case WidgetCommandCodes.PlacementChanged:
                var position = WidgetCommandJson.Deserialize<PositionBody>(command.Body);
                if (position == null)
                    return;
                // From the move mode or, after host-anchor, the same place given for the new corner.
                _log.LogInformation("Position reported by the card: ({OldX}, {OldY}) -> ({NewX}, {NewY}) of the {Anchor} corner",
                    _record.X, _record.Y, position.X, position.Y, _record.Anchor);
                _record.X = position.X;
                _record.Y = position.Y;
                Notify(nameof(X));
                Notify(nameof(Y));
                _registry.Save();
                break;
            case ChannelCodes.CardSize:
                var size = WidgetCommandJson.Deserialize<CardSizeBody>(command.Body);
                if (size == null || (size.Width == _record.CardWidth && size.Height == _record.CardHeight))
                    return;
                _log.LogDebug("Card size {Width}x{Height}", size.Width, size.Height);
                _record.CardWidth = size.Width;
                _record.CardHeight = size.Height;
                _registry.Save();
                break;
            case WidgetCommandCodes.MoveMode:
                var mode = WidgetCommandJson.Deserialize<MoveModeBody>(command.Body);
                if (mode == null)
                    return;
                MoveMode = mode.Active;
                if (!mode.Active)
                    MoveModeEnded?.Invoke(this, mode.PositionSaved);
                break;
            case WidgetCommandCodes.PersistSettings:
                var json = Encoding.UTF8.GetString(command.Body.Span);
                WriteSettings(json);
                _log.LogInformation("Settings saved by the widget ({Length} characters)", json.Length);
                SettingsPersisted?.Invoke(json);
                break;
            case WidgetCommandCodes.ShowNotification:
                var notification = WidgetCommandJson.Deserialize<NotificationBody>(command.Body);
                if (notification != null)
                {
                    _log.LogInformation("Widget notification: {Title}: {Message}", notification.Title, notification.Message);
                    _notifications.Show(notification.Title, notification.Message);
                }
                break;
            case WidgetCommandCodes.SetTopmost:
                var topmost = WidgetCommandJson.Deserialize<TopmostBody>(command.Body);
                if (topmost != null)
                    SetTopmost(topmost.Topmost, "the widget");
                break;
            case WidgetCommandCodes.OpenSettings:
                _log.LogInformation("Settings screen requested by the widget");
                SettingsRequested?.Invoke(this);
                break;
            case WidgetCommandCodes.Stop:
                if (State == InstanceState.Running)
                {
                    _log.LogInformation("Stop requested by the widget");
                    _ = StopAsync("self");
                }
                break;
            default:
                _log.LogInformation("Unknown code \"{Code}\" from the instance ignored", command.Code);
                break;
        }
    }

    private void OnReady(WidgetCommand command)
    {
        if (State != InstanceState.Starting)
            return;

        var ready = WidgetCommandJson.Deserialize<ReadyBody>(command.Body);
        if (ready == null || !long.TryParse(ready.Hwnd, out var hwnd))
        {
            _log.LogError("Ready arrived without a HWND");
            KillProcess();
            return;
        }

        _hwnd = new IntPtr(hwnd);
        State = InstanceState.Running;
        if (!_record.Topmost)
            _desktop.Register(_hwnd);
        var current = ReadSettings();
        if (!string.Equals(current ?? string.Empty, _welcomeSettings ?? string.Empty, StringComparison.Ordinal))
        {
            _log.LogInformation("Settings changed while the instance was starting, applying them");
            Send(new WidgetCommand(WidgetCommandCodes.ApplySettings, WidgetCommandTarget.Widget,
                Encoding.UTF8.GetBytes(current ?? string.Empty)));
        }

        // The card came up with the corner of Welcome, and the converted position describes the same place.
        if (_record.Anchor != _welcomeAnchor)
        {
            _log.LogInformation("Anchor changed while the instance was starting, asking the card for its position");
            SendAnchor();
        }
    }

    private void OnProcessExited()
    {
        var code = 0;
        try
        {
            code = _process?.HasExited == true ? _process.ExitCode : 0;
        }
        catch
        {
            // The process is no longer accessible.
        }

        if (_hwnd != IntPtr.Zero)
        {
            _desktop.Unregister(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        ClosePipe();
        MoveMode = false;

        if (_expectExit || State == InstanceState.Stopping)
        {
            _log.LogInformation("Wrapper process exited with code {ExitCode}", code);
            State = InstanceState.Stopped;
            return;
        }

        if (State is not (InstanceState.Starting or InstanceState.Running))
            return;

        _log.LogError("Wrapper process exited unexpectedly with code {ExitCode}", code);
        HandleCrash();
    }

    private void HandleCrash()
    {
        _crashCount++;
        var willRetry = _record.RestartOnCrash && _crashCount < 3;
        _log.LogError("Crash {CrashCount} of 3, {Outcome}", _crashCount,
            willRetry ? $"restarting in {(_crashCount == 1 ? 1 : 2)} s" : "no more restarts");
        if (_crashCount == 1)
        {
            _notifications.Show(Title, willRetry ? Strings.Crash_WillRestart : Strings.Crash_NoRestart);
        }
        else if (!willRetry)
        {
            _notifications.Show(Title, Strings.Crash_Again);
        }

        if (!willRetry)
        {
            State = InstanceState.Faulted;
            return;
        }

        State = InstanceState.RestartBackoff;
        _backoff?.Cancel();
        _backoff = new CancellationTokenSource();
        var delay = TimeSpan.FromSeconds(_crashCount == 1 ? 1 : 2);
        var token = _backoff.Token;
        _ = RestartAfter(delay, token);
    }

    private async Task RestartAfter(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        OnUi(() =>
        {
            if (State == InstanceState.RestartBackoff)
                BeginLaunch(resetCrashes: false);
        });
    }

    private async Task StopAsync(string reason)
    {
        if (State == InstanceState.RestartBackoff)
        {
            _backoff?.Cancel();
            State = InstanceState.Stopped;
            return;
        }

        if (State is not (InstanceState.Starting or InstanceState.Running))
            return;

        _log.LogInformation("Stopping the instance, reason: {Reason}", reason);
        var generation = _generation;
        var process = _process;
        _expectExit = true;
        if (State == InstanceState.Running)
            Send(new WidgetCommand(WidgetCommandCodes.Stopping, WidgetCommandTarget.Widget, WidgetCommandJson.Serialize(reason)));
        State = InstanceState.Stopping;
        if (_welcomed)
            Send(new WidgetCommand(WidgetCommandCodes.Shutdown, WidgetCommandTarget.Wrapper));
        else
            KillProcess();

        if (process != null)
        {
            var exited = await Task.Run(() => process.HasExited || process.WaitForExit(3000)).ConfigureAwait(false);
            if (!exited)
            {
                _log.LogWarning("The wrapper did not exit within 3 seconds and is being killed");
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process has already exited.
                }
            }
        }

        OnUi(() =>
        {
            if (generation == _generation && State == InstanceState.Stopping)
                State = InstanceState.Stopped;
        });
    }

    private HostThemeBody CreateThemeBody() => new()
    {
        Dark = IsDarkTheme,
        InheritMaterialDesign = InheritHostTheme
    };

    /// <summary>
    /// After Welcome the wrapper keeps the theme itself, so a change during startup is not lost.
    /// </summary>
    private void SendTheme()
    {
        if (!_welcomed || State is not (InstanceState.Starting or InstanceState.Running))
            return;
        Send(new WidgetCommand(ChannelCodes.HostTheme, WidgetCommandTarget.Wrapper, WidgetCommandJson.Serialize(CreateThemeBody())));
    }

    /// <summary>After Welcome the wrapper keeps the value itself, as it does the theme.</summary>
    private void SendTopmost()
    {
        if (!_welcomed || State is not (InstanceState.Starting or InstanceState.Running))
            return;
        Send(new WidgetCommand(ChannelCodes.HostTopmost, WidgetCommandTarget.Wrapper,
            WidgetCommandJson.Serialize(new TopmostBody { Topmost = _record.Topmost })));
    }

    private void SendAnchor() =>
        Send(new WidgetCommand(ChannelCodes.HostAnchor, WidgetCommandTarget.Wrapper,
            WidgetCommandJson.Serialize(new AnchorBody { Anchor = _record.Anchor })));

    private void SendPosition() =>
        Send(new WidgetCommand(WidgetCommandCodes.SetPosition, WidgetCommandTarget.Wrapper,
            WidgetCommandJson.Serialize(new PositionBody { X = _record.X, Y = _record.Y })));

    private void Send(WidgetCommand command)
    {
        var pipe = _pipe;
        if (pipe == null || !pipe.IsConnected)
            return;

        try
        {
            var bytes = CommandFrame.Encode(command);
            _channel.LogDebug("Sent {Command}", CommandText.Describe(command));
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }
        catch (Exception exception)
        {
            _log.LogWarning(exception, "Failed to send \"{Code}\"", command.Code);
        }
    }

    private void KillProcess()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _log.LogWarning("Killing wrapper process {ProcessId}", _process.Id);
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process has already exited.
        }
    }

    private void ClosePipe()
    {
        try
        {
            _io?.Cancel();
        }
        catch
        {
            // The source is already disposed.
        }

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // The pipe is already closed.
        }

        _pipe = null;
    }

    private void WriteSettings(string json)
    {
        _settings.Write(InstanceId, json);
        _settingsJson = json;
        _settingsJsonRead = true;
        Notify(nameof(SettingsJson));
    }


    /// <summary>
    /// A record the wrapper or the widget sent over the channel. Errors are copied to the manager log
    /// so that failures of every instance are visible in one place.
    /// </summary>
    private void WriteWrapperRecord(ReadOnlyMemory<byte> body)
    {
        LogRecord? record;
        try
        {
            record = WidgetCommandJson.Deserialize<LogRecord>(body);
        }
        catch (Exception exception)
        {
            _log.LogWarning(exception, "Malformed log record from the wrapper dropped");
            return;
        }

        if (record == null)
            return;
        _managerLog.WriteInstance(InstanceId, record);
        if (record.Level >= WidgetLogLevel.Error)
            _managerLog.WriteManager(record, InstanceId);
    }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnUi(Action action) => _ui.Invoke(action);
}
