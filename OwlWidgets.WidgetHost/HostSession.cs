using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;
using OwlWidgets.Protocol;
using OwlWidgets.WidgetHost.Logging;

namespace OwlWidgets.WidgetHost;

internal sealed record HostOptions(Guid InstanceId, string PipeName, string Token, string Language);

internal sealed class HostSession
{
    private readonly HostOptions _options;
    private readonly ChannelLog _channelLog;
    private readonly ILogger<HostSession> _log;
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    private NamedPipeClientStream? _pipe;
    private WidgetLoadContext? _loadContext;
    private IWidgetRuntime? _runtime;
    private HostWindow? _window;
    private HostContext? _hostContext;
    private WelcomeBody? _welcome;
    private HostThemeBody _theme = new();
    private bool _topmost;
    private bool _running;
    private bool _stopping;
    private bool _exiting;
    private int _exitCode;

    public HostSession(HostOptions options, ChannelLog channelLog, ILogger<HostSession> log)
    {
        _options = options;
        _channelLog = channelLog;
        _log = log;
    }

    public static bool TryParse(string[] args, out HostOptions options)
    {
        options = null!;
        string? instance = null;
        string? pipe = null;
        string? token = null;
        var language = UiLanguage.Default;
        var run = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--run":
                    run = true;
                    break;
                case "--instance" when i + 1 < args.Length:
                    instance = args[++i];
                    break;
                case "--pipe" when i + 1 < args.Length:
                    pipe = args[++i];
                    break;
                case "--token" when i + 1 < args.Length:
                    token = args[++i];
                    break;
                case "--language" when i + 1 < args.Length:
                    language = args[++i];
                    break;
            }
        }

        if (!run || !Guid.TryParse(instance, out var instanceId) || string.IsNullOrWhiteSpace(pipe) || string.IsNullOrWhiteSpace(token))
            return false;

        options = new HostOptions(instanceId, pipe, token, language);
        return true;
    }

    public void Start() => _ = Task.Run(RunAsync);

    public void ReportCrash(Exception exception)
    {
        try
        {
            _log.LogCritical(exception, "Unhandled wrapper exception");
        }
        catch
        {
            // The log is unavailable: the process must exit anyway.
        }

        Environment.Exit(1);
    }

    private async Task RunAsync()
    {
        try
        {
            _log.LogInformation("Wrapper started: process {ProcessId}, instance {InstanceId}",
                Environment.ProcessId, _options.InstanceId);
            _pipe = new NamedPipeClientStream(".", _options.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await _pipe.ConnectAsync(10_000, _lifetime.Token).ConfigureAwait(false);
            _pipe.ReadMode = PipeTransmissionMode.Message;

            Send(new WidgetCommand(HandshakeCodes.Hello, WidgetCommandTarget.Manager,
                WidgetCommandJson.Serialize(new HelloBody
                {
                    InstanceId = _options.InstanceId,
                    Token = _options.Token,
                    ProtocolVersion = 1
                })));
            _channelLog.Attach(SendLog);

            if (await CommandFrame.ReadAsync(_pipe, _lifetime.Token).ConfigureAwait(false) is not { } response)
            {
                _log.LogInformation("The manager closed the channel before Welcome, the wrapper is exiting");
                Exit(0);
                return;
            }

            if (response.Code == WidgetCommandCodes.Shutdown)
            {
                _log.LogInformation("Shutdown arrived before Welcome");
                Exit(0);
                return;
            }

            if (response.Code != HandshakeCodes.Welcome)
                throw new InvalidOperationException($"Expected welcome, received \"{response.Code}\".");

            _welcome = WidgetCommandJson.Deserialize<WelcomeBody>(response.Body)
                ?? throw new InvalidOperationException("The welcome body is empty.");
            _channelLog.MinimumLevel = _welcome.LogLevel;
            _theme = _welcome.Theme ?? new HostThemeBody();
            _topmost = _welcome.Topmost;
            WatchManager();
            _ = Task.Run(ReadLoop);
            await Application.Current.Dispatcher.InvokeAsync(LoadRuntime);
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "The wrapper failed to start the widget");
            TrySendFailed(exception.Message);
            Exit(1);
        }
    }

    private void LoadRuntime()
    {
        if (_exiting || _welcome == null)
            return;

        try
        {
            var contentRoot = _welcome.ContentRoot;
            Directory.SetCurrentDirectory(contentRoot);
            NativeSearch.Enable(contentRoot);

            var runtimePath = Path.IsPathRooted(_welcome.RuntimeAssembly)
                ? _welcome.RuntimeAssembly
                : Path.Combine(contentRoot, _welcome.RuntimeAssembly);
            // The language was applied before Run, from the command line: see App().
            _log.LogInformation("Interface language {Language}, formats {Formats}",
                CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentCulture.Name);
            // The theme goes first: a view that inherits it may resolve theme resources while it is being built.
            _window = new HostWindow { Anchor = _welcome.Anchor };
            _window.ApplyTheme(_theme.Dark, _theme.InheritMaterialDesign);
            _log.LogInformation("Theme applied: {Theme}, Material Design inherited: {Inherited}",
                _theme.Dark ? "dark" : "light", _theme.InheritMaterialDesign);

            _log.LogInformation("Loading runtime {RuntimeType} from {Path}", _welcome.RuntimeType, runtimePath);
            _loadContext = new WidgetLoadContext(runtimePath);
            var assembly = _loadContext.LoadFromAssemblyPath(runtimePath);
            var type = assembly.GetType(_welcome.RuntimeType, throwOnError: true)
                ?? throw new InvalidOperationException("Runtime type not found.");
            _runtime = Activator.CreateInstance(type) as IWidgetRuntime
                ?? throw new InvalidOperationException("Runtime type does not implement IWidgetRuntime.");

            _hostContext = new HostContext(this, _welcome, new WidgetLogger(_channelLog, _welcome.RuntimeType));
            _window.MoveModeEntered += OnMoveModeEntered;
            _window.PlacementConfirmed += OnPlacementConfirmed;
            _window.PlacementCancelled += OnPlacementCancelled;

            _runtime.Start(_hostContext);
            DeliverTheme();
            DeliverLanguage();
            DeliverAnchor();
            DeliverTopmost();
            _window.SetView(_runtime.View as FrameworkElement);
            // After the view: for another corner than the top left one the position depends on the size of the card.
            _window.SetAnchorPosition(_welcome.X, _welcome.Y);
            _window.CardSizeSettled += OnCardSizeSettled;
            _window.Show();
            // Once the window has its handle; under regular windows the manager places it after Ready.
            _window.SetTopmost(_topmost);
            _running = true;
            _log.LogInformation("Widget started at ({X}, {Y}) of its {Anchor} corner{Topmost}", _welcome.X, _welcome.Y,
                _welcome.Anchor, _topmost ? ", above all windows" : string.Empty);
            Send(new WidgetCommand(HandshakeCodes.Ready, WidgetCommandTarget.Manager,
                WidgetCommandJson.Serialize(new ReadyBody { Hwnd = _window.Hwnd.ToInt64().ToString() })));
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Failed to load the runtime");
            TrySendFailed(exception.Message);
            Exit(1);
        }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested && _pipe != null)
            {
                if (await CommandFrame.ReadAsync(_pipe, _lifetime.Token).ConfigureAwait(false) is not { } command)
                {
                    OnChannelClosed(null);
                    return;
                }

                Application.Current.Dispatcher.Invoke(() => Dispatch(command, fromWidget: false));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            OnChannelClosed(exception);
        }
    }

    private void OnChannelClosed(Exception? exception)
    {
        if (_exiting)
            return;
        _log.LogInformation(exception, "The manager channel was closed, the wrapper is exiting");
        Application.Current.Dispatcher.Invoke(() => Exit(0));
    }

    private void Dispatch(WidgetCommand command, bool fromWidget)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            _log.LogWarning("Command with an empty code dropped");
            return;
        }

        if (fromWidget)
        {
            _log.LogDebug("Widget sent {Command}", CommandText.Describe(command));
            if (command.Target == WidgetCommandTarget.Widget)
            {
                _log.LogWarning("The widget sent \"{Code}\" to itself", command.Code);
                return;
            }

            if (command.Target == WidgetCommandTarget.Wrapper)
            {
                HandleWrapper(command, fromWidget: true);
                return;
            }

            if (command.Target != WidgetCommandTarget.Manager)
            {
                _log.LogWarning("Unknown target \"{Target}\" from the widget", command.Target);
                return;
            }

            if (_stopping && command.Code is WidgetCommandCodes.Stop or WidgetCommandCodes.EnterMoveMode)
            {
                _log.LogDebug("\"{Code}\" ignored while the widget is stopping", command.Code);
                return;
            }

            // The click on the card made this process the foreground one; without the permission
            // the manager window would only flash on the taskbar.
            if (command.Code == WidgetCommandCodes.OpenSettings)
                AllowSetForegroundWindow(AsfwAny);

            Send(command);
            return;
        }

        if (command.Target == WidgetCommandTarget.Manager)
        {
            _log.LogWarning("The manager sent \"{Code}\" with target manager", command.Code);
            return;
        }

        if (command.Target == WidgetCommandTarget.Widget)
        {
            if (!_running)
            {
                _log.LogWarning("\"{Code}\" for the widget arrived before start and was dropped", command.Code);
                return;
            }

            DeliverToWidget(command);

            if (command.Code == WidgetCommandCodes.Stopping)
                _stopping = true;
            return;
        }

        if (command.Target == WidgetCommandTarget.Wrapper)
        {
            HandleWrapper(command, fromWidget: false);
            return;
        }

        _log.LogWarning("Unknown target \"{Target}\"", command.Target);
    }

    private void HandleWrapper(WidgetCommand command, bool fromWidget)
    {
        switch (command.Code)
        {
            case WidgetCommandCodes.Shutdown:
                _log.LogInformation("Shutdown requested by the manager");
                Exit(0);
                break;
            case WidgetCommandCodes.Place:
                Place(command);
                break;
            case WidgetCommandCodes.SetPosition:
                SetPosition(command);
                break;
            case WidgetCommandCodes.EnterMoveMode:
                if (_stopping || _window == null || !_running)
                    return;
                _window.EnterMoveMode();
                break;
            case WidgetCommandCodes.CancelMoveMode:
                _window?.CancelMoveMode();
                break;
            case WidgetCommandCodes.ConfirmMoveMode when !fromWidget:
                _window?.ConfirmMoveMode();
                break;
            case ChannelCodes.HostTheme when !fromWidget:
                ApplyTheme(command);
                break;
            case ChannelCodes.HostAnchor when !fromWidget:
                ChangeAnchor(command);
                break;
            case ChannelCodes.HostTopmost when !fromWidget:
                ApplyTopmost(command);
                break;
            default:
                _log.LogInformation("Unknown wrapper code \"{Code}\" from the {Sender} ignored",
                    command.Code, fromWidget ? "widget" : "manager");
                break;
        }
    }

    /// <summary>
    /// The widget hears every theme message, even when the value did not change for it.
    /// </summary>
    private void ApplyTheme(WidgetCommand command)
    {
        var body = WidgetCommandJson.Deserialize<HostThemeBody>(command.Body);
        if (body == null)
            return;
        _theme = body;
        if (_window == null)
            return;

        _window.ApplyTheme(body.Dark, body.InheritMaterialDesign);
        _log.LogInformation("Theme applied: {Theme}, Material Design inherited: {Inherited}",
            body.Dark ? "dark" : "light", body.InheritMaterialDesign);
        if (_running)
            DeliverTheme();
    }

    /// <summary>
    /// The card stays where it is; the manager gets the position of the new corner. In the move mode the frame
    /// holds the position until it ends, so the change waits for the next one from the manager.
    /// </summary>
    private void ChangeAnchor(WidgetCommand command)
    {
        var body = WidgetCommandJson.Deserialize<AnchorBody>(command.Body);
        if (body == null || _window == null || !_running || _window.MoveMode)
            return;
        var (x, y) = _window.ChangeAnchor(body.Anchor);
        _log.LogInformation("Anchor changed to the {Anchor} corner at ({X}, {Y})", body.Anchor, x, y);
        Send(new WidgetCommand(WidgetCommandCodes.PlacementChanged, WidgetCommandTarget.Manager,
            WidgetCommandJson.Serialize(new PositionBody { X = x, Y = y })));
        DeliverAnchor();
    }

    /// <summary>Before the start the value waits for the window; the widget hears it after <c>Start</c>.</summary>
    private void ApplyTopmost(WidgetCommand command)
    {
        var body = WidgetCommandJson.Deserialize<TopmostBody>(command.Body);
        if (body == null)
            return;
        _topmost = body.Topmost;
        if (!_running || _window == null)
            return;
        _window.SetTopmost(_topmost);
        _log.LogInformation(_topmost ? "The card is above all windows" : "The card is under regular windows again");
        DeliverTopmost();
    }

    private void DeliverTopmost() =>
        DeliverToWidget(new WidgetCommand(WidgetCommandCodes.Topmost, WidgetCommandTarget.Widget,
            WidgetCommandJson.Serialize(new TopmostBody { Topmost = _topmost })));

    private void DeliverAnchor() =>
        DeliverToWidget(new WidgetCommand(WidgetCommandCodes.Anchor, WidgetCommandTarget.Widget,
            WidgetCommandJson.Serialize(new AnchorBody { Anchor = _window?.Anchor ?? _welcome?.Anchor ?? CardCorner.TopLeft })));

    private void OnCardSizeSettled(double width, double height) =>
        Send(new WidgetCommand(ChannelCodes.CardSize, WidgetCommandTarget.Manager,
            WidgetCommandJson.Serialize(new CardSizeBody { Width = width, Height = height })));

    /// <summary>
    /// The first delivery follows <c>Start</c>, before the window is shown, so the widget never draws in a stale theme.
    /// </summary>
    private void DeliverTheme() =>
        DeliverToWidget(new WidgetCommand(WidgetCommandCodes.Theme, WidgetCommandTarget.Widget,
            WidgetCommandJson.Serialize(new ThemeBody { Dark = _theme.Dark })));

    /// <summary>
    /// Delivered once after <c>Start</c>, next to the first <c>theme</c>: the language does not change while the wrapper runs.
    /// </summary>
    private void DeliverLanguage() =>
        DeliverToWidget(new WidgetCommand(WidgetCommandCodes.Language, WidgetCommandTarget.Widget,
            WidgetCommandJson.Serialize(new LanguageBody { Language = CultureInfo.CurrentUICulture.Name })));

    private void Place(WidgetCommand command)
    {
        var body = WidgetCommandJson.Deserialize<PlaceBody>(command.Body);
        if (body == null || !long.TryParse(body.Hwnd, out var value) || _window == null)
            return;
        _window.PlaceBelow(new IntPtr(value));
    }

    private void SetPosition(WidgetCommand command)
    {
        if (_window == null || _window.MoveMode)
            return;
        var body = WidgetCommandJson.Deserialize<PositionBody>(command.Body);
        if (body == null)
            return;
        _window.SetAnchorPosition(body.X, body.Y);
    }

    private void OnMoveModeEntered() => PublishMoveMode(active: true, positionSaved: false);

    private void OnPlacementConfirmed(double x, double y)
    {
        Send(new WidgetCommand(WidgetCommandCodes.PlacementChanged, WidgetCommandTarget.Manager,
            WidgetCommandJson.Serialize(new PositionBody { X = x, Y = y })));
        PublishMoveMode(active: false, positionSaved: true);
    }

    private void OnPlacementCancelled() => PublishMoveMode(active: false, positionSaved: false);

    private void PublishMoveMode(bool active, bool positionSaved)
    {
        var body = WidgetCommandJson.Serialize(new MoveModeBody { Active = active, PositionSaved = positionSaved });
        Send(new WidgetCommand(WidgetCommandCodes.MoveMode, WidgetCommandTarget.Manager, body));
        if (_running)
            DeliverToWidget(new WidgetCommand(WidgetCommandCodes.MoveMode, WidgetCommandTarget.Widget, body));
    }

    private void DeliverToWidget(WidgetCommand command)
    {
        if (_runtime == null)
            return;

        _log.LogDebug("Delivering {Command}", CommandText.Describe(command));
        try
        {
            _runtime.Receive(command);
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Receive of \"{Code}\" failed", command.Code);
        }
    }

    private void Send(WidgetCommand command)
    {
        var pipe = _pipe;
        if (pipe == null || !pipe.IsConnected)
            return;

        var bytes = CommandFrame.Encode(command);
        _write.Wait();
        try
        {
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }
        finally
        {
            _write.Release();
        }
    }

    private void SendLog(LogRecord record) =>
        Send(new WidgetCommand(ChannelCodes.Log, WidgetCommandTarget.Manager, WidgetCommandJson.Serialize(record)));

    private void TrySendFailed(string message)
    {
        try
        {
            Send(new WidgetCommand(HandshakeCodes.Failed, WidgetCommandTarget.Manager,
                WidgetCommandJson.Serialize(new FailedBody { Message = message })));
        }
        catch
        {
            // The pipe is already closed.
        }
    }

    private void WatchManager()
    {
        var parts = _options.PipeName.Split('-');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var pid))
            return;

        try
        {
            var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                _log.LogInformation("The manager process has already exited, the wrapper is exiting");
                Application.Current.Dispatcher.Invoke(() => Exit(0));
                return;
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _log.LogInformation("The manager process exited, the wrapper is exiting");
                Exit(0);
            });
        }
        catch (Exception exception)
        {
            _log.LogInformation(exception, "The manager process is unavailable, the wrapper is exiting");
            Application.Current.Dispatcher.BeginInvoke(() => Exit(0));
        }
    }

    private void Exit(int code)
    {
        if (_exiting)
            return;
        _exiting = true;
        _exitCode = code;
        Environment.ExitCode = code;
        _log.LogInformation("Wrapper exiting with code {ExitCode}", code);
        _lifetime.Cancel();

        try
        {
            _hostContext?.NotifyStopping();
            _runtime?.Stop();
            _runtime?.Dispose();
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Failed to stop the widget");
        }

        _runtime = null;
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }

        if (code == 0)
            Application.Current.Shutdown(0);
        else
            Environment.Exit(code);
    }

    private sealed class HostContext : IWidgetHostContext
    {
        private readonly HostSession _session;
        private readonly WelcomeBody _welcome;
        private readonly CancellationTokenSource _stopping = new();

        public HostContext(HostSession session, WelcomeBody welcome, IWidgetLogger logger)
        {
            _session = session;
            _welcome = welcome;
            Logger = logger;
        }

        public Guid WidgetId => _welcome.WidgetId;

        public Guid InstanceId => _session._options.InstanceId;

        public string ContentRoot => _welcome.ContentRoot;

        public string InstanceDataFolder => _welcome.InstanceDataFolder;

        public string? Settings => _welcome.Settings;

        public CancellationToken Stopping => _stopping.Token;

        public IWidgetLogger Logger { get; }

        public void Send(WidgetCommand command)
        {
            var dispatcher = Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
                _session.Dispatch(command, fromWidget: true);
            else
                dispatcher.Invoke(() => _session.Dispatch(command, fromWidget: true));
        }

        public void NotifyStopping() => _stopping.Cancel();
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static class NativeSearch
    {
        private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

        public static void Enable(string directory)
        {
            SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs);
            AddDllDirectory(directory);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string newDirectory);
    }
}
