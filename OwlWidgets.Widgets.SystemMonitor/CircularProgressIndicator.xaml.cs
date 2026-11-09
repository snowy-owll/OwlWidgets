using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OwlWidgets.Widgets.SystemMonitor
{
    /// <summary>
    /// A ring filled clockwise from the top by <see cref="Value"/>, with the value in the middle, and the lowest and
    /// highest values marked by a tick across the ring and a number outside it. The control keeps one size whatever
    /// the marks: the room for the numbers is reserved around the ring, so the card does not move when they do.
    /// </summary>
    public partial class CircularProgressIndicator : UserControl
    {
        private const double RingSize = 70;
        private const double Radius = 31.5;
        private const double Thickness = 7;
        /// <summary>How far a tick goes past the outer edge of the ring.</summary>
        private const double TickOverhang = 4;
        private const double LabelGap = 2;
        private const double LabelFontSize = 11;
        /// <summary>Room for the widest label, "100°C", on the left and the right, and for its height above and below.</summary>
        private const double LabelWidth = 34;
        private const double LabelHeight = 15;

        private static readonly double OuterRadius = Radius + Thickness / 2 + TickOverhang;
        private static readonly double HalfWidth = OuterRadius + LabelGap + LabelWidth;
        private static readonly double HalfHeight = OuterRadius + LabelGap + LabelHeight;

        public CircularProgressIndicator()
        {
            InitializeComponent();

            Grid ring = new()
            {
                Width = RingSize,
                Height = RingSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Background circle
            Ellipse backgroundCircle = new()
            {
                Width = RingSize,
                Height = RingSize,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 255, 255, 255))
            };

            // Arc path for progress
            _arcPath = new Path
            {
                Stroke = Brushes.Blue, // Default, will be updated
                StrokeThickness = Thickness
            };

            // Center circle
            Ellipse centerCircle = new()
            {
                Width = 56,
                Height = 56,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52))
            };

            // Value text
            _valueText = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            ring.Children.Add(backgroundCircle);
            ring.Children.Add(_arcPath);
            ring.Children.Add(centerCircle);
            ring.Children.Add(_valueText);

            _minMark = new Mark();
            _maxMark = new Mark();
            Canvas marks = new() { IsHitTestVisible = false };
            foreach (var mark in new[] { _minMark, _maxMark })
            {
                marks.Children.Add(mark.Tick);
                marks.Children.Add(mark.Label);
            }

            Content = new Grid
            {
                Width = HalfWidth * 2,
                Height = HalfHeight * 2,
                Children = { ring, marks }
            };
            this.Loaded += (s, e) => UpdateRing();
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double?), typeof(CircularProgressIndicator),
                new PropertyMetadata(null, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double?), typeof(CircularProgressIndicator),
                new PropertyMetadata(null, OnValueChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double?), typeof(CircularProgressIndicator),
                new PropertyMetadata(null, OnValueChanged));

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register("MaxValue", typeof(double), typeof(CircularProgressIndicator),
                new PropertyMetadata(100.0, OnValueChanged));

        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register("Color", typeof(Brush), typeof(CircularProgressIndicator),
                new PropertyMetadata(Brushes.Blue, OnValueChanged));

        public static readonly DependencyProperty MinimumColorProperty =
            DependencyProperty.Register("MinimumColor", typeof(Brush), typeof(CircularProgressIndicator),
                new PropertyMetadata(Brushes.Gray, OnValueChanged));

        public static readonly DependencyProperty MaximumColorProperty =
            DependencyProperty.Register("MaximumColor", typeof(Brush), typeof(CircularProgressIndicator),
                new PropertyMetadata(Brushes.Red, OnValueChanged));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(CircularProgressIndicator),
                new PropertyMetadata("", OnValueChanged));

        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register("DecimalPlaces", typeof(int), typeof(CircularProgressIndicator),
                new PropertyMetadata(1, OnValueChanged));

        private readonly Path _arcPath;
        private readonly TextBlock _valueText;
        private readonly Mark _minMark;
        private readonly Mark _maxMark;

        /// <summary>null: no value, a dash, an empty ring and no marks.</summary>
        public double? Value
        {
            get { return (double?)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        /// <summary>The lowest value seen; null: no mark.</summary>
        public double? Minimum
        {
            get { return (double?)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        /// <summary>The highest value seen; null: no mark.</summary>
        public double? Maximum
        {
            get { return (double?)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double MaxValue
        {
            get { return (double)GetValue(MaxValueProperty); }
            set { SetValue(MaxValueProperty, value); }
        }

        public Brush Color
        {
            get { return (Brush)GetValue(ColorProperty); }
            set { SetValue(ColorProperty, value); }
        }

        public Brush MinimumColor
        {
            get { return (Brush)GetValue(MinimumColorProperty); }
            set { SetValue(MinimumColorProperty, value); }
        }

        public Brush MaximumColor
        {
            get { return (Brush)GetValue(MaximumColorProperty); }
            set { SetValue(MaximumColorProperty, value); }
        }

        public string Unit
        {
            get { return (string)GetValue(UnitProperty); }
            set { SetValue(UnitProperty, value); }
        }

        public int DecimalPlaces
        {
            get { return (int)GetValue(DecimalPlacesProperty); }
            set { SetValue(DecimalPlacesProperty, value); }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as CircularProgressIndicator;
            control?.UpdateRing();
        }

        private string Format(double value) =>
            value.ToString("F" + Math.Max(DecimalPlaces, 0), CultureInfo.CurrentCulture) + Unit;

        /// <summary>Clockwise from the top, in radians.</summary>
        private double Angle(double value) =>
            (Math.Clamp(value / MaxValue, 0.0, 1.0) * 360 - 90) * (Math.PI / 180);

        private void UpdateRing()
        {
            if (_arcPath == null || _valueText == null)
                return;

            if (Value is not { } value)
            {
                _valueText.Text = "—";
                _arcPath.Data = null;
                // The lowest and highest seen so far stay, dimmed: a card asleep keeps its marks.
                UpdateMarks(DimOpacity);
                return;
            }

            _valueText.Text = Format(value);
            _arcPath.Stroke = Color;

            double percentage = Math.Clamp(value / MaxValue, 0.0, 1.0);
            Point center = new Point(RingSize / 2, RingSize / 2);
            double startAngle = Angle(0);
            double endAngle = Angle(value);

            Point startPoint = new Point(
                center.X + Radius * Math.Cos(startAngle),
                center.Y + Radius * Math.Sin(startAngle));

            Point endPoint = new Point(
                center.X + Radius * Math.Cos(endAngle),
                center.Y + Radius * Math.Sin(endAngle));

            PathFigure pathFigure = new PathFigure { StartPoint = startPoint };
            if (percentage >= 1)
            {
                // An arc from a point to itself draws nothing: a full ring is two halves.
                Point opposite = new Point(center.X, center.Y + Radius);
                pathFigure.Segments.Add(new ArcSegment(opposite, new Size(Radius, Radius), 0, false, SweepDirection.Clockwise, true));
                pathFigure.Segments.Add(new ArcSegment(startPoint, new Size(Radius, Radius), 0, false, SweepDirection.Clockwise, true));
            }
            else
            {
                pathFigure.Segments.Add(new ArcSegment(endPoint, new Size(Radius, Radius), 0, percentage > 0.5, SweepDirection.Clockwise, true));
            }

            PathGeometry pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(pathFigure);
            _arcPath.Data = pathGeometry;

            UpdateMarks(1);
        }

        /// <summary>Of the marks while there is no value.</summary>
        private const double DimOpacity = 0.4;

        private void UpdateMarks(double opacity)
        {
            _minMark.Tick.Opacity = _minMark.Label.Opacity = opacity;
            _maxMark.Tick.Opacity = _maxMark.Label.Opacity = opacity;
            var min = Minimum is { } low ? Format(low) : null;
            var max = Maximum is { } high ? Format(high) : null;
            // One mark while both read the same, as at the start: two would lie on each other.
            if (min == max)
                min = null;

            _minMark.Show(min, Minimum ?? 0, MinimumColor, this);
            _maxMark.Show(max, Maximum ?? 0, MaximumColor, this);
            if (min == null || max == null)
                return;

            // Close values put the numbers on each other: they are moved apart along the ring, the ticks stay.
            double minAngle = _minMark.Angle;
            double maxAngle = _maxMark.Angle;
            const double step = 3 * Math.PI / 180;
            for (var i = 0; i < 40 && _minMark.Bounds.IntersectsWith(_maxMark.Bounds); i++)
            {
                minAngle -= step;
                maxAngle += step;
                _minMark.PlaceLabel(minAngle);
                _maxMark.PlaceLabel(maxAngle);
            }
        }

        /// <summary>A tick across the ring at a value and its number outside the ring.</summary>
        private sealed class Mark
        {
            public Line Tick { get; } = new() { StrokeThickness = 2, Visibility = Visibility.Collapsed };

            public TextBlock Label { get; } = new()
            {
                FontSize = LabelFontSize,
                FontWeight = FontWeights.SemiBold,
                Visibility = Visibility.Collapsed
            };

            public double Angle { get; private set; }

            public Rect Bounds { get; private set; }

            public void Hide()
            {
                Tick.Visibility = Visibility.Collapsed;
                Label.Visibility = Visibility.Collapsed;
            }

            /// <param name="text">Null: no mark.</param>
            public void Show(string? text, double value, Brush brush, CircularProgressIndicator owner)
            {
                if (text == null)
                {
                    Hide();
                    return;
                }

                Angle = owner.Angle(value);
                // From the inner edge of the ring, not into the middle with the value.
                var innerRadius = Radius - Thickness / 2;
                Tick.X1 = HalfWidth + innerRadius * Math.Cos(Angle);
                Tick.Y1 = HalfHeight + innerRadius * Math.Sin(Angle);
                Tick.X2 = HalfWidth + OuterRadius * Math.Cos(Angle);
                Tick.Y2 = HalfHeight + OuterRadius * Math.Sin(Angle);
                Tick.Stroke = brush;
                Tick.Visibility = Visibility.Visible;

                Label.Text = text;
                Label.Foreground = brush;
                Label.Visibility = Visibility.Visible;
                PlaceLabel(Angle);
            }

            /// <summary>
            /// The label touches the circle just past the tick with its side or corner that faces the centre:
            /// its centre is moved out from that point by half its size along the direction of the angle.
            /// </summary>
            public void PlaceLabel(double angle)
            {
                Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = Label.DesiredSize;
                var distance = OuterRadius + LabelGap;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                var x = HalfWidth + distance * cos + size.Width / 2 * cos - size.Width / 2;
                var y = HalfHeight + distance * sin + size.Height / 2 * sin - size.Height / 2;
                x = Math.Clamp(x, 0, HalfWidth * 2 - size.Width);
                y = Math.Clamp(y, 0, HalfHeight * 2 - size.Height);
                Canvas.SetLeft(Label, x);
                Canvas.SetTop(Label, y);
                Bounds = new Rect(x, y, size.Width, size.Height);
            }
        }
    }
}
