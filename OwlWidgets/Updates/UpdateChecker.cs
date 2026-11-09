using Microsoft.Extensions.Logging;
using OwlWidgets.Properties;
using OwlWidgets.Services;
using OwlWidgets.Settings;

namespace OwlWidgets.Updates;

internal enum UpdateStatus
{
    NotChecked,
    Checking,
    UpToDate,
    Available,
    Failed
}

/// <summary>
/// Compares the version of the manager with the latest release on GitHub. While the setting is on, checks
/// soon after the start and then once a day; the About screen checks on request whatever the setting.
/// A newer version is told about in the tray once. The state changes on the UI thread.
/// </summary>
internal sealed class UpdateChecker : IDisposable
{
    /// <summary>How often an automatic check is due.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);

    /// <summary>The loop wakes up this often, so that a check missed while Windows slept is not put off a day.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromHours(1);

    private readonly IReleaseSource _source;
    private readonly ManagerSettingsStore _settings;
    private readonly INotificationService _notifications;
    private readonly IShellService _shell;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<UpdateChecker> _log;
    private readonly CancellationTokenSource _stop = new();

    /// <param name="currentVersion">The version of this build; tests pass their own.</param>
    public UpdateChecker(IReleaseSource source, ManagerSettingsStore settings, INotificationService notifications,
        IShellService shell, IUiDispatcher ui, ILogger<UpdateChecker> log, string? currentVersion = null)
    {
        _source = source;
        _settings = settings;
        _notifications = notifications;
        _shell = shell;
        _ui = ui;
        _log = log;
        Current = SemanticVersion.Parse(currentVersion ?? AppInfo.Version);
    }

    /// <summary>Raised on the UI thread when the status, the release or the time of the check changes.</summary>
    public event EventHandler? Changed;

    public SemanticVersion Current { get; }

    public UpdateStatus Status { get; private set; } = UpdateStatus.NotChecked;

    /// <summary>The newer release while <see cref="Status"/> is <see cref="UpdateStatus.Available"/>.</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>When the last check finished, successfully or not.</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>Starts the automatic checks in the background.</summary>
    public void Start() => _ = RunAsync(_stop.Token);

    /// <summary>The release page of the newer version, or the list of releases.</summary>
    public void OpenReleasePage() => _shell.OpenWebPage(Available?.PageUrl ?? AppInfo.ReleasesUrl);

    /// <summary>
    /// Asks for the latest release; does nothing while a check is under way. A check on request does not
    /// notify: its result is on the screen that asked.
    /// </summary>
    public async Task CheckAsync(bool requested)
    {
        if (Status == UpdateStatus.Checking)
            return;
        SetState(UpdateStatus.Checking, Available);

        ReleaseInfo? latest;
        try
        {
            latest = await _source.GetLatestAsync(_stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _log.LogWarning(exception, "Updates could not be checked at {Url}", AppInfo.LatestReleaseApiUrl);
            LastChecked = DateTimeOffset.Now;
            SetState(UpdateStatus.Failed, null);
            return;
        }

        LastChecked = DateTimeOffset.Now;
        if (latest is null)
        {
            _log.LogInformation("Updates checked: no release is published, version {Version} is the latest", Current);
            SetState(UpdateStatus.UpToDate, null);
            return;
        }

        if (!(latest.Version > Current))
        {
            _log.LogInformation("Updates checked: the latest release is {Latest}, version {Version} is up to date",
                latest.Version, Current);
            SetState(UpdateStatus.UpToDate, null);
            return;
        }

        _log.LogInformation("Updates checked: version {Latest} is available, this is {Version}", latest.Version, Current);
        SetState(UpdateStatus.Available, latest);
        var version = latest.Version.ToString();
        if (_settings.Current.NotifiedUpdate == version)
            return;
        _settings.Current.NotifiedUpdate = version;
        _settings.Save();
        if (!requested)
            _notifications.Show(AppInfo.Name, string.Format(Strings.Update_Notification, version), OpenReleasePage);
    }

    /// <summary>An automatic check: only while the setting is on and a day has passed since the last one.</summary>
    public Task CheckIfDueAsync() => IsDue ? CheckAsync(requested: false) : Task.CompletedTask;

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private bool IsDue => _settings.Current.CheckForUpdates
        && Status != UpdateStatus.Checking
        && (LastChecked is not { } last || DateTimeOffset.Now - last >= Interval);

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(FirstDelay, stop).ConfigureAwait(false);
            while (!stop.IsCancellationRequested)
            {
                // The check itself runs on the UI thread: its state is bound to the screen.
                _ui.Post(() =>
                {
                    if (!stop.IsCancellationRequested)
                        _ = CheckIfDueAsync();
                });
                await Task.Delay(Tick, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The manager is stopping.
        }
    }

    private void SetState(UpdateStatus status, ReleaseInfo? available)
    {
        Status = status;
        Available = available;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
