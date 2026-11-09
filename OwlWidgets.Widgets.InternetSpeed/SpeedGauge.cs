using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OwlWidgets.Widgets.InternetSpeed;

/// <summary>
/// The speedometer of the card: an arc of 270 degrees open at the bottom, with marks spaced evenly though their
/// values are not (0, 5, 10, 50, 100, 250, 500, 750, 1000), as on Speedtest, so that 3 and 300 Mbps both move
/// the needle visibly. Above 1000 the marks are multiplied by 10. The needle follows <see cref="Value"/> smoothly.
/// </summary>
internal sealed class SpeedGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SpeedGauge), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(SpeedGauge),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>false: only the scale, without the needle and the arc of the value.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(SpeedGauge),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(SpeedGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly double[] Marks = [0, 5, 10, 50, 100, 250, 500, 750, 1000];

    private const double StartAngle = 135;
    private const double Sweep = 270;
    private const double Thickness = 10;

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9c, 0xa3, 0xaf)));
    private static readonly Brush HubBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1f, 0x29, 0x37)));
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>1 up to 1000 Mbps, 10 above; kept until the needle is back at zero, so the scale does not jump back and forth.</summary>
    private double _scale = 1;

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private double Shown => (double)GetValue(ShownProperty);

    protected override void OnRender(DrawingContext context)
    {
        var radius = Math.Min(ActualWidth, ActualHeight * 1.2) / 2 - Thickness / 2;
        if (radius <= 20)
            return;
        var center = new Point(ActualWidth / 2, Thickness / 2 + radius);

        var track = new Pen(TrackBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        context.DrawGeometry(null, track, Arc(center, radius, StartAngle, Sweep));

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var i = 0; i < Marks.Length; i++)
        {
            var angle = StartAngle + Sweep * i / (Marks.Length - 1);
            var text = new FormattedText((Marks[i] * _scale).ToString("N0", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, LabelFace, 10, LabelBrush, dpi);
            var at = Polar(center, radius - Thickness - 9, angle);
            context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
        }

        if (!IsActive)
            return;

        var position = Position(Shown / _scale);
        var valueAngle = StartAngle + Sweep * position;
        if (position > 0.002)
        {
            var value = new Pen(Accent, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            context.DrawGeometry(null, value, Arc(center, radius, StartAngle, Sweep * position));
        }

        // A narrow wedge from the hub to the inside of the labels.
        var tip = Polar(center, radius - Thickness - 22, valueAngle);
        var left = Polar(center, 5, valueAngle - 90);
        var right = Polar(center, 5, valueAngle + 90);
        var needle = new StreamGeometry();
        using (var figure = needle.Open())
        {
            figure.BeginFigure(left, true, true);
            figure.LineTo(tip, true, true);
            figure.LineTo(right, true, true);
        }

        needle.Freeze();
        context.DrawGeometry(Brushes.White, null, needle);
        context.DrawEllipse(Brushes.White, null, center, 9, 9);
        context.DrawEllipse(HubBrush, null, center, 4, 4);
    }

    /// <summary>0 to 1 along the arc for a value in Mbps on the base scale; linear between the marks.</summary>
    private static double Position(double value)
    {
        if (value <= 0)
            return 0;
        for (var i = 1; i < Marks.Length; i++)
        {
            if (value <= Marks[i])
                return (i - 1 + (value - Marks[i - 1]) / (Marks[i] - Marks[i - 1])) / (Marks.Length - 1);
        }

        return 1;
    }

    private static void OnValueChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (SpeedGauge)element;
        var value = Math.Max(0, (double)e.NewValue);
        if (value > Marks[^1])
            gauge._scale = 10;
        else if (value == 0)
            gauge._scale = 1;
        // A little longer than the tick of the test: the needle glides from one reading to the next.
        gauge.BeginAnimation(ShownProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(value == 0 ? 150 : 300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static Point Polar(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static StreamGeometry Arc(Point center, double radius, double from, double sweep)
    {
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(Polar(center, radius, from), false, false);
            figure.ArcTo(Polar(center, radius, from + sweep), new Size(radius, radius), 0, sweep > 180,
                SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
