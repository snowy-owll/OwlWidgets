using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.Logging;
using OwlWidgets.Protocol;

namespace OwlWidgets.Settings;

/// <summary>
/// Applies the UI language once at startup. A different choice on the settings screen is stored
/// and takes effect after the application restarts.
/// </summary>
internal sealed class LanguageService
{
    private readonly ILogger<LanguageService> _log;

    /// <summary>The Windows display language, captured before the application replaces the UI culture.</summary>
    private readonly CultureInfo _system = CultureInfo.CurrentUICulture;

    public LanguageService(ILogger<LanguageService> log) => _log = log;

    /// <summary>The language this process shows, one of <see cref="UiLanguage.Supported"/>.</summary>
    public string Current { get; private set; } = UiLanguage.Default;

    public string Resolve(AppLanguage language) => language switch
    {
        AppLanguage.English => UiLanguage.English,
        AppLanguage.Russian => UiLanguage.Russian,
        _ => UiLanguage.Resolve(_system)
    };

    public bool RequiresRestart(AppLanguage language) => Resolve(language) != Current;

    /// <summary>
    /// Must run before the first window and the tray menu are created: <c>x:Static</c> reads a string once.
    /// </summary>
    public void Apply(AppLanguage language)
    {
        Current = Resolve(language);
        UiLanguage.Apply(Current);
        UseRegionalFormats();
        _log.LogInformation("Interface language {Language} (setting {Setting}, Windows {SystemLanguage}), formats {Formats}",
            Current, language, _system.Name, CultureInfo.CurrentCulture.Name);
    }

    /// <summary>
    /// WPF formats bindings by <see cref="FrameworkElement.Language"/>, which is en-US unless set,
    /// not by <see cref="CultureInfo.CurrentCulture"/>. The default is replaced once per process.
    /// </summary>
    private static void UseRegionalFormats() =>
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
}
