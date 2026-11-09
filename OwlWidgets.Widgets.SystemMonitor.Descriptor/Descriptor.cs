using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.SystemMonitor.Properties;

namespace OwlWidgets.Widgets.SystemMonitor;

public sealed class Descriptor : IWidgetDescriptor
{
    public Guid Id { get; } = Guid.Parse("b6d15c29-7ac7-4a92-a63a-edd2d95a3c9a");

    public string Name => Strings.Name;

    public string Description => Strings.Description;

    public string Version => "0.10.1";

    public string Author => "Snowy Owl";

    public ImageSource? Icon =>
        new BitmapImage(new Uri("pack://application:,,,/OwlWidgets.Widgets.SystemMonitor.Descriptor;component/Assets/Icon.png"));

    public bool AllowMultipleInstances => false;

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance) =>
        new SettingsEditor(settings);
}
