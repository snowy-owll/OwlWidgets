using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.Weather;

/// <summary>
/// The icons of the card by OpenWeatherMap code: those of the icon pack of the settings, the built-in ones
/// for the codes it lacks. The pack is read once, when the settings are applied, and its files are released,
/// so the settings screen may delete a pack the card no longer uses.
/// </summary>
internal sealed class WeatherIcons
{
    private static readonly Dictionary<string, ImageSource> BuiltInIcons = [];
    private static readonly string AssemblyName = typeof(WeatherIcons).Assembly.GetName().Name!;

    private readonly Dictionary<string, ImageSource> _pack = [];

    /// <summary>Only the built-in icons.</summary>
    public static WeatherIcons BuiltIn { get; } = new(null, null);

    /// <param name="folder">Folder of the pack; null: only the built-in icons.</param>
    public WeatherIcons(string? folder, IWidgetLogger? logger)
    {
        if (folder == null)
            return;
        if (!Directory.Exists(folder))
        {
            logger?.Log(WidgetLogLevel.Warning, $"Icon pack {Path.GetFileName(folder)} was not found, the built-in icons are used.");
            return;
        }

        foreach (var code in IconPacks.Codes)
        {
            var file = Path.Combine(folder, IconPacks.IconFileName(code));
            if (!File.Exists(file))
                continue;
            try
            {
                _pack[code] = Load(new Uri(file), onLoad: true);
            }
            catch (Exception exception)
            {
                logger?.Log(WidgetLogLevel.Warning, $"Icon {code} of the pack was not read, the built-in one is used.", exception);
            }
        }
    }

    /// <summary>null for no code or a code the providers do not give.</summary>
    public ImageSource? Get(string? code)
    {
        if (code == null)
            return null;
        if (_pack.TryGetValue(code, out var icon))
            return icon;
        if (!IconPacks.Codes.Contains(code))
            return null;
        if (!BuiltInIcons.TryGetValue(code, out icon))
        {
            icon = Load(new Uri($"pack://application:,,,/{AssemblyName};component/Assets/Images/WeatherIcons/{code}.png"), onLoad: false);
            BuiltInIcons[code] = icon;
        }

        return icon;
    }

    /// <param name="onLoad">Read the file at once and release it; a resource of the assembly needs no such care.</param>
    private static BitmapImage Load(Uri uri, bool onLoad)
    {
        var image = new BitmapImage();
        image.BeginInit();
        if (onLoad)
        {
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        }

        image.UriSource = uri;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
