using System.Windows.Controls;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Views;

/// <summary>The view of <see cref="LogViewModel"/>; the window creates it once and only hides it.</summary>
internal partial class LogView : System.Windows.Controls.UserControl
{
    // At first the newest record is in view; the user scrolling up stops the following until they come back down.
    private bool _followEnd = true;

    public LogView() => InitializeComponent();

    /// <summary>New records keep the end of the list in view while it is scrolled to the end.</summary>
    private void Lines_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // The details of a line have a scroll viewer of their own.
        if (e.OriginalSource is not ScrollViewer viewer || viewer.TemplatedParent != Lines)
            return;

        if (e.ExtentHeightChange == 0)
            _followEnd = viewer.VerticalOffset >= viewer.ScrollableHeight - 1;
        else if (_followEnd)
            viewer.ScrollToEnd();
    }
}
