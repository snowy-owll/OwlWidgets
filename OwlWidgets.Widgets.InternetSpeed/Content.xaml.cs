using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.InternetSpeed.ViewModels;

namespace OwlWidgets.Widgets.InternetSpeed;

/// <summary>
/// Visual only: puts the card, its content, the icon and the buttons in <see cref="ContentViewModel.ButtonCorner"/>
/// and animates hiding and showing. Hiding fades the content out while the card shrinks to the icon and the buttons
/// and fades the icon in; showing does the reverse, then gives the card back its own size, so a change of the content
/// (a jitter line, a longer status) sizes it again.
/// </summary>
public partial class Content : UserControl
{
    private static readonly Duration Resize = TimeSpan.FromMilliseconds(320);
    private static readonly Duration Fade = TimeSpan.FromMilliseconds(160);
    private static readonly IEasingFunction Easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

    /// <summary>Gap between the icon of a hidden card and the buttons.</summary>
    private const double IconGap = 6;

    private ContentViewModel? _viewModel;

    public Content()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = e.NewValue as ContentViewModel;
            if (_viewModel == null)
                return;
            _viewModel.PropertyChanged += OnViewModelChanged;
            ApplyCorner(_viewModel.ButtonCorner);
            ApplyCollapsed(_viewModel.IsCollapsed, animate: false);
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null)
            return;
        if (e.PropertyName == nameof(ContentViewModel.IsCollapsed))
            ApplyCollapsed(_viewModel.IsCollapsed, animate: IsLoaded);
        else if (e.PropertyName == nameof(ContentViewModel.ButtonCorner))
            ApplyCorner(_viewModel.ButtonCorner);
    }

    /// <summary>
    /// The hide button is the outer one, in the corner itself; the icon of a hidden card is inside of both.
    /// The content is aligned to the same corner, so while the card shrinks it is cut on the far sides.
    /// Buttons at the bottom stand over the status line, which then keeps clear of them.
    /// </summary>
    private void ApplyCorner(CardCorner corner)
    {
        var left = !corner.IsRight();
        var horizontal = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        var vertical = corner.IsBottom() ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        foreach (var element in new FrameworkElement[] { Card, Expanded, Icon, Buttons })
        {
            element.HorizontalAlignment = horizontal;
            element.VerticalAlignment = vertical;
        }

        var inner = Buttons.Children.Cast<UIElement>().First(child => child != ToggleButton);
        Buttons.Children.Clear();
        if (left)
        {
            Buttons.Children.Add(ToggleButton);
            Buttons.Children.Add(inner);
        }
        else
        {
            Buttons.Children.Add(inner);
            Buttons.Children.Add(ToggleButton);
        }

        Buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var offset = Buttons.DesiredSize.Width + IconGap;
        Icon.Margin = left ? new Thickness(offset, 0, 0, 0) : new Thickness(0, 0, offset, 0);
        var clear = corner.IsBottom() ? Buttons.DesiredSize.Width : 0;
        StatusLine.Margin = new Thickness(left ? clear : 0, StatusLine.Margin.Top, left ? 0 : clear, 0);
    }

    /// <summary>
    /// The window changes its size at most once per animation, before it starts: the frame takes the size
    /// of the shown card, and the card changes inside it. Only a shown card at rest sizes the frame itself again.
    /// </summary>
    private void ApplyCollapsed(bool collapsed, bool animate)
    {
        var (width, height) = collapsed ? CollapsedSize() : ExpandedSize();
        if (!animate)
        {
            StopAnimations();
            var expanded = ExpandedSize();
            Frame.Width = collapsed ? expanded.Width : double.NaN;
            Frame.Height = collapsed ? expanded.Height : double.NaN;
            Card.Width = collapsed ? width : double.NaN;
            Card.Height = collapsed ? height : double.NaN;
            Expanded.Opacity = collapsed ? 0 : 1;
            Expanded.Visibility = collapsed ? Visibility.Hidden : Visibility.Visible;
            Icon.Opacity = collapsed ? 1 : 0;
            Icon.Visibility = collapsed ? Visibility.Visible : Visibility.Hidden;
            return;
        }

        // From the size on the screen: a click in the middle of an animation turns it back from where it is.
        var fromWidth = Card.ActualWidth;
        var fromHeight = Card.ActualHeight;
        if (collapsed)
        {
            // A shown card at rest: the frame keeps its size. Turned back from showing, the frame has a size already.
            if (double.IsNaN(Frame.Width))
            {
                Frame.Width = fromWidth;
                Frame.Height = fromHeight;
            }

            Animate(Card, WidthProperty, fromWidth, width, Resize, TimeSpan.Zero, () =>
            {
                if (_viewModel?.IsCollapsed == true)
                    Expanded.Visibility = Visibility.Hidden;
            });
            Animate(Card, HeightProperty, fromHeight, height, Resize, TimeSpan.Zero);
            Animate(Expanded, OpacityProperty, null, 0, Fade, TimeSpan.Zero);
            Icon.Visibility = Visibility.Visible;
            Animate(Icon, OpacityProperty, null, 1, Fade, Resize.TimeSpan - Fade.TimeSpan);
        }
        else
        {
            // The content may have changed while hidden (a new result, a longer status): the frame takes its size now.
            Frame.Width = width;
            Frame.Height = height;
            Expanded.Visibility = Visibility.Visible;
            Animate(Card, WidthProperty, fromWidth, width, Resize, TimeSpan.Zero, () =>
            {
                if (_viewModel?.IsCollapsed != false)
                    return;
                Icon.Visibility = Visibility.Hidden;
                // The card and the frame size themselves again; both are of the size of the content already.
                Card.BeginAnimation(WidthProperty, null);
                Card.BeginAnimation(HeightProperty, null);
                Card.Width = double.NaN;
                Card.Height = double.NaN;
                Frame.Width = double.NaN;
                Frame.Height = double.NaN;
            });
            Animate(Card, HeightProperty, fromHeight, height, Resize, TimeSpan.Zero);
            Animate(Icon, OpacityProperty, null, 0, Fade, TimeSpan.Zero);
            Animate(Expanded, OpacityProperty, null, 1, Fade, Resize.TimeSpan - Fade.TimeSpan);
        }
    }

    /// <summary>The icon and the buttons in one row, inside the padding and the border of the card.</summary>
    private (double Width, double Height) CollapsedSize()
    {
        Buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var chrome = Chrome();
        var width = Icon.Width + IconGap + Buttons.DesiredSize.Width + chrome.Width;
        var height = Math.Max(Icon.Height, Buttons.DesiredSize.Height) + chrome.Height;
        return (width, height);
    }

    /// <summary>The size the content asks for, measured apart from the card, which may still be small.</summary>
    private (double Width, double Height) ExpandedSize()
    {
        Expanded.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var chrome = Chrome();
        return (Expanded.DesiredSize.Width + chrome.Width, Expanded.DesiredSize.Height + chrome.Height);
    }

    private Size Chrome() => new(
        Card.Padding.Left + Card.Padding.Right + Card.BorderThickness.Left + Card.BorderThickness.Right,
        Card.Padding.Top + Card.Padding.Bottom + Card.BorderThickness.Top + Card.BorderThickness.Bottom);

    private void StopAnimations()
    {
        Card.BeginAnimation(WidthProperty, null);
        Card.BeginAnimation(HeightProperty, null);
        Expanded.BeginAnimation(OpacityProperty, null);
        Icon.BeginAnimation(OpacityProperty, null);
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
