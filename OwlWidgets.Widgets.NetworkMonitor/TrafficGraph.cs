using System.Windows;
using System.Windows.Media;

namespace OwlWidgets.Widgets.NetworkMonitor;

/// <summary>
/// The speed of one direction over the last <see cref="Capacity"/> readings: a filled area under a line, the newest value
/// at the right edge, 0 at the bottom and <see cref="Maximum"/> at the top, with a grid line in the middle.
/// A new <see cref="Values"/> array draws it again.
/// </summary>
public sealed class TrafficGraph : FrameworkElement
{
    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen GridPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF))), 1));

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TrafficGraph)d)._fill = null));

    private Brush? _fill;

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        drawing.DrawRoundedRectangle(Background, null, new Rect(0, 0, width, height), 4, 4);
        var middle = Math.Round(height / 2) + 0.5;
        drawing.DrawLine(GridPen, new Point(0, middle), new Point(width, middle));

        var values = Values;
        if (values == null || values.Count < 2 || Capacity < 2)
            return;

        var count = Math.Min(values.Count, Capacity);
        var first = values.Count - count;
        var step = width / (Capacity - 1);
        var left = width - (count - 1) * step;
        var maximum = Maximum > 0 ? Maximum : 1;
        // The line keeps a pixel off the edges, so its stroke is not cut at 0 and at the top.
        Point At(int i) => new(left + i * step, 1 + (height - 2) * (1 - Math.Clamp(values[first + i] / maximum, 0, 1)));

        var area = new StreamGeometry();
        var line = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Point(left, height), true, true);
            for (var i = 0; i < count; i++)
                context.LineTo(At(i), false, false);
            context.LineTo(new Point(width, height), false, false);
        }

        using (var context = line.Open())
        {
            context.BeginFigure(At(0), false, false);
            for (var i = 1; i < count; i++)
                context.LineTo(At(i), true, true);
        }

        area.Freeze();
        line.Freeze();
        _fill ??= FillOf(Stroke);
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), 4, 4));
        drawing.DrawGeometry(_fill, null, area);
        drawing.DrawGeometry(null, new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round }, line);
        drawing.Pop();
    }

    private static Brush FillOf(Brush stroke)
    {
        var fill = stroke.CloneCurrentValue();
        fill.Opacity = 0.25;
        fill.Freeze();
        return fill;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
