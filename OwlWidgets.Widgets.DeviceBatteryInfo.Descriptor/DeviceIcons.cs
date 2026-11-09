using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Widgets.DeviceBatteryInfo.Properties;

namespace OwlWidgets.Widgets.DeviceBatteryInfo;

/// <summary>
/// Icons of the devices: the built-in ones, named by a key, and the icons the user added, PNG files in the folder
/// <c>icons</c> of the instance files, named <c>user:{file}</c>. Used by the settings screen and by the card.
/// </summary>
public static partial class DeviceIcons
{
    public const string UserPrefix = "user:";

    public const string FolderName = "icons";

    /// <summary>Larger files are not read.</summary>
    internal const int MaxSourceBytes = 10 * 1024 * 1024;

    /// <summary>A larger picture is scaled down when it is added: the card draws it at 40 pixels.</summary>
    internal const int MaxPixels = 256;

    private static readonly string AssemblyName = typeof(DeviceIcons).Assembly.GetName().Name!;

    /// <summary>Keys of the built-in icons, in the order of the list: <c>Assets/DeviceIcons/{key}.png</c>.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } =
    [
        "headphones", "gamepad", "joystick", "mouse", "keyboard", "phone", "watch", "laptop",
        "speaker", "microphone", "studio-microphone", "pen", "hearing-aid", "camera", "radio", "battery"
    ];

    public static string UserIcon(string file) => UserPrefix + file;

    /// <summary>The file of a user icon; null for a built-in icon or a name that is not a file of the folder.</summary>
    public static string? UserFileName(string? icon) =>
        icon != null && icon.StartsWith(UserPrefix, StringComparison.Ordinal) && FileName().IsMatch(icon[UserPrefix.Length..])
            ? icon[UserPrefix.Length..]
            : null;

    public static string? UserFilePath(string instanceDataFolder, string? icon) =>
        UserFileName(icon) is { } file && !string.IsNullOrEmpty(instanceDataFolder)
            ? Path.Combine(instanceDataFolder, FolderName, file)
            : null;

    /// <summary>The name in the list: of the built-in icon in the language of the application, or of the file.</summary>
    public static string DisplayName(string icon) =>
        UserFileName(icon) is { } file
            ? Path.GetFileNameWithoutExtension(file)
            : Strings.ResourceManager.GetString("Icon_" + icon.Replace('-', '_'), Strings.Culture) ?? icon;

    /// <summary>
    /// A user icon is read at once and its file released, so the settings screen may delete it while the card runs.
    /// </summary>
    /// <returns>null for no icon or an unknown key.</returns>
    /// <exception cref="Exception">A user icon that cannot be read.</exception>
    public static ImageSource? Load(string? icon, string instanceDataFolder)
    {
        if (icon == null)
            return null;
        if (UserFilePath(instanceDataFolder, icon) is { } path)
            return Load(new Uri(path), onLoad: true);
        return BuiltIn.Contains(icon)
            ? Load(new Uri($"pack://application:,,,/{AssemblyName};component/Assets/DeviceIcons/{icon}.png"), onLoad: false)
            : null;
    }

    /// <summary>
    /// Copies a picture into the user icons as PNG, scaled down to <see cref="MaxPixels"/>, under a new name made of
    /// its file name. Of an icon file with several pictures the largest is taken.
    /// </summary>
    /// <returns>The file name in the folder.</returns>
    /// <exception cref="DeviceIconException">The file is too large or not an image.</exception>
    internal static string Import(string source, string instanceDataFolder, IEnumerable<string> taken)
    {
        if (new FileInfo(source).Length > MaxSourceBytes)
            throw new DeviceIconException(tooLarge: true);

        BitmapSource picture;
        try
        {
            using var input = File.OpenRead(source);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            picture = decoder.Frames.MaxBy(frame => frame.PixelWidth * frame.PixelHeight)!;
            if (picture.PixelWidth <= 0 || picture.PixelHeight <= 0)
                throw new DeviceIconException(tooLarge: false);
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException or ArgumentException
                                              or InvalidOperationException or OverflowException)
        {
            throw new DeviceIconException(tooLarge: false);
        }

        var scale = Math.Min(1.0, (double)MaxPixels / Math.Max(picture.PixelWidth, picture.PixelHeight));
        if (scale < 1)
            picture = new TransformedBitmap(picture, new ScaleTransform(scale, scale));

        var folder = Path.Combine(instanceDataFolder, FolderName);
        Directory.CreateDirectory(folder);
        var file = NewFileName(folder, Path.GetFileNameWithoutExtension(source), taken);
        var path = Path.Combine(folder, file);
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using var output = File.Create(path);
            encoder.Save(output);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        return file;
    }

    /// <summary>Deletes the files of the user icons other than <paramref name="keep"/>. A file in use stays until the next time.</summary>
    internal static void Prune(string instanceDataFolder, IEnumerable<string> keep)
    {
        if (string.IsNullOrEmpty(instanceDataFolder))
            return;
        var folder = Path.Combine(instanceDataFolder, FolderName);
        if (!Directory.Exists(folder))
            return;
        var kept = keep.ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!kept.Contains(Path.GetFileName(file)))
                    TryDelete(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder cannot be listed now; the files go on the next prune.
        }
    }

    /// <summary>The name of the source without characters that do not suit a file name, made unique in the folder.</summary>
    private static string NewFileName(string folder, string source, IEnumerable<string> taken)
    {
        var stem = new string(source.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '_' : c).ToArray()).Trim(' ', '.');
        if (stem.Length > 60)
            stem = stem[..60];
        if (stem.Length == 0)
            stem = "icon";
        var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = stem + ".png";
        for (var index = 2; names.Contains(candidate) || File.Exists(Path.Combine(folder, candidate)); index++)
            candidate = string.Create(CultureInfo.InvariantCulture, $"{stem}-{index}.png");
        return candidate;
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

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Open right now; it goes on the next prune.
        }
    }

    /// <summary>A plain file name of the folder: no separators, no relative parts.</summary>
    [GeneratedRegex(@"^[^\\/:*?""<>|]+\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex FileName();
}

/// <summary>The file cannot become an icon.</summary>
/// <param name="tooLarge">true: larger than <see cref="DeviceIcons.MaxSourceBytes"/>; false: not an image.</param>
internal sealed class DeviceIconException(bool tooLarge)
    : Exception(tooLarge ? "The file is too large for an icon." : "The file is not an image.")
{
    public bool TooLarge { get; } = tooLarge;
}
