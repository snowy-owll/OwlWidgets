using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.Weather.Properties;

namespace OwlWidgets.Widgets.Weather;

public sealed class Descriptor : IWidgetDescriptor
{
    public Guid Id { get; } = Guid.Parse("235c158b-6399-4aa1-a8e8-54fd58fa3326");

    public string Name => Strings.Name;

    public string Description => Strings.Description;

    public string Version => "0.11.0";

    public string Author => "Snowy Owl";

    public ImageSource? Icon =>
        new BitmapImage(new Uri("pack://application:,,,/OwlWidgets.Widgets.Weather.Descriptor;component/Assets/Icon.png"));

    public bool AllowMultipleInstances => true;

    public Task<string?> CreateStarterSettingsAsync(IWidgetLogger log, CancellationToken token) =>
        StarterSettings.CreateAsync(log, token);

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance) =>
        new SettingsEditor(settings, instance.InstanceDataFolder);
}
