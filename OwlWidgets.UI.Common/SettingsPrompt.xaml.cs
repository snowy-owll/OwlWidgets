using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OwlWidgets.UI.Common;

/// <summary>
/// What a card shows while its settings are empty: a hint and a button that opens the settings.
/// The command usually sends <c>open-settings</c> to the manager.
/// </summary>
public partial class SettingsPrompt : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(SettingsPrompt));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SettingsPrompt));

    public SettingsPrompt()
    {
        InitializeComponent();
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
