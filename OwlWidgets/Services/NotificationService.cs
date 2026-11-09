using Microsoft.Extensions.Logging;

namespace OwlWidgets.Services;

/// <summary>
/// System notifications on behalf of the manager.
/// </summary>
internal interface INotificationService
{
    /// <param name="clicked">Runs when the user clicks this notification; a later one replaces it.</param>
    void Show(string title, string message, Action? clicked = null);
}

internal sealed class TrayNotificationService : INotificationService
{
    private readonly NotifyIcon _icon;
    private readonly ILogger<TrayNotificationService> _log;
    private Action? _clicked;

    public TrayNotificationService(NotifyIcon icon, ILogger<TrayNotificationService> log)
    {
        _icon = icon;
        _log = log;
        _icon.BalloonTipClicked += (_, _) =>
        {
            var clicked = _clicked;
            _clicked = null;
            clicked?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _clicked = null;
    }

    public void Show(string title, string message, Action? clicked = null)
    {
        try
        {
            _clicked = clicked;
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(4000);
        }
        catch (Exception exception)
        {
            _clicked = null;
            _log.LogWarning(exception, "Notification was not shown");
        }
    }
}
