using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.DeviceBatteryInfo.Properties;

namespace OwlWidgets.Widgets.DeviceBatteryInfo;

public sealed class Descriptor : IWidgetDescriptor
{
    public Guid Id { get; } = Guid.Parse("85de61f5-a572-41f8-b664-a173bc0b6fb7");

    public string Name => Strings.Name;

    public string Description => Strings.Description;

    public string Version => "0.4.0";

    public string Author => "Snowy Owl";

    public ImageSource? Icon =>
        new BitmapImage(new Uri("pack://application:,,,/OwlWidgets.Widgets.DeviceBatteryInfo.Descriptor;component/Assets/Icon.png"));

    public bool AllowMultipleInstances => false;

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance) =>
        new SettingsEditor(settings, instance.InstanceDataFolder);
}
