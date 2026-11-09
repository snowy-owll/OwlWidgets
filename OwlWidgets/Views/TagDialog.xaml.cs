using System.Windows.Input;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Views
{
    /// <summary>The view of <see cref="TagViewModel"/>. The field opens selected, ready to be typed over.</summary>
    internal partial class TagDialog : System.Windows.Controls.UserControl
    {
        public TagDialog()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                TagBox.SelectAll();
                Keyboard.Focus(TagBox);
            };
        }
    }
}
