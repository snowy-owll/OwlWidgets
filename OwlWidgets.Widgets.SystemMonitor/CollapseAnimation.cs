using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>
/// Turns the whole card into the collapsed one and back, the same in every style. Both cards are laid out all the time
/// and the one not shown is only hidden, so the window keeps the size of the whole card and both heights are known.
/// Collapsing fades the content of the whole card out, then the frame, from now on the collapsed card, shrinks to
/// the corner of the buttons while the values fade in; expanding does the reverse. A click in the middle of
/// an animation turns it back from where it is.
/// </summary>
internal sealed class CollapseAnimation(Border full, Border collapsed)
{
    private static readonly Duration Resize = TimeSpan.FromMilliseconds(280);
    private static readonly Duration Fade = TimeSpan.FromMilliseconds(140);
    private static readonly IEasingFunction Easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

    private bool _collapsed;

    private UIElement FullContent => full.Child;

    private FrameworkElement CollapsedContent => (FrameworkElement)collapsed.Child;

    /// <param name="atTop">The buttons are in a top corner: the collapsed card stands at the top of the window.</param>
    public void Apply(bool isCollapsed, bool atTop, bool animate)
    {
        _collapsed = isCollapsed;
        // While the frame grows, the values stay by the buttons.
        CollapsedContent.VerticalAlignment = atTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        if (!animate)
        {
            Stop();
            full.Visibility = isCollapsed ? Visibility.Hidden : Visibility.Visible;
            collapsed.Visibility = isCollapsed ? Visibility.Visible : Visibility.Hidden;
            return;
        }

        if (isCollapsed)
        {
            if (full.Visibility == Visibility.Visible)
                Animate(FullContent, UIElement.OpacityProperty, null, 0, Fade, TimeSpan.Zero, () =>
                {
                    if (_collapsed)
                        Shrink(fromFull: true);
                });
            else
                Shrink(fromFull: false);
        }
        else if (collapsed.Visibility == Visibility.Visible)
        {
            Grow();
        }
        else
        {
            // Turned back while the content of the whole card fades out.
            Animate(FullContent, UIElement.OpacityProperty, null, 1, Fade, TimeSpan.Zero);
        }
    }

    /// <param name="fromFull">From the whole card at its own height; otherwise from the frame where it is now.</param>
    private void Shrink(bool fromFull)
    {
        var from = fromFull ? full.ActualHeight : collapsed.ActualHeight;
        full.Visibility = Visibility.Hidden;
        collapsed.Visibility = Visibility.Visible;
        Animate(collapsed, FrameworkElement.HeightProperty, from, CollapsedHeight(), Resize, TimeSpan.Zero, () =>
        {
            if (!_collapsed)
                return;
            collapsed.BeginAnimation(FrameworkElement.HeightProperty, null);
            collapsed.Height = double.NaN;
        });
        if (fromFull)
        {
            CollapsedContent.BeginAnimation(UIElement.OpacityProperty, null);
            CollapsedContent.Opacity = 0;
            Animate(CollapsedContent, UIElement.OpacityProperty, null, 1, Fade, Resize.TimeSpan - Fade.TimeSpan);
        }
        else
        {
            Animate(CollapsedContent, UIElement.OpacityProperty, null, 1, Fade, TimeSpan.Zero);
        }
    }

    /// <summary>The frame grows to the whole card with the values fading out; then the whole card takes its place.</summary>
    private void Grow()
    {
        Animate(collapsed, FrameworkElement.HeightProperty, collapsed.ActualHeight, full.ActualHeight, Resize, TimeSpan.Zero, () =>
        {
            if (_collapsed)
                return;
            collapsed.Visibility = Visibility.Hidden;
            collapsed.BeginAnimation(FrameworkElement.HeightProperty, null);
            collapsed.Height = double.NaN;
            CollapsedContent.BeginAnimation(UIElement.OpacityProperty, null);
            CollapsedContent.Opacity = 1;
            full.Visibility = Visibility.Visible;
            Animate(FullContent, UIElement.OpacityProperty, 0, 1, Fade, TimeSpan.Zero);
        });
        Animate(CollapsedContent, UIElement.OpacityProperty, null, 0, Fade, TimeSpan.Zero);
    }

    /// <summary>The height the collapsed card asks for, measured apart from the frame, which may be higher now.</summary>
    private double CollapsedHeight()
    {
        var chrome = new Size(
            collapsed.Padding.Left + collapsed.Padding.Right + collapsed.BorderThickness.Left + collapsed.BorderThickness.Right,
            collapsed.Padding.Top + collapsed.Padding.Bottom + collapsed.BorderThickness.Top + collapsed.BorderThickness.Bottom);
        CollapsedContent.Measure(new Size(Math.Max(0, full.ActualWidth - chrome.Width), double.PositiveInfinity));
        return CollapsedContent.DesiredSize.Height + chrome.Height;
    }

    private void Stop()
    {
        collapsed.BeginAnimation(FrameworkElement.HeightProperty, null);
        collapsed.Height = double.NaN;
        FullContent.BeginAnimation(UIElement.OpacityProperty, null);
        FullContent.Opacity = 1;
        CollapsedContent.BeginAnimation(UIElement.OpacityProperty, null);
        CollapsedContent.Opacity = 1;
    }

    /// <param name="completed">Attached before the start: a started animation is frozen.</param>
    private static void Animate(UIElement element, DependencyProperty property, double? from, double to,
        Duration duration, TimeSpan delay, Action? completed = null)
    {
        var animation = new DoubleAnimation(to, duration) { BeginTime = delay, EasingFunction = Easing };
        if (from != null)
            animation.From = from;
        if (completed != null)
            animation.Completed += (_, _) => completed();
        element.BeginAnimation(property, animation);
    }
}
