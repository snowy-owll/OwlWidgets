using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.InternetSpeed.Properties;

namespace OwlWidgets.Widgets.InternetSpeed;

public sealed class Descriptor : IWidgetDescriptor
{
    public Guid Id { get; } = Guid.Parse("84b7c587-4ef4-4eae-a111-ad82d5bc871e");

    public string Name => Strings.Name;

    public string Description => Strings.Description;

    public string Version => "0.2.1";

    public string Author => "Snowy Owl";

    public ImageSource? Icon =>
        new BitmapImage(new Uri("pack://application:,,,/OwlWidgets.Widgets.InternetSpeed.Descriptor;component/Assets/Icon.png"));

    public bool AllowMultipleInstances => true;

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance) =>
        new SettingsEditor(settings);
}
