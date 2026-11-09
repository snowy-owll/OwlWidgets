using OwlWidgets.Hosting;
using OwlWidgets.Properties;

namespace OwlWidgets.Services;

/// <summary>
/// The tray icon and its menu. Created after the language is applied: the menu texts are read once.
/// </summary>
internal sealed class TrayIconService
{
    private readonly NotifyIcon _icon;
    private readonly MassMoveCoordinator _massMove;
    private readonly IAppLifetime _lifetime;
    private readonly IMainWindowService _window;
    private readonly AppPaths _paths;

    public TrayIconService(NotifyIcon icon, MassMoveCoordinator massMove, IAppLifetime lifetime, IMainWindowService window,
        AppPaths paths)
    {
        _paths = paths;
        _icon = icon;
        _massMove = massMove;
        _lifetime = lifetime;
        _window = window;
    }

    public void Show()
    {
        _icon.DoubleClick += (_, _) => _window.Show();
        // The tray shows the small icon size: ask for that frame, so the simplified owl is used and not a scaled 32×32 one.
        _icon.Icon = new Icon(_paths.TrayIcon, SystemInformation.SmallIconSize);
        _icon.ContextMenuStrip = CreateMenu();
        _icon.Visible = true;
        _icon.Text = AppInfo.Name;
    }

    public void Hide() => _icon.Visible = false;

    private ContextMenuStrip CreateMenu()
    {
        var contextMenu = new ContextMenuStrip();
        var menuItemOpenMainWindow = new ToolStripMenuItem { Text = Strings.Tray_Open };
        menuItemOpenMainWindow.Click += (_, _) => _window.Show();
        contextMenu.Items.Add(menuItemOpenMainWindow);
        // Mass move: the same item cancels it while it is on.
        var menuItemMoveAll = new ToolStripMenuItem();
        menuItemMoveAll.Click += (_, _) =>
        {
            if (_massMove.Active)
                _massMove.Cancel("the tray menu");
            else
                _massMove.Enter("the tray menu");
        };
        contextMenu.Opening += (_, _) =>
        {
            var active = _massMove.Active;
            menuItemMoveAll.Text = active ? Strings.MoveAll_Cancel : Strings.MoveAll_Start;
            menuItemMoveAll.Enabled = active || _massMove.CanStart;
        };
        contextMenu.Items.Add(menuItemMoveAll);
        var menuItemExit = new ToolStripMenuItem { Text = Strings.Tray_Exit };
        menuItemExit.Click += (_, _) => _lifetime.Close();
        contextMenu.Items.Add(menuItemExit);
        return contextMenu;
    }
}
