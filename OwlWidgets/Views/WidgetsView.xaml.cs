using System.Windows.Controls;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Views
{
    /// <summary>The view of <see cref="WidgetsViewModel"/>.</summary>
    internal partial class WidgetsView : System.Windows.Controls.UserControl
    {
        public WidgetsView() => InitializeComponent();

        /// <summary>An instance selected from elsewhere, such as a widget asking for its settings, comes into view.</summary>
        private void InstanceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InstanceList.SelectedItem != null)
                InstanceList.ScrollIntoView(InstanceList.SelectedItem);
        }
    }
}
