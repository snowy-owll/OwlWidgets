using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OwlWidgets.Widgets.SystemMonitor.Converters
{
    public class UsageToBrushConverter : DependencyObject, IValueConverter
    {
        // Dependency properties for color brushes
        public static readonly DependencyProperty LowUsageBrushProperty =
            DependencyProperty.Register(nameof(LowUsageBrush), typeof(Brush), typeof(UsageToBrushConverter), new PropertyMetadata(Brushes.Green));

        public static readonly DependencyProperty MediumUsageBrushProperty =
            DependencyProperty.Register(nameof(MediumUsageBrush), typeof(Brush), typeof(UsageToBrushConverter), new PropertyMetadata(Brushes.Yellow));

        public static readonly DependencyProperty HighUsageBrushProperty =
            DependencyProperty.Register(nameof(HighUsageBrush), typeof(Brush), typeof(UsageToBrushConverter), new PropertyMetadata(Brushes.Red));

        public Brush LowUsageBrush
        {
            get => (Brush)GetValue(LowUsageBrushProperty);
            set => SetValue(LowUsageBrushProperty, value);
        }

        public Brush MediumUsageBrush
        {
            get => (Brush)GetValue(MediumUsageBrushProperty);
            set => SetValue(MediumUsageBrushProperty, value);
        }

        public Brush HighUsageBrush
        {
            get => (Brush)GetValue(HighUsageBrushProperty);
            set => SetValue(HighUsageBrushProperty, value);
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double usagePercentage)
            {
                if (usagePercentage < 50) return LowUsageBrush;
                if (usagePercentage < 80) return MediumUsageBrush;
                return HighUsageBrush;
            }

            return Brushes.Gray; // Default fallback
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}