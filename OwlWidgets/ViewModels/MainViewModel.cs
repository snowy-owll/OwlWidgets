using OwlWidgets.Common;
using OwlWidgets.Hosting;

namespace OwlWidgets.ViewModels;

/// <summary>
/// The screens of the manager window, in the order of the navigation rail.
/// Public: the rail items take their values by <c>x:Static</c>.
/// </summary>
public enum ManagerScreen
{
    Widgets,
    Settings,
    Log,
    About
}

internal sealed class MainViewModel : ViewModelBase
{
    public MainViewModel(WidgetsViewModel widgets, SettingsViewModel settings, LogViewModel log, AboutViewModel about)
    {
        Widgets = widgets;
        Settings = settings;
        Log = log;
        About = about;
    }

    public WidgetsViewModel Widgets { get; }

    public SettingsViewModel Settings { get; }

    public LogViewModel Log { get; }

    public AboutViewModel About { get; }

    public ManagerScreen SelectedScreen
    {
        get => GetValue(() => SelectedScreen);
        set
        {
            if (value == SelectedScreen)
                return;
            SetValue(() => SelectedScreen, value);
            OnPropertyChanged(nameof(CurrentScreen));
        }
    }

    /// <summary>
    /// The model of the selected screen, its view picked by type; null for the widgets and log screens,
    /// which the window keeps all the time.
    /// </summary>
    public object? CurrentScreen => SelectedScreen switch
    {
        ManagerScreen.Settings => Settings,
        ManagerScreen.About => About,
        _ => null
    };

    /// <summary>The widgets screen on this instance, on its own settings page when it has one.</summary>
    public void ShowInstanceSettings(InstanceSession session)
    {
        SelectedScreen = ManagerScreen.Widgets;
        Widgets.ShowSettings(session);
    }
}
