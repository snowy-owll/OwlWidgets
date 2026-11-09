using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.CurrencyRates.Properties;

namespace OwlWidgets.Widgets.CurrencyRates;

public sealed class Descriptor : IWidgetDescriptor
{
    public Guid Id { get; } = Guid.Parse("79f35e48-e536-4a47-a78e-41330bbfc81e");

    public string Name => Strings.Name;

    public string Description => Strings.Description;

    public string Version => "0.2.0";

    public string Author => "Snowy Owl";

    public ImageSource? Icon =>
        new BitmapImage(new Uri("pack://application:,,,/OwlWidgets.Widgets.CurrencyRates.Descriptor;component/Assets/Icon.png"));

    public bool AllowMultipleInstances => true;

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance) =>
        new SettingsEditor(settings);
}
