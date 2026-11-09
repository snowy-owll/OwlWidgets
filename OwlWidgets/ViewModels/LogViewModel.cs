using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using OwlWidgets.Common;
using OwlWidgets.Contracts;
using OwlWidgets.Hosting;
using OwlWidgets.Logging;
using OwlWidgets.Properties;
using OwlWidgets.Services;
using OwlWidgets.Settings;
using OwlWidgets.UI.Common;

namespace OwlWidgets.ViewModels;

internal enum LogSourceKind
{
    All,
    Manager,
    Instance
}

/// <summary>An item of the log picker: every log, the manager log or the log of one instance.</summary>
internal sealed class LogSourceViewModel : ViewModelBase, IDisposable
{
    private readonly InstanceSession? _session;

    private LogSourceViewModel(LogSourceKind kind, string title, InstanceSession? session)
    {
        Kind = kind;
        Title = title;
        _session = session;
        if (_session != null)
            _session.PropertyChanged += OnSessionPropertyChanged;
    }

    public static LogSourceViewModel All() => new(LogSourceKind.All, Strings.Log_All, null);

    public static LogSourceViewModel Manager() => new(LogSourceKind.Manager, Strings.Log_Manager, null);

    public static LogSourceViewModel Instance(InstanceSession session) => new(LogSourceKind.Instance, session.Title, session);

    public LogSourceKind Kind { get; }

    public Guid? InstanceId => _session?.InstanceId;

    public string Title
    {
        get => GetValue(() => Title) ?? string.Empty;
        private set => SetValue(() => Title, value);
    }

    /// <summary>The records this item shows; in every log at once, the manager copies of instance records are left out.</summary>
    public bool Contains(LogEntry entry) => Kind switch
    {
        LogSourceKind.All => !entry.IsManagerCopy,
        LogSourceKind.Manager => entry.Log == null,
        _ => entry.Log == InstanceId
    };

    public IReadOnlyList<LogEntry> Read(LogFeed feed) => Kind switch
    {
        LogSourceKind.All => feed.All(),
        LogSourceKind.Manager => feed.Manager(),
        _ => feed.Instance(InstanceId!.Value)
    };

    public void Clear(LogFeed feed)
    {
        if (Kind == LogSourceKind.All)
            feed.ClearAll();
        else
            feed.Clear(InstanceId);
    }

    public void Dispose()
    {
        if (_session != null)
            _session.PropertyChanged -= OnSessionPropertyChanged;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstanceSession.Title))
            Title = _session!.Title;
    }
}

/// <summary>A line of the log screen.</summary>
internal sealed class LogEntryViewModel
{
    public LogEntryViewModel(LogEntry entry, string origin)
    {
        Entry = entry;
        Origin = origin;
        var record = entry.Record;
        var local = record.Timestamp.ToLocalTime();
        Time = local.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);
        FullTime = local.ToString("G", CultureInfo.CurrentCulture);
        Source = string.IsNullOrEmpty(record.Category) ? record.Source : $"{record.Source} · {record.Category}";
    }

    public LogEntry Entry { get; }

    public WidgetLogLevel Level => Entry.Record.Level;

    /// <summary>The level as in the log files.</summary>
    public string LevelText => Level switch
    {
        WidgetLogLevel.Trace => "TRC",
        WidgetLogLevel.Debug => "DBG",
        WidgetLogLevel.Information => "INF",
        WidgetLogLevel.Warning => "WRN",
        WidgetLogLevel.Error => "ERR",
        _ => "CRT"
    };

    /// <summary>Time of the day by the Windows regional settings, with milliseconds.</summary>
    public string Time { get; }

    /// <summary>Date and time by the Windows regional settings, for the tooltip.</summary>
    public string FullTime { get; }

    /// <summary>The manager or the instance the record concerns.</summary>
    public string Origin { get; }

    /// <summary>Who wrote the record (manager, wrapper, widget) and the category.</summary>
    public string Source { get; }

    public string Message => Entry.Record.Message;

    public string? Exception => Entry.Record.Exception;

    public bool HasException => !string.IsNullOrEmpty(Exception);

    /// <summary>What Ctrl+C copies from the message column: the message with the exception.</summary>
    public string FullText => HasException ? $"{Message}{Environment.NewLine}{Exception}" : Message;
}

/// <summary>
/// The log screen: the records of this session kept by <see cref="LogFeed"/>, of every log, of the manager
/// or of one instance, filtered by level and text. The full history is in the files.
/// </summary>
internal sealed class LogViewModel : ViewModelBase
{
    private readonly LogFeed _feed;
    private readonly InstanceManager _manager;
    private readonly IUiDispatcher _ui;
    private readonly ConcurrentQueue<LogFeedChange> _pending = new();
    private readonly Dictionary<LogEntry, LogEntryViewModel> _shown = [];
    private int _flushQueued;

    public LogViewModel(LogFeed feed, InstanceManager manager, IUiDispatcher ui, ManagerLog log, IShellService shell,
        ThemeService theme, IDialogService dialogs, IClipboardService clipboard)
    {
        _feed = feed;
        _manager = manager;
        _ui = ui;
        IsDarkTheme = theme.IsDark;
        theme.Changed += (_, _) => IsDarkTheme = theme.IsDark;
        ShowErrors = true;
        ShowWarnings = true;
        ShowInformation = true;
        ShowDebug = true;
        ShowsDebugFilter = log.MinimumLevel <= WidgetLogLevel.Debug;

        Sources.Add(LogSourceViewModel.All());
        Sources.Add(LogSourceViewModel.Manager());
        foreach (var session in _manager.Instances)
            Sources.Add(LogSourceViewModel.Instance(session));
        SelectedSource = Sources[0];

        ClearCommand = new RelayCommand(() => SelectedSource?.Clear(_feed));
        OpenFolderCommand = new RelayCommand(() => shell.OpenFolder(log.Root));
        ShowDetailsCommand = new RelayCommand<LogEntryViewModel>(line =>
            _ = dialogs.ShowAsync(new LogEntryDetailsViewModel(line, IsDarkTheme, dialogs, clipboard)));

        _manager.Instances.CollectionChanged += OnInstancesChanged;
        _feed.Changed += OnFeedChanged;
        Reload();
    }

    public ObservableCollection<LogSourceViewModel> Sources { get; } = [];

    public LogSourceViewModel? SelectedSource
    {
        get => GetValue(() => SelectedSource);
        set
        {
            // The picker clears its selection while the list is rebuilt; the screen keeps showing something.
            if (value == null || value == SelectedSource)
                return;
            SetValue(() => SelectedSource, value);
            Reload();
        }
    }

    public bool ShowErrors
    {
        get => GetValue(() => ShowErrors);
        set => SetFilter(() => ShowErrors, value);
    }

    public bool ShowWarnings
    {
        get => GetValue(() => ShowWarnings);
        set => SetFilter(() => ShowWarnings, value);
    }

    public bool ShowInformation
    {
        get => GetValue(() => ShowInformation);
        set => SetFilter(() => ShowInformation, value);
    }

    public bool ShowDebug
    {
        get => GetValue(() => ShowDebug);
        set => SetFilter(() => ShowDebug, value);
    }

    /// <summary>Debug records are written only by a Debug build; a Release build has nothing to filter.</summary>
    public bool ShowsDebugFilter { get; }

    public string SearchText
    {
        get => GetValue(() => SearchText) ?? string.Empty;
        set => SetFilter(() => SearchText, value ?? string.Empty);
    }

    /// <summary>The lines shown; a reload puts a new list, so the table is rebuilt once and not for every line.</summary>
    public ObservableCollection<LogEntryViewModel> Entries
    {
        get => GetValue(() => Entries) ?? [];
        private set => SetValue(() => Entries, value);
    }

    public bool IsEmpty
    {
        get => GetValue(() => IsEmpty);
        private set => SetValue(() => IsEmpty, value);
    }

    public RelayCommand ClearCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    /// <summary>The line in a dialog where every field can be selected and copied.</summary>
    public RelayCommand<LogEntryViewModel> ShowDetailsCommand { get; }

    /// <summary>Level colours have a shade for each base theme.</summary>
    public bool IsDarkTheme
    {
        get => GetValue(() => IsDarkTheme);
        private set => SetValue(() => IsDarkTheme, value);
    }

    private void SetFilter<T>(System.Linq.Expressions.Expression<Func<T>> property, T value)
    {
        if (EqualityComparer<T>.Default.Equals(GetValue(property), value))
            return;
        SetValue(property, value);
        Reload();
    }

    private bool Matches(LogEntryViewModel line)
    {
        var shown = line.Level switch
        {
            WidgetLogLevel.Trace or WidgetLogLevel.Debug => ShowDebug,
            WidgetLogLevel.Information => ShowInformation,
            WidgetLogLevel.Warning => ShowWarnings,
            _ => ShowErrors
        };
        if (!shown)
            return false;

        var text = SearchText.Trim();
        return text.Length == 0
               || Contains(line.Message, text)
               || Contains(line.Source, text)
               || Contains(line.Origin, text)
               || Contains(line.Exception, text);
    }

    private static bool Contains(string? value, string text) =>
        value != null && value.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    private void Reload()
    {
        // Called from the constructor and setters before everything is set.
        if (SelectedSource is not { } source)
            return;

        _shown.Clear();
        var lines = new ObservableCollection<LogEntryViewModel>();
        foreach (var entry in source.Read(_feed))
            Show(entry, lines);
        Entries = lines;
        IsEmpty = Entries.Count == 0;
    }

    private void Show(LogEntry entry, ObservableCollection<LogEntryViewModel> lines)
    {
        var line = new LogEntryViewModel(entry, OriginOf(entry));
        if (!Matches(line))
            return;
        _shown[entry] = line;
        lines.Add(line);
    }

    private string OriginOf(LogEntry entry)
    {
        if ((entry.Log ?? entry.InstanceId) is not { } instanceId)
            return Strings.Log_Manager;
        var session = _manager.Instances.FirstOrDefault(instance => instance.InstanceId == instanceId);
        return session?.Title ?? instanceId.ToString("N");
    }

    /// <summary>Records come from any thread and often in bursts; the screen takes them in one go on the UI thread.</summary>
    private void OnFeedChanged(LogFeedChange change)
    {
        _pending.Enqueue(change);
        if (Interlocked.Exchange(ref _flushQueued, 1) == 0)
            _ui.Post(Flush);
    }

    private void Flush()
    {
        Volatile.Write(ref _flushQueued, 0);
        var changes = new List<LogFeedChange>();
        while (_pending.TryDequeue(out var change))
            changes.Add(change);

        if (changes.Any(change => change.Added == null))
        {
            Reload();
            return;
        }

        // Writers on different threads may raise their changes out of order. A record already read
        // by a reload while its change was on the way is not shown twice.
        foreach (var change in changes.OrderBy(change => change.Added!.Sequence))
        {
            if (change.Evicted is { } evicted && _shown.Remove(evicted, out var old))
                Entries.Remove(old);
            var added = change.Added!;
            if (SelectedSource!.Contains(added) && !_shown.ContainsKey(added))
                Show(added, Entries);
        }

        IsEmpty = Entries.Count == 0;
    }

    private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var selected = SelectedSource!;
        var kept = Sources.Where(source => source.Kind != LogSourceKind.Instance).ToList();
        var byId = Sources.Where(source => source.Kind == LogSourceKind.Instance)
            .ToDictionary(source => source.InstanceId!.Value);

        var updated = kept.Concat(_manager.Instances.Select(session =>
            byId.Remove(session.InstanceId, out var existing) ? existing : LogSourceViewModel.Instance(session))).ToList();
        foreach (var removed in byId.Values)
            removed.Dispose();

        Sources.Clear();
        foreach (var source in updated)
            Sources.Add(source);

        // The log of a deleted instance is gone; every log is shown instead.
        if (!Sources.Contains(selected))
            SelectedSource = Sources[0];
        else
            OnPropertyChanged(nameof(SelectedSource));
    }
}
