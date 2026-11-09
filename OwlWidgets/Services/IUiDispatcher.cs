using System.Windows.Threading;

namespace OwlWidgets.Services;

/// <summary>
/// The UI thread of the manager, for code that hears about its events on other threads.
/// </summary>
internal interface IUiDispatcher
{
    /// <summary>Runs the action on the UI thread and waits for it; at once when already there.</summary>
    void Invoke(Action action);

    /// <summary>Queues the action to the UI thread and returns.</summary>
    void Post(Action action);
}

internal sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Invoke(action);
    }

    public void Post(Action action) => _dispatcher.BeginInvoke(action);
}
