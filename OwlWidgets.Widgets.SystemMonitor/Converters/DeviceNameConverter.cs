using System.Globalization;
using System.Windows.Data;

namespace OwlWidgets.Widgets.SystemMonitor.Converters;

/// <summary>A device name of the compact styles, see <see cref="DeviceNames"/>; the parameter is <c>short</c>, <c>tag</c> or <c>drive</c>.</summary>
public sealed class DeviceNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name)
            return string.Empty;
        return (parameter as string) switch
        {
            "tag" => DeviceNames.Tag(name),
            "drive" => DeviceNames.Drive(name),
            _ => DeviceNames.Short(name)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
