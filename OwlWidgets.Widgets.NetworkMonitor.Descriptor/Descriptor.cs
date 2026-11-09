using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.NetworkMonitor.Properties;

namespace OwlWidgets.Widgets.NetworkMonitor;

public sealed class Descriptor : IWidgetDescriptor
{
    public Guid Id { get; } = Guid.Parse("7694266b-8256-40f2-b978-3558058fdbe0");

    public string Name => Strings.Name;

    public string Description => Strings.Description;

    public string Version => "0.1.0";

    public string Author => "Snowy Owl";

    public ImageSource? Icon =>
        new BitmapImage(new Uri("pack://application:,,,/OwlWidgets.Widgets.NetworkMonitor.Descriptor;component/Assets/Icon.png"));

    public bool AllowMultipleInstances => true;

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance) =>
        new SettingsEditor(settings);
}
