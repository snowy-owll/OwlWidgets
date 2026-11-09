using System.Windows.Input;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Views;

/// <summary>The view of <see cref="LogEntryDetailsViewModel"/>. The focus starts on Close, so Enter and Esc close it.</summary>
internal partial class LogEntryDialog : System.Windows.Controls.UserControl
{
    public LogEntryDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Keyboard.Focus(CloseButton);
    }
}
