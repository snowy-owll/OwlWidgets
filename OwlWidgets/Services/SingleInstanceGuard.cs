using System.Runtime.InteropServices;

namespace OwlWidgets.Services;

/// <summary>
/// One manager per session: the first process holds the mutex, a second one asks it to open its window.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\OwlWidgets.Manager";
    private const string ShowEventName = @"Local\OwlWidgets.Manager.Show";
    private const int AsfwAny = -1;

    private readonly EventWaitHandle _showEvent = new(false, EventResetMode.AutoReset, ShowEventName);
    private readonly Mutex _mutex = new(false, MutexName);
    private RegisteredWaitHandle? _showWait;
    private bool _owned;
    private bool _disposed;

    /// <summary>
    /// Another manager process asked this one to open its window. Raised on a thread pool thread.
    /// </summary>
    public event Action? ShowRequested;

    /// <summary>
    /// An abandoned mutex means the previous manager died without releasing it; it is ours now.
    /// </summary>
    public bool TryAcquire(TimeSpan timeout)
    {
        try
        {
            _owned = _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            _owned = true;
        }

        return _owned;
    }

    /// <summary>Asks the manager that is already running to open its window.</summary>
    public void SignalRunning()
    {
        AllowSetForegroundWindow(AsfwAny);
        _showEvent.Set();
    }

    public void Listen() =>
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => ShowRequested?.Invoke(), null,
            Timeout.Infinite, false);

    /// <summary>
    /// Releases the mutex, so it must run on the thread that acquired it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _showWait?.Unregister(null);
        _showEvent.Dispose();
        if (_owned)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
