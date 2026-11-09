using System.Windows;

namespace OwlWidgets.Views;

/// <summary>
/// Whether the log colours take the shade of the dark theme. Set once on the root of the log screen or the record
/// dialog and inherited down the tree: every line binds to it on itself instead of looking for an ancestor,
/// which a list of many lines pays for at every row it creates.
/// </summary>
public static class LogTheme
{
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.RegisterAttached(
        "IsDark", typeof(bool), typeof(LogTheme),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsDark(DependencyObject element) => (bool)element.GetValue(IsDarkProperty);

    public static void SetIsDark(DependencyObject element, bool value) => element.SetValue(IsDarkProperty, value);
}
