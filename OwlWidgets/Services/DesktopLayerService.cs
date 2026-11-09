namespace OwlWidgets.Services;

/// <summary>
/// The z-order of the widget windows: above the desktop icons, below regular windows.
/// </summary>
internal interface IDesktopLayer
{
    /// <summary>The desktop was raised above regular windows: Win+D or "Show desktop".</summary>
    event Action? DesktopRaised;

    /// <summary>HWND the wrappers must keep their windows below.</summary>
    event Action<IntPtr>? WidgetPlacement;

    void Start();

    void Stop();

    void Register(IntPtr hwnd);

    void Unregister(IntPtr hwnd);
}

/// <summary>
/// The layer itself is static: its window procedure and event hook are process-wide callbacks.
/// </summary>
internal sealed class DesktopLayerService : IDesktopLayer
{
    public event Action? DesktopRaised
    {
        add => DesktopLayer.DesktopRaised += value;
        remove => DesktopLayer.DesktopRaised -= value;
    }

    public event Action<IntPtr>? WidgetPlacement
    {
        add => DesktopLayer.WidgetPlacement += value;
        remove => DesktopLayer.WidgetPlacement -= value;
    }

    public void Start() => DesktopLayer.Initialize();

    public void Stop() => DesktopLayer.Dispose();

    public void Register(IntPtr hwnd) => DesktopLayer.Register(hwnd);

    public void Unregister(IntPtr hwnd) => DesktopLayer.Unregister(hwnd);
}
