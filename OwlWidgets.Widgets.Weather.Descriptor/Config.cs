using System.Globalization;
using System.Text.Json.Serialization;

namespace OwlWidgets.Widgets.Weather;

/// <summary>Source of the weather data of an instance.</summary>
public enum WeatherProviderKind
{
    /// <summary>OpenWeatherMap, needs <see cref="Config.ApiKey"/>. The default: older settings have no provider.</summary>
    OpenWeatherMap,

    /// <summary>Open-Meteo, no key.</summary>
    OpenMeteo
}

public class Config
{
    public const int DefaultUpdateInterval = 1000 * 60 * 10;

    public Config()
    {
        UpdateInterval = DefaultUpdateInterval;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WeatherProviderKind Provider { get; set; }

    public int UpdateInterval { get; set; }

    /// <summary>
    /// Place chosen from the search on the settings screen; every provider requests the weather by its coordinates.
    /// </summary>
    public WeatherPlace? Place { get; set; }

    /// <summary>The place for the log, the same for instances with one place: <c>Tbilisi,GE</c>.</summary>
    [JsonIgnore]
    public string? LocationKey => Place == null
        ? null
        : Place.CountryCode == null ? Place.Name : $"{Place.Name},{Place.CountryCode}";

    /// <summary>OpenWeatherMap key; other providers do not use it.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Language code of the name of the place: in the search and on the card. null or empty: the UI language of the application.
    /// </summary>
    public string? Lang { get; set; }

    /// <summary><see cref="Lang"/>, else the UI language of the application; lower case, as the geocoders give it.</summary>
    [JsonIgnore]
    public string NameLang => WeatherPlace.NormalizeLang(
        string.IsNullOrWhiteSpace(Lang) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : Lang);

    /// <summary>Name of the place for the card, in <see cref="NameLang"/> when it is known.</summary>
    [JsonIgnore]
    public string? PlaceName => Place?.NameIn(NameLang);

    /// <summary>
    /// Tabs with today and the following days under the current weather, points of the selected day below them.
    /// </summary>
    public bool ShowForecast { get; set; }

    /// <summary>
    /// Folder of the imported icon pack in the files of the instance, see <see cref="IconPacks"/>; null: the built-in icons.
    /// </summary>
    public string? IconPack { get; set; }

    /// <summary>Name of the zip archive or the folder the pack came from, for the settings screen.</summary>
    public string? IconPackSource { get; set; }
}

/// <summary>A place found by the geocoder of a provider. The coordinates fit any provider.</summary>
public sealed record WeatherPlace
{
    /// <summary>
    /// Languages of the interface of the application: names in them are taken when a place is chosen,
    /// so the card has its name after the language of the application changes.
    /// </summary>
    public static readonly IReadOnlyList<string> AppLanguages = ["en", "ru"];

    /// <summary>Name of the place in the language of the search; the card shows it when <see cref="Names"/> lacks its language.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Names by language code, lower case: <c>{"en": "Tbilisi", "ru": "Тбилиси"}</c>; null in the settings before 0.8.0.</summary>
    public Dictionary<string, string>? Names { get; init; }

    /// <summary>Id of the place at Open-Meteo (GeoNames): its name in another language is asked by it. null for OpenWeatherMap.</summary>
    public long? GeoNamesId { get; init; }

    /// <summary>Region of the country; null when the geocoder gave none.</summary>
    public string? Region { get; init; }

    /// <summary>Name of the country in the language of the search; null when unknown.</summary>
    public string? Country { get; init; }

    /// <summary>ISO 3166-1 alpha-2 code of the country, upper case; null when unknown.</summary>
    public string? CountryCode { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    /// <summary>The name in <paramref name="lang"/>, else <see cref="Name"/>.</summary>
    public string NameIn(string lang) =>
        Names != null && Names.TryGetValue(NormalizeLang(lang), out var name) && !string.IsNullOrWhiteSpace(name) ? name : Name;

    public bool HasName(string lang) => Names?.ContainsKey(NormalizeLang(lang)) == true;

    /// <summary>The same place with the name in <paramref name="lang"/> added to <see cref="Names"/>.</summary>
    public WeatherPlace WithName(string lang, string name)
    {
        var names = Names == null ? new Dictionary<string, string>() : new Dictionary<string, string>(Names);
        names[NormalizeLang(lang)] = name;
        return this with { Names = names };
    }

    /// <summary>The same place with only the names in <paramref name="languages"/>; itself when it has no other.</summary>
    public WeatherPlace KeepNames(IReadOnlySet<string> languages)
    {
        if (Names == null || Names.Keys.All(languages.Contains))
            return this;
        var kept = Names.Where(name => languages.Contains(name.Key)).ToDictionary(name => name.Key, name => name.Value);
        return this with { Names = kept.Count > 0 ? kept : null };
    }

    /// <summary>Languages a place keeps its name in: those of the application and <paramref name="lang"/> of the settings.</summary>
    public static IReadOnlySet<string> NameLanguages(string lang) =>
        AppLanguages.Append(NormalizeLang(lang)).ToHashSet();

    public static string NormalizeLang(string lang) => lang.Trim().ToLowerInvariant();

    /// <summary>Region and country, without a region that repeats the name of the place.</summary>
    [JsonIgnore]
    public string Details => string.Join(", ", new[] { Region, Country }
        .Where(part => !string.IsNullOrWhiteSpace(part) && !string.Equals(part, Name, StringComparison.CurrentCultureIgnoreCase)));

    /// <summary>Name, region and country on one line.</summary>
    [JsonIgnore]
    public string FullName => Details.Length > 0 ? $"{Name}, {Details}" : Name;

    /// <summary><see cref="FullName"/> with the name in <paramref name="lang"/>.</summary>
    public string FullNameIn(string lang) => Details.Length > 0 ? $"{NameIn(lang)}, {Details}" : NameIn(lang);
}
