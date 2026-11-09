using System.Globalization;
using System.Reflection;

namespace OwlWidgets;

/// <summary>
/// Product facts shown on the About screen and written to the startup log.
/// Version and release date come from the project file.
/// </summary>
internal static class AppInfo
{
    public const string Name = "Owl Widgets";

    public const string Author = "Snowy Owl";

    public const string RepositoryUrl = "https://github.com/snowy-owll/OwlWidgets";

    public const string ReleasesUrl = RepositoryUrl + "/releases";

    /// <summary>The latest release of <see cref="RepositoryUrl"/> in the GitHub REST API.</summary>
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/snowy-owll/OwlWidgets/releases/latest";

    public const string BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Version { get; } = ReadVersion(Assembly);

    public static DateOnly? ReleaseDate { get; } = ReadReleaseDate();

    public static string ReadVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var metadata = informational.IndexOf('+');
            return metadata < 0 ? informational : informational[..metadata];
        }

        return assembly.GetName().Version?.ToString(3) ?? "";
    }

    private static DateOnly? ReadReleaseDate()
    {
        var value = Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ReleaseDate")?.Value;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
