using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace OwlWidgets.Widgets.Weather;

/// <summary>
/// Icon packs of an instance: folders <c>icons-yyyyMMdd-HHmmss</c> in its files, each with <c>{code}.png</c>
/// for the OpenWeatherMap icon codes it has. The settings name one of them; a code the pack lacks is taken
/// from the built-in icons. Every import makes a new folder, so a running card never sees a pack being written
/// and the settings screen can drop an import that was not saved.
/// </summary>
public static partial class IconPacks
{
    public const string FolderPrefix = "icons-";

    /// <summary>The codes the providers give, day and night.</summary>
    public static IReadOnlyList<string> Codes { get; } =
    [
        "01d", "01n", "02d", "02n", "03d", "03n", "04d", "04n", "09d", "09n",
        "10d", "10n", "11d", "11n", "13d", "13n", "50d", "50n"
    ];

    /// <summary>Larger files are not taken: an icon is drawn at 64 pixels at most.</summary>
    internal const int MaxIconBytes = 2 * 1024 * 1024;

    internal const int MaxIconPixels = 1024;

    private const int MaxNoticeBytes = 256 * 1024;

    private const int MaxNotices = 10;

    /// <summary>A folder or an archive with more files is not searched further.</summary>
    private const int MaxFiles = 10_000;

    public static string IconFileName(string code) => code + ".png";

    /// <summary>The folder of the pack named in the settings; null for the built-in icons or a name that is not a pack.</summary>
    public static string? FolderPath(string instanceDataFolder, string? pack) =>
        pack != null && PackName().IsMatch(pack) && !string.IsNullOrEmpty(instanceDataFolder)
            ? Path.Combine(instanceDataFolder, pack)
            : null;

    /// <summary>The codes the pack has a file for; empty when the folder is missing.</summary>
    public static IReadOnlyList<string> CodesIn(string? folder) =>
        folder != null && Directory.Exists(folder)
            ? Codes.Where(code => File.Exists(Path.Combine(folder, IconFileName(code)))).ToList()
            : [];

    /// <summary>
    /// Copies the icons of a zip archive or a folder into a new pack. Only files named <c>{code}.png</c> or
    /// <c>{code}@2x.png</c> are taken, at any depth, the larger variant first; paths inside the source are never used
    /// for writing. Each icon must be an image of at most <see cref="MaxIconBytes"/>. License and notice files go along,
    /// so the credit of the icons stays with them.
    /// </summary>
    /// <exception cref="IconPackException">The source has no usable icon.</exception>
    internal static IconPackImport Import(string source, string instanceDataFolder)
    {
        Directory.CreateDirectory(instanceDataFolder);
        var pack = NewPackName(instanceDataFolder);
        var temp = Path.Combine(instanceDataFolder, pack + ".tmp");
        Directory.CreateDirectory(temp);
        try
        {
            var result = File.Exists(source)
                ? ImportZip(source, temp)
                : ImportFolder(source, temp);
            if (result.Codes.Count == 0)
                throw new IconPackException(result.Rejected);
            Directory.Move(temp, Path.Combine(instanceDataFolder, pack));
            return result with { Pack = pack };
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Deletes the packs of the instance other than <paramref name="keep"/>, and folders of imports that did not finish.
    /// A pack that cannot be deleted now stays until the next time.
    /// </summary>
    internal static void Prune(string instanceDataFolder, params string?[] keep)
    {
        if (string.IsNullOrEmpty(instanceDataFolder) || !Directory.Exists(instanceDataFolder))
            return;
        foreach (var folder in Directory.EnumerateDirectories(instanceDataFolder, FolderPrefix + "*"))
        {
            if (!keep.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase))
                TryDelete(folder);
        }
    }

    private static IconPackImport ImportZip(string path, string target)
    {
        using var archive = ZipFile.OpenRead(path);
        var files = archive.Entries
            .Where(entry => entry.Name.Length > 0)
            .Take(MaxFiles)
            .Select(entry => new SourceFile(entry.Name, entry.Length, entry.Open));
        return Copy(files.ToList(), target);
    }

    private static IconPackImport ImportFolder(string path, string target)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 };
        var files = Directory.EnumerateFiles(path, "*", options)
            .Take(MaxFiles)
            .Select(file => new SourceFile(Path.GetFileName(file), new FileInfo(file).Length, () => File.OpenRead(file)));
        return Copy(files.ToList(), target);
    }

    private static IconPackImport Copy(IReadOnlyList<SourceFile> files, string target)
    {
        var codes = new List<string>();
        var rejected = new List<string>();
        var candidates = files
            .Select(file => (File: file, Match: IconName().Match(file.Name)))
            .Where(candidate => candidate.Match.Success && Codes.Contains(candidate.Match.Groups["code"].Value.ToLowerInvariant()))
            .GroupBy(candidate => candidate.Match.Groups["code"].Value.ToLowerInvariant());
        foreach (var group in candidates)
        {
            // The @2x variant first: it stays sharp on a scaled screen.
            foreach (var candidate in group.OrderByDescending(candidate => candidate.Match.Groups["scale"].Success))
            {
                if (TryCopyIcon(candidate.File, Path.Combine(target, IconFileName(group.Key))))
                {
                    codes.Add(group.Key);
                    break;
                }

                rejected.Add(candidate.File.Name);
            }
        }

        var notices = 0;
        foreach (var file in files.Where(file => NoticeName().IsMatch(file.Name) && file.Length <= MaxNoticeBytes))
        {
            var destination = Path.Combine(target, Path.GetFileName(file.Name));
            if (notices >= MaxNotices || File.Exists(destination))
                continue;
            using (var input = file.Open())
            using (var output = File.Create(destination))
                input.CopyTo(output);
            notices++;
        }

        var ordered = Codes.Where(codes.Contains).ToList();
        return new IconPackImport(string.Empty, ordered, rejected);
    }

    /// <summary>Reads the file within the limit and writes it only when it decodes as an image of a sensible size.</summary>
    private static bool TryCopyIcon(SourceFile file, string destination)
    {
        if (file.Length > MaxIconBytes)
            return false;
        var bytes = new MemoryStream();
        using (var input = file.Open())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (bytes.Length + read > MaxIconBytes)
                    return false;
                bytes.Write(buffer, 0, read);
            }
        }

        try
        {
            bytes.Position = 0;
            var decoder = BitmapDecoder.Create(bytes, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth is <= 0 or > MaxIconPixels || frame.PixelHeight is <= 0 or > MaxIconPixels)
                return false;
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException or ArgumentException
                                              or InvalidOperationException or OverflowException)
        {
            return false;
        }

        File.WriteAllBytes(destination, bytes.ToArray());
        return true;
    }

    private static string NewPackName(string instanceDataFolder)
    {
        var name = FolderPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = name;
        for (var index = 2; Directory.Exists(Path.Combine(instanceDataFolder, candidate))
                            || Directory.Exists(Path.Combine(instanceDataFolder, candidate + ".tmp")); index++)
            candidate = $"{name}-{index}";
        return candidate;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file may be open right now; the folder goes on the next prune.
        }
    }

    [GeneratedRegex(@"^icons-\d{8}-\d{6}(-\d+)?$")]
    private static partial Regex PackName();

    [GeneratedRegex(@"^(?<code>\d\d[dn])(?<scale>@2x)?\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex IconName();

    [GeneratedRegex(@"^(licen[cs]e|notice|readme|copying|copyright|attribution|credits?)([-_.][^/\\]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NoticeName();

    private sealed record SourceFile(string Name, long Length, Func<Stream> Open);
}

/// <param name="Pack">Folder name of the new pack.</param>
/// <param name="Codes">The codes it has, in the order of <see cref="IconPacks.Codes"/>.</param>
/// <param name="Rejected">Files named as icons that are not images or are too large.</param>
internal sealed record IconPackImport(string Pack, IReadOnlyList<string> Codes, IReadOnlyList<string> Rejected);

/// <summary>The source has no file usable as an icon.</summary>
internal sealed class IconPackException(IReadOnlyList<string> rejected) : Exception("The source has no usable weather icon.")
{
    public IReadOnlyList<string> Rejected { get; } = rejected;
}
