using System.Windows;
using System.Windows.Controls;

namespace OwlWidgets.Widgets.Weather;

/// <summary>
/// Asks for no width of its own and gives the child whatever width the parent has left.
/// The card is sized to its content, so a long text in here is trimmed instead of widening the card.
/// </summary>
public sealed class WidthlessDecorator : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child == null)
            return default;
        Child.Measure(constraint);
        return new Size(0, Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child == null)
            return arrangeSize;
        // Measured again with the real width, so the child can trim to it.
        Child.Measure(arrangeSize);
        Child.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}
