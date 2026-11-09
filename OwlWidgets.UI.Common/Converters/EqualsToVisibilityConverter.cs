using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OwlWidgets.UI.Common.Converters;

/// <summary>
/// Visible when the value, as text, equals the parameter; collapsed otherwise. For enum values in XAML.
/// </summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
