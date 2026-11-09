using System.Windows.Input;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Views
{
    /// <summary>The view of <see cref="DeleteInstanceViewModel"/>. The focus starts on Cancel.</summary>
    internal partial class DeleteInstanceDialog : System.Windows.Controls.UserControl
    {
        public DeleteInstanceDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => Keyboard.Focus(CancelButton);
        }
    }
}
