using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OwlWidgets.Services;

namespace OwlWidgets.Logging;

/// <summary>
/// Daily log files under <c>Data/logs/{folder}/</c>: <c>yyyy-MM-dd.log</c>, then <c>yyyy-MM-dd.1.log</c> and so on
/// once a file reaches <see cref="MaxFileBytes"/>. Files older than a month are deleted, and the oldest files go first
/// while all logs together exceed <see cref="MaxTotalBytes"/>.
/// </summary>
internal sealed partial class LogFileStore : IDisposable
{
    public const long MaxFileBytes = 5L * 1024 * 1024;
    public const long MaxTotalBytes = 100L * 1024 * 1024;

    private readonly string _root;
    private readonly object _gate = new();
    private readonly Dictionary<string, OpenFile> _open = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public LogFileStore(AppPaths paths)
    {
        _root = paths.LogsRoot;
    }

    public string Root => _root;

    public void Append(string folder, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_gate)
        {
            if (_disposed)
                return;

            var file = Current(folder, DateOnly.FromDateTime(DateTime.Now), bytes.Length);
            file.Stream.Write(bytes);
            file.Stream.Flush();
        }
    }

    /// <summary>
    /// Applies the age and total size limits. Runs on its own at startup and whenever a new file is opened.
    /// </summary>
    public void Cleanup()
    {
        lock (_gate)
            CleanupCore();
    }

    /// <summary>
    /// Deletes files of the old JSON Lines format that lay directly in <c>Data/logs</c>.
    /// </summary>
    public void RemoveLegacyFiles()
    {
        if (!Directory.Exists(_root))
            return;
        foreach (var path in Directory.GetFiles(_root, "*.jsonl*", SearchOption.TopDirectoryOnly))
            TryDelete(path);
    }

    public void RemoveFolder(string folder)
    {
        lock (_gate)
        {
            if (_open.Remove(folder, out var file))
                file.Stream.Dispose();

            var directory = Path.Combine(_root, folder);
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Log folder {directory} was not deleted: {exception}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var file in _open.Values)
                file.Stream.Dispose();
            _open.Clear();
        }
    }

    private OpenFile Current(string folder, DateOnly today, int incoming)
    {
        if (_open.TryGetValue(folder, out var file))
        {
            if (file.Date == today && (file.Stream.Length == 0 || file.Stream.Length + incoming <= MaxFileBytes))
                return file;

            file.Stream.Dispose();
            _open.Remove(folder);
            return Open(folder, today, file.Date == today ? file.Index + 1 : 0);
        }

        // First write to this folder since startup: continue today's last file while it has room.
        var last = ListFiles(Path.Combine(_root, folder))
            .Where(item => item.Date == today)
            .MaxBy(item => item.Index);
        var index = last == null ? 0 : last.Length + incoming <= MaxFileBytes ? last.Index : last.Index + 1;
        return Open(folder, today, index);
    }

    private OpenFile Open(string folder, DateOnly date, int index)
    {
        var directory = Path.Combine(_root, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName(date, index));
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var file = new OpenFile(path, date, index, stream);
        _open[folder] = file;
        CleanupCore();
        return file;
    }

    private void CleanupCore()
    {
        if (!Directory.Exists(_root))
            return;

        var open = _open.Values.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cutoff = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1);
        var files = new List<LogFile>();
        foreach (var directory in Directory.GetDirectories(_root))
        {
            foreach (var file in ListFiles(directory))
            {
                if (file.Date < cutoff && !open.Contains(file.Path) && TryDelete(file.Path))
                    continue;
                files.Add(file);
            }
        }

        var total = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.Date).ThenBy(file => file.Index))
        {
            if (total <= MaxTotalBytes)
                break;
            if (open.Contains(file.Path))
                continue;
            if (TryDelete(file.Path))
                total -= file.Length;
        }
    }

    private static IEnumerable<LogFile> ListFiles(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var info in new DirectoryInfo(directory).EnumerateFiles("*.log"))
        {
            var match = FileNamePattern().Match(info.Name);
            if (!match.Success
                || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            var index = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            yield return new LogFile(info.FullName, date, index, info.Length);
        }
    }

    private static string FileName(DateOnly date, int index)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return index == 0 ? $"{day}.log" : $"{day}.{index.ToString(CultureInfo.InvariantCulture)}.log";
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Log file {path} was not deleted: {exception.Message}");
            return false;
        }
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})(?:\.(\d+))?\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    private sealed record LogFile(string Path, DateOnly Date, int Index, long Length);

    private sealed record OpenFile(string Path, DateOnly Date, int Index, FileStream Stream);
}
