using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.BluetoothAudio.Properties;

namespace OwlWidgets.Widgets.BluetoothAudio;

public sealed class Descriptor : IWidgetDescriptor
{
    public Guid Id { get; } = Guid.Parse("eaaf3c7e-96bd-4f17-96ee-48df677295d0");

    public string Name => Strings.Name;

    public string Description => Strings.Description;

    public string Version => "0.1.2";

    public string Author => "Snowy Owl";

    public ImageSource? Icon =>
        new BitmapImage(new Uri("pack://application:,,,/OwlWidgets.Widgets.BluetoothAudio.Descriptor;component/Assets/Icon.png"));

    public bool AllowMultipleInstances => true;

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance) =>
        new SettingsEditor(settings);
}
