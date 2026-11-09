using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OwlWidgets.UI.Common;

/// <summary>
/// The content of a <see cref="ScrollViewer"/> with <c>HorizontalScrollBarVisibility="Auto"</c> that scrolls
/// horizontally only when it does not fit. A scroll viewer alone gives its content unlimited width: text stops
/// wrapping and the scroll bar shows up for any long line. The decorator lays the content out at the width of
/// the viewport, as without the scroll bar, and widens it only by what overflows that width (fixed widths,
/// <c>MinWidth</c>, a horizontal row of fields, a line that does not wrap).
/// </summary>
/// <remarks>
/// WPF clips the desired size of an element to the space it is given, so an overflow is not seen in measuring.
/// It is seen after arranging: an element that does not fit gets its full size and is clipped to its slot.
/// The content is arranged once at the viewport width, the widest overflow is added, and that is repeated
/// for overflows nested in each other. Scroll viewers inside the content are not looked into: they scroll themselves.
/// </remarks>
public sealed class ViewportWidthDecorator : Decorator
{
    private const int MaxPasses = 4;
    private const double Tolerance = 0.5;

    private ScrollViewer? _scrollViewer;

    public ViewportWidthDecorator()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var child = Child;
        if (child == null)
            return default;

        // Not inside a horizontally scrolling viewer, or its viewport is not known yet.
        var viewport = _scrollViewer?.ViewportWidth ?? 0;
        if (!double.IsPositiveInfinity(constraint.Width) || viewport <= 0)
            return base.MeasureOverride(constraint);

        var width = viewport;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            child.Measure(new Size(width, constraint.Height));
            child.Arrange(new Rect(0, 0, width, child.DesiredSize.Height));
            var overflow = MaxOverflow(child);
            if (overflow < Tolerance)
                break;
            width += Math.Ceiling(overflow);
        }

        return width > viewport ? new Size(width, child.DesiredSize.Height) : child.DesiredSize;
    }

    private static double MaxOverflow(DependencyObject element)
    {
        var overflow = 0.0;
        if (element is FrameworkElement { IsVisible: true } framework)
        {
            var slot = LayoutInformation.GetLayoutSlot(framework);
            var needed = framework.RenderSize.Width + framework.Margin.Left + framework.Margin.Right;
            overflow = needed - slot.Width;
        }

        if (element is ScrollViewer)
            return overflow;

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
            overflow = Math.Max(overflow, MaxOverflow(VisualTreeHelper.GetChild(element, i)));
        return overflow;
    }

    private void Attach()
    {
        Detach();
        for (var parent = VisualTreeHelper.GetParent(this); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer scrollViewer)
            {
                _scrollViewer = scrollViewer;
                break;
            }
        }

        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
        InvalidateMeasure();
    }

    private void Detach()
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // ScrollChanged bubbles from scroll viewers inside the content too.
        if (e.OriginalSource == _scrollViewer && e.ViewportWidthChange != 0)
            InvalidateMeasure();
    }
}
