using System.Windows;
using OwlWidgets.Hosting;

namespace OwlWidgets.Services;

/// <summary>
/// The manager window for code that does not own it: the tray, a second manager process, a widget.
/// </summary>
internal interface IMainWindowService
{
    void Show();

    /// <summary>Gets the window out of the way, as closing it does.</summary>
    void Hide();

    /// <summary>The window on the widgets screen and this instance, on its own settings page when it has one.</summary>
    void ShowSettings(InstanceSession session);
}

internal sealed class MainWindowService : IMainWindowService
{
    // Lazy: the window and its screens read the catalog, so they are created after it is loaded.
    private readonly Lazy<MainWindow> _window;
    private readonly IAppLifetime _lifetime;

    public MainWindowService(Lazy<MainWindow> window, IAppLifetime lifetime)
    {
        _window = window;
        _lifetime = lifetime;
    }

    public void Show()
    {
        if (_lifetime.IsClosing)
            return;
        var window = _window.Value;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void Hide()
    {
        if (_window.IsValueCreated)
            _window.Value.Hide();
    }

    public void ShowSettings(InstanceSession session)
    {
        if (_lifetime.IsClosing)
            return;
        Show();
        _window.Value.ShowSettings(session);
    }
}
