using System.Text.RegularExpressions;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>Shorter names of the devices for the compact styles; the full name stays in the tooltip.</summary>
internal static partial class DeviceNames
{
    /// <summary>
    /// A name without the vendor, the trademarks and a trailing "Graphics" that follows a model:
    /// <c>RTX 4070 Ti SUPER</c>, <c>Ryzen 9 7950X</c>, <c>Radeon 780M</c>, but <c>Radeon Graphics</c>.
    /// </summary>
    public static string Short(string name)
    {
        var text = Spaces().Replace(Trademarks().Replace(Vendors().Replace(name, string.Empty), string.Empty), " ").Trim();
        const string graphics = " Graphics";
        return text.EndsWith(graphics, StringComparison.Ordinal) && text[..^graphics.Length].Contains(' ') ? text[..^graphics.Length] : text;
    }

    /// <summary>Shorter still, for a column of a dozen characters: <c>RTX 4070 Ti S</c>, <c>Radeon iGPU</c>.</summary>
    public static string Tag(string name) => Short(name)
        .Replace("SUPER", "S", StringComparison.Ordinal)
        .Replace("Laptop GPU", "Laptop", StringComparison.Ordinal)
        .Replace("Radeon Graphics", "Radeon iGPU", StringComparison.Ordinal);

    /// <summary><c>C:\</c> as <c>C:</c>.</summary>
    public static string Drive(string name) => name.Length == 3 && name.EndsWith(":\\", StringComparison.Ordinal) ? name[..2] : name;

    [GeneratedRegex(@"\b(NVIDIA|GeForce|AMD|Intel|Corporation)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Vendors();

    [GeneratedRegex(@"\((TM|R)\)", RegexOptions.IgnoreCase)]
    private static partial Regex Trademarks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
