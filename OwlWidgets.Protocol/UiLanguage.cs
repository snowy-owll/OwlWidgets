using System.Globalization;

namespace OwlWidgets.Protocol;

/// <summary>
/// Languages of the user interface. The language is chosen once per process start:
/// strings come from resources through <c>x:Static</c> and do not change at run time.
/// </summary>
public static class UiLanguage
{
    public const string English = "en";

    public const string Russian = "ru";

    /// <summary>The neutral language of the resources and the fallback for anything unsupported.</summary>
    public const string Default = English;

    public static IReadOnlyList<string> Supported { get; } = [English, Russian];

    /// <summary>
    /// The supported language matching <paramref name="culture"/> by its two-letter code, otherwise <see cref="Default"/>.
    /// </summary>
    public static string Resolve(CultureInfo culture)
    {
        var code = culture.TwoLetterISOLanguageName;
        return Supported.Contains(code) ? code : Default;
    }

    /// <summary>
    /// Sets the UI culture of the calling thread and the default for threads that have not set one.
    /// Number and date formats (<see cref="CultureInfo.CurrentCulture"/>) keep the Windows regional settings.
    /// </summary>
    public static void Apply(string language)
    {
        var culture = CultureInfo.GetCultureInfo(Supported.Contains(language) ? language : Default);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
