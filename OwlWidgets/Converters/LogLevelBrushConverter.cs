using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OwlWidgets.Contracts;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace OwlWidgets.Converters;

/// <summary>
/// The colour of a log level, the usual scale of log viewers rather than theme colours: critical red, error orange,
/// warning yellow, information green, debug blue, trace grey. Each has a darker shade for the light theme and
/// a lighter one for the dark theme so text stays readable on both.
/// Values: the <see cref="WidgetLogLevel"/> and whether the theme is dark. Parameter <c>Tint</c>: a faint background
/// of the same colour for warnings and errors, transparent for the other levels; <c>Fill</c>: that faint background
/// for any level.
/// </summary>
public sealed class LogLevelBrushConverter : IMultiValueConverter
{
    private const byte TintAlpha = 0x26;
    private const byte FillAlpha = 0x33;

    private static readonly Dictionary<(WidgetLogLevel, bool, byte), Brush> Cache = [];

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not WidgetLogLevel level)
            return Binding.DoNothing;
        var dark = values[1] is true;
        byte alpha = (parameter as string) switch
        {
            "Tint" => TintAlpha,
            "Fill" => FillAlpha,
            _ => 0xFF
        };
        if (alpha == TintAlpha && level < WidgetLogLevel.Warning)
            return Brushes.Transparent;

        lock (Cache)
        {
            if (!Cache.TryGetValue((level, dark, alpha), out var brush))
            {
                var color = ColorOf(level, dark);
                color.A = alpha;
                brush = new SolidColorBrush(color);
                brush.Freeze();
                Cache[(level, dark, alpha)] = brush;
            }

            return brush;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>Material palette: 800–900 shades on the light theme, 300–400 on the dark one.</summary>
    private static Color ColorOf(WidgetLogLevel level, bool dark) => level switch
    {
        WidgetLogLevel.Critical => dark ? Rgb(0xFF5252) : Rgb(0xC62828),
        WidgetLogLevel.Error => dark ? Rgb(0xFFA040) : Rgb(0xD84315),
        WidgetLogLevel.Warning => dark ? Rgb(0xFFD740) : Rgb(0x8D6E00),
        WidgetLogLevel.Information => dark ? Rgb(0x66BB6A) : Rgb(0x2E7D32),
        WidgetLogLevel.Debug => dark ? Rgb(0x64B5F6) : Rgb(0x1565C0),
        _ => dark ? Rgb(0x9E9E9E) : Rgb(0x757575)
    };

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
