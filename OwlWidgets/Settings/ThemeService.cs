using System.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OwlWidgets.Services;

namespace OwlWidgets.Settings;

/// <summary>
/// Applies the light or dark Material Design base theme and follows Windows while the mode is <see cref="AppTheme.System"/>.
/// </summary>
internal sealed class ThemeService : IDisposable
{
    private readonly PaletteHelper _palette = new();
    private readonly ILogger<ThemeService> _log;
    private readonly IUiDispatcher _ui;
    private AppTheme _mode;

    public ThemeService(ILogger<ThemeService> log, IUiDispatcher ui)
    {
        _log = log;
        _ui = ui;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Raised on the UI thread after the base theme changed.
    /// </summary>
    public event EventHandler? Changed;

    public bool IsDark { get; private set; }

    public void Apply(AppTheme mode)
    {
        _mode = mode;
        ApplyCore();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void ApplyCore()
    {
        var dark = _mode switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => Theme.GetSystemTheme() == BaseTheme.Dark
        };

        var theme = _palette.GetTheme();
        theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
        _palette.SetTheme(theme);

        IsDark = dark;
        _log.LogDebug("Theme applied: mode {Mode}, dark {Dark}", _mode, dark);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != AppTheme.System || e.Category != UserPreferenceCategory.General)
            return;
        _ui.Post(() =>
        {
            if (_mode == AppTheme.System && (Theme.GetSystemTheme() == BaseTheme.Dark) != IsDark)
                ApplyCore();
        });
    }
}
