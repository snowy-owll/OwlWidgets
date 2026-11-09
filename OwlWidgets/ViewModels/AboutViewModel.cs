using System.Globalization;
using System.Runtime.InteropServices;
using MaterialDesignThemes.Wpf;
using OwlWidgets.Hosting;
using OwlWidgets.Common;
using OwlWidgets.Logging;
using OwlWidgets.Properties;
using OwlWidgets.Services;
using OwlWidgets.UI.Common;
using OwlWidgets.Updates;

namespace OwlWidgets.ViewModels;

internal sealed record AboutWidget(string Name, string Version, string Author);

internal sealed record AboutComponent(string Name, string Version, string License);

internal sealed class AboutViewModel : ViewModelBase
{
    private readonly UpdateChecker _updates;

    public AboutViewModel(IWidgetCatalog catalog, AppPaths paths, ManagerLog log, IShellService shell, UpdateChecker updates)
    {
        _updates = updates;
        CheckUpdatesCommand = new AsyncRelayCommand(() => updates.CheckAsync(requested: true));
        OpenReleaseCommand = new RelayCommand(updates.OpenReleasePage);
        updates.Changed += (_, _) => ReadUpdates();
        ReadUpdates();
        DataFolder = paths.DataRoot;
        LogsFolder = log.Root;
        OpenDataCommand = new RelayCommand(() => shell.OpenFolder(DataFolder));
        OpenLogsCommand = new RelayCommand(() => shell.OpenFolder(LogsFolder));
        OpenRepositoryCommand = new RelayCommand(() => shell.OpenWebPage(AppInfo.RepositoryUrl));
        Widgets = catalog.Packages
            .Select(package => new AboutWidget(package.Descriptor.Name, package.Descriptor.Version, package.Descriptor.Author))
            .OrderBy(widget => widget.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string Name => AppInfo.Name;

    public string Author => AppInfo.Author;

    /// <summary>The repository address without the scheme, as the link text.</summary>
    public string Repository => new Uri(AppInfo.RepositoryUrl).GetComponents(UriComponents.Host | UriComponents.Path, UriFormat.Unescaped);

    public string Version => AppInfo.Version;

    public string Build => AppInfo.BuildConfiguration;

    /// <summary>Short date of the Windows regional settings, whatever the interface language.</summary>
    public string ReleaseDate => AppInfo.ReleaseDate?.ToString("d", CultureInfo.CurrentCulture) ?? "—";

    public string Runtime => RuntimeInformation.FrameworkDescription;

    public string OperatingSystem => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    public string DataFolder { get; }

    public string LogsFolder { get; }

    public IReadOnlyList<AboutWidget> Widgets { get; }

    public bool HasNoWidgets => Widgets.Count == 0;

    public IReadOnlyList<AboutComponent> Components { get; } =
    [
        new("Material Design In XAML Toolkit", AppInfo.ReadVersion(typeof(PaletteHelper).Assembly), "MIT")
    ];

    public RelayCommand OpenDataCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand OpenRepositoryCommand { get; }

    public AsyncRelayCommand CheckUpdatesCommand { get; }

    public RelayCommand OpenReleaseCommand { get; }

    public UpdateStatus UpdateStatus
    {
        get => GetValue(() => UpdateStatus);
        private set => SetValue(() => UpdateStatus, value);
    }

    /// <summary>What the last check found, in a line next to the version.</summary>
    public string UpdateText
    {
        get => GetValue(() => UpdateText) ?? "";
        private set => SetValue(() => UpdateText, value);
    }

    public bool IsUpdateAvailable
    {
        get => GetValue(() => IsUpdateAvailable);
        private set => SetValue(() => IsUpdateAvailable, value);
    }

    /// <summary>When the last check finished, by the Windows regional settings; <c>null</c> before the first one.</summary>
    public string? UpdateCheckedAt
    {
        get => GetValue(() => UpdateCheckedAt);
        private set => SetValue(() => UpdateCheckedAt, value);
    }

    public bool IsUpdateChecked
    {
        get => GetValue(() => IsUpdateChecked);
        private set => SetValue(() => IsUpdateChecked, value);
    }

    private void ReadUpdates()
    {
        IsUpdateChecked = _updates.LastChecked != null;
        UpdateStatus = _updates.Status;
        IsUpdateAvailable = _updates.Status == UpdateStatus.Available;
        UpdateText = _updates.Status switch
        {
            UpdateStatus.Checking => Strings.About_UpdateChecking,
            UpdateStatus.UpToDate => Strings.About_UpdateLatest,
            UpdateStatus.Available => string.Format(Strings.About_UpdateAvailable, _updates.Available?.Version),
            UpdateStatus.Failed => Strings.About_UpdateFailed,
            _ => Strings.About_UpdateNotChecked
        };
        UpdateCheckedAt = _updates.LastChecked is { } checkedAt
            ? string.Format(Strings.About_UpdateCheckedAt, checkedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture))
            : null;
    }
}
