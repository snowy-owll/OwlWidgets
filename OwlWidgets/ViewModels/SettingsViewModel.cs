using OwlWidgets.Common;
using OwlWidgets.Properties;
using OwlWidgets.Services;
using OwlWidgets.Settings;
using OwlWidgets.UI.Common;
using OwlWidgets.Updates;

namespace OwlWidgets.ViewModels;

internal sealed record ThemeOption(AppTheme Value, string Title);

internal sealed record LanguageOption(AppLanguage Value, string Title);

internal sealed class SettingsViewModel : ViewModelBase
{
    private readonly ManagerSettingsStore _store;
    private readonly ThemeService _theme;
    private readonly LanguageService _language;
    private readonly IAutostartService _autostart;
    private readonly UpdateChecker _updates;

    public SettingsViewModel(ManagerSettingsStore store, ThemeService theme, LanguageService language, IAppLifetime lifetime,
        IAutostartService autostart, UpdateChecker updates)
    {
        _store = store;
        _updates = updates;
        _theme = theme;
        _language = language;
        _autostart = autostart;
        RestartCommand = new RelayCommand(lifetime.Restart);
        SetValue(() => Theme, store.Current.Theme);
        SetValue(() => Language, store.Current.Language);
        SetValue(() => RestartRequired, language.RequiresRestart(store.Current.Language));
        SetValue(() => StartWithWindows, autostart.IsEnabled);
        SetValue(() => ShowWindowOnStart, store.Current.ShowWindowOnStart);
        SetValue(() => CheckForUpdates, store.Current.CheckForUpdates);
    }

    /// <summary>
    /// Lives in Windows, not in <c>manager.json</c>; a change that failed leaves the switch as it was.
    /// </summary>
    public bool StartWithWindows
    {
        get => GetValue(() => StartWithWindows);
        set
        {
            if (value == StartWithWindows)
                return;
            SetValue(() => StartWithWindows, value);
            if (!_autostart.SetEnabled(value))
                SetValue(() => StartWithWindows, _autostart.IsEnabled);
        }
    }

    public bool ShowWindowOnStart
    {
        get => GetValue(() => ShowWindowOnStart);
        set
        {
            if (value == ShowWindowOnStart)
                return;
            SetValue(() => ShowWindowOnStart, value);
            _store.Current.ShowWindowOnStart = value;
            _store.Save();
        }
    }

    /// <summary>Turning it on checks at once when a check is due.</summary>
    public bool CheckForUpdates
    {
        get => GetValue(() => CheckForUpdates);
        set
        {
            if (value == CheckForUpdates)
                return;
            SetValue(() => CheckForUpdates, value);
            _store.Current.CheckForUpdates = value;
            _store.Save();
            if (value)
                _ = _updates.CheckIfDueAsync();
        }
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(AppTheme.System, Strings.Settings_ThemeSystem),
        new(AppTheme.Light, Strings.Theme_Light),
        new(AppTheme.Dark, Strings.Theme_Dark)
    ];

    /// <summary>Languages are named in themselves, so each one can be found whatever the current language.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(AppLanguage.System, Strings.Settings_LanguageSystem),
        new(AppLanguage.English, "English"),
        new(AppLanguage.Russian, "Русский")
    ];

    public AppTheme Theme
    {
        get => GetValue(() => Theme);
        set
        {
            if (value == Theme)
                return;
            SetValue(() => Theme, value);
            _store.Current.Theme = value;
            _store.Save();
            _theme.Apply(value);
        }
    }

    /// <summary>
    /// Stored at once and applied on the next start of the application.
    /// </summary>
    public AppLanguage Language
    {
        get => GetValue(() => Language);
        set
        {
            if (value == Language)
                return;
            SetValue(() => Language, value);
            _store.Current.Language = value;
            _store.Save();
            RestartRequired = _language.RequiresRestart(value);
        }
    }

    /// <summary>Applies the stored language: stops the instances and starts the manager again.</summary>
    public RelayCommand RestartCommand { get; }

    /// <summary>The stored language differs from the one this process shows.</summary>
    public bool RestartRequired
    {
        get => GetValue(() => RestartRequired);
        private set => SetValue(() => RestartRequired, value);
    }
}
