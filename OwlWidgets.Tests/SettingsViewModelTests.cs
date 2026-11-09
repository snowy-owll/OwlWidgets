using Microsoft.Extensions.Logging.Abstractions;
using OwlWidgets.Services;
using OwlWidgets.Settings;
using OwlWidgets.Updates;
using OwlWidgets.ViewModels;
using Xunit;

namespace OwlWidgets.Tests;

/// <summary>The startup switches of the application settings screen.</summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OwlWidgets.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ThemeService _theme = new(NullLogger<ThemeService>.Instance, new FakeUi());
    private readonly FakeAutostart _autostart = new();
    private readonly FakeReleaseSource _releases = new();

    public SettingsViewModelTests() => _paths = new AppPaths(_root);

    [Fact]
    public void The_window_is_hidden_at_start_and_autostart_is_off_by_default()
    {
        var settings = Create();

        Assert.False(settings.ShowWindowOnStart);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void Showing_the_window_at_start_is_stored_at_once()
    {
        var settings = Create();

        settings.ShowWindowOnStart = true;

        var stored = Store();
        stored.Load();
        Assert.True(stored.Current.ShowWindowOnStart);
    }

    [Fact]
    public void Autostart_is_read_from_Windows_and_written_there()
    {
        _autostart.IsEnabled = true;
        var settings = Create();
        Assert.True(settings.StartWithWindows);

        settings.StartWithWindows = false;

        Assert.False(_autostart.IsEnabled);
        Assert.False(File.Exists(_paths.ManagerSettingsPath));
    }

    [Fact]
    public void A_failed_autostart_change_leaves_the_switch_as_it_was()
    {
        _autostart.Fails = true;
        var settings = Create();

        settings.StartWithWindows = true;

        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void Update_checks_are_on_by_default_and_turning_them_on_checks_at_once()
    {
        var settings = Create();
        Assert.True(settings.CheckForUpdates);

        settings.CheckForUpdates = false;
        var stored = Store();
        stored.Load();
        Assert.False(stored.Current.CheckForUpdates);
        Assert.Equal(0, _releases.Requests);

        settings.CheckForUpdates = true;
        Assert.Equal(1, _releases.Requests);
    }

    private ManagerSettingsStore Store() => new(_paths, NullLogger<ManagerSettingsStore>.Instance);

    private SettingsViewModel Create()
    {
        var store = Store();
        store.Load();
        var updates = new UpdateChecker(_releases, store, new FakeNotifications(), new FakeShell(), new FakeUi(),
            NullLogger<UpdateChecker>.Instance, "0.12.1");
        return new SettingsViewModel(store, _theme, new LanguageService(NullLogger<LanguageService>.Instance),
            new FakeLifetime(), _autostart, updates);
    }

    public void Dispose()
    {
        _theme.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
