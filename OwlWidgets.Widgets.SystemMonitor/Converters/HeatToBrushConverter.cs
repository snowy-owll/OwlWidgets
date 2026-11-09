using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OwlWidgets.Widgets.SystemMonitor.Converters;

/// <summary>A temperature in °C: below 70 low, below 85 medium, then high, as in the terminal styles; no value is transparent.</summary>
public sealed class HeatToBrushConverter : IValueConverter
{
    public Brush LowBrush { get; set; } = Brushes.Green;

    public Brush MediumBrush { get; set; } = Brushes.Yellow;

    public Brush HighBrush { get; set; } = Brushes.Red;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        double celsius when celsius < 70 => LowBrush,
        double celsius when celsius < 85 => MediumBrush,
        double => HighBrush,
        _ => Brushes.Transparent
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
