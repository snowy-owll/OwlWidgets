using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace OwlWidgets.Widgets.Weather;

/// <summary>
/// Search of a place by its name with the geocoder of the selected provider, for the settings screen.
/// The found coordinates fit any provider.
/// </summary>
internal static class PlaceSearch
{
    /// <summary>The shortest query the geocoders answer.</summary>
    public const int MinQueryLength = 2;

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>The geocoder gives the same coordinates of a record every time; the margin is only for the rounding of doubles.</summary>
    private const double SameCoordinate = 1e-6;

    /// <summary>Searches of an OpenWeatherMap place by its names before its name in a new language is given up.</summary>
    private const int MaxNameQueries = 3;

    /// <param name="apiKey">OpenWeatherMap key; Open-Meteo does not use it.</param>
    /// <param name="lang">Language of the names of the places.</param>
    /// <remarks>
    /// <see cref="WeatherPlace.Names"/> of the found places hold only the languages of <see cref="WeatherPlace.NameLanguages"/>
    /// that the answer has; the missing ones are asked with <see cref="GetNameAsync"/> once a place is chosen.
    /// </remarks>
    public static Task<IReadOnlyList<WeatherPlace>> FindAsync(
        WeatherProviderKind provider, string query, string? apiKey, string lang, CancellationToken token) =>
        provider == WeatherProviderKind.OpenMeteo
            ? FindOpenMeteoAsync(query, lang, token)
            : FindOpenWeatherMapAsync(query, apiKey ?? string.Empty, lang, token);

    /// <summary>
    /// Name of a chosen place in <paramref name="lang"/>, whichever provider found it and whichever is selected:
    /// by the Open-Meteo id when the place has one, no key needed; else the OpenWeatherMap record of the place, found
    /// again by its name, which needs the key. null when neither knows the name, or there is no key.
    /// </summary>
    public static Task<string?> GetNameAsync(WeatherPlace place, string lang, string? apiKey, CancellationToken token) =>
        place.GeoNamesId is { } id
            ? GetOpenMeteoNameAsync(id, lang, token)
            : string.IsNullOrWhiteSpace(apiKey)
                ? Task.FromResult<string?>(null)
                : GetOpenWeatherMapNameAsync(place, lang, apiKey.Trim(), token);

    /// <summary>
    /// The place of the computer by its public IP address (<c>https://ipinfo.io/json</c>, no key), matched with the
    /// Open-Meteo geocoder so it has an id to ask its name in other languages by: the nearest place of that name in that
    /// country. The city of ipinfo.io with its coordinates when the geocoder has none near; null when ipinfo.io knows no city.
    /// </summary>
    public static async Task<WeatherPlace?> FindByIpAsync(string lang, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await GetStringAsync("https://ipinfo.io/json", token));
        var city = Text(json.RootElement, "city");
        var location = Text(json.RootElement, "loc")?.Split(',');
        if (city == null || location is not { Length: 2 }
            || !double.TryParse(location[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(location[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            return null;

        var countryCode = Text(json.RootElement, "country")?.ToUpperInvariant();
        var nearest = (await FindOpenMeteoAsync(city, lang, token))
            .Where(place => countryCode == null || place.CountryCode == countryCode)
            .MinBy(place => DistanceKm(place, latitude, longitude));
        if (nearest != null && DistanceKm(nearest, latitude, longitude) <= MaxIpPlaceDistanceKm)
            return nearest;

        // ipinfo.io names places in English.
        return new WeatherPlace
        {
            Name = city,
            Names = new Dictionary<string, string> { ["en"] = city },
            Region = Text(json.RootElement, "region"),
            Country = CountryName(countryCode),
            CountryCode = countryCode,
            Latitude = latitude,
            Longitude = longitude
        };
    }

    /// <summary>How far the coordinates of a city by IP may be from the geocoder's place of that name; IP databases are coarse.</summary>
    private const double MaxIpPlaceDistanceKm = 100;

    /// <summary>Equirectangular approximation: precise enough at the distances of one city.</summary>
    private static double DistanceKm(WeatherPlace place, double latitude, double longitude)
    {
        const double earthRadiusKm = 6371;
        var x = (place.Longitude - longitude) * Math.PI / 180 * Math.Cos((place.Latitude + latitude) / 2 * Math.PI / 180);
        var y = (place.Latitude - latitude) * Math.PI / 180;
        return Math.Sqrt(x * x + y * y) * earthRadiusKm;
    }

    private static async Task<IReadOnlyList<WeatherPlace>> FindOpenMeteoAsync(string query, string lang, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await GetStringAsync(
            $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=8&language={Uri.EscapeDataString(lang)}&format=json",
            token));
        var places = new List<WeatherPlace>();
        // No results: the answer has no "results" at all.
        if (!json.RootElement.TryGetProperty("results", out var results))
            return places;
        foreach (var result in results.EnumerateArray())
        {
            var name = Text(result, "name") ?? query;
            places.Add(new WeatherPlace
            {
                Name = name,
                Names = new Dictionary<string, string> { [WeatherPlace.NormalizeLang(lang)] = name },
                GeoNamesId = result.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) ? value : null,
                Region = Text(result, "admin1"),
                Country = Text(result, "country"),
                CountryCode = Text(result, "country_code")?.ToUpperInvariant(),
                Latitude = result.GetProperty("latitude").GetDouble(),
                Longitude = result.GetProperty("longitude").GetDouble()
            });
        }

        return places;
    }

    /// <summary>The geocoder gives at most 5 places and only the code of the country; its name comes from the system.</summary>
    private static async Task<IReadOnlyList<WeatherPlace>> FindOpenWeatherMapAsync(string query, string apiKey, string lang, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await GetStringAsync(
            $"http://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(query)}&limit=5&appid={Uri.EscapeDataString(apiKey)}",
            token));
        var places = new List<WeatherPlace>();
        var languages = WeatherPlace.NameLanguages(lang);
        foreach (var result in json.RootElement.EnumerateArray())
        {
            var code = Text(result, "country")?.ToUpperInvariant();
            // The answer has dozens of languages; the place keeps only the ones it needs.
            var localNames = LocalNames(result)
                .Where(name => languages.Contains(name.Key))
                .ToDictionary(name => name.Key, name => name.Value);
            places.Add(new WeatherPlace
            {
                Name = (localNames.TryGetValue(WeatherPlace.NormalizeLang(lang), out var localName) ? localName : null)
                    ?? Text(result, "name") ?? query,
                Names = localNames.Count > 0 ? localNames : null,
                Region = Text(result, "state"),
                Country = CountryName(code),
                CountryCode = code,
                Latitude = result.GetProperty("lat").GetDouble(),
                Longitude = result.GetProperty("lon").GetDouble()
            });
        }

        return places;
    }

    /// <summary>
    /// Name of a place found at Open-Meteo in another language, by its id:
    /// <c>https://geocoding-api.open-meteo.com/v1/get?id={id}&amp;language={lang}</c>. null when the answer has no name.
    /// </summary>
    private static async Task<string?> GetOpenMeteoNameAsync(long id, string lang, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await GetStringAsync(
            $"https://geocoding-api.open-meteo.com/v1/get?id={id.ToString(CultureInfo.InvariantCulture)}&language={Uri.EscapeDataString(lang)}",
            token));
        return Text(json.RootElement, "name");
    }

    /// <summary>
    /// OpenWeatherMap has no id of a place, so the place is searched again by a name the geocoder gave for it, with the
    /// country, and recognised by its coordinates: the geocoder gives a record with the same coordinates every time.
    /// The reverse geocoder does not fit: for Aktau it gives another record of the city, 0.02° away.
    /// English first, then the other known names. null when no search finds the record or the record lacks the language.
    /// </summary>
    private static async Task<string?> GetOpenWeatherMapNameAsync(WeatherPlace place, string lang, string apiKey, CancellationToken token)
    {
        var queries = new[] { place.Names?.GetValueOrDefault("en"), place.Name }
            .Concat(place.Names?.Values ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxNameQueries);
        foreach (var query in queries)
        {
            var q = place.CountryCode == null ? query! : $"{query},{place.CountryCode}";
            using var json = JsonDocument.Parse(await GetStringAsync(
                $"http://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(q)}&limit=5&appid={Uri.EscapeDataString(apiKey)}",
                token));
            foreach (var result in json.RootElement.EnumerateArray())
            {
                if (Math.Abs(result.GetProperty("lat").GetDouble() - place.Latitude) < SameCoordinate
                    && Math.Abs(result.GetProperty("lon").GetDouble() - place.Longitude) < SameCoordinate)
                    return LocalNames(result).GetValueOrDefault(WeatherPlace.NormalizeLang(lang));
            }
        }

        return null;
    }

    /// <summary>
    /// <c>local_names</c> of an OpenWeatherMap place by language code, lower case.
    /// The service keys <c>ascii</c> and <c>feature_name</c> are not languages.
    /// </summary>
    private static Dictionary<string, string> LocalNames(JsonElement result)
    {
        var names = new Dictionary<string, string>();
        if (!result.TryGetProperty("local_names", out var localNames) || localNames.ValueKind != JsonValueKind.Object)
            return names;
        foreach (var property in localNames.EnumerateObject())
        {
            if (property.Name is "ascii" or "feature_name" || property.Value.ValueKind != JsonValueKind.String)
                continue;
            var name = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                names[WeatherPlace.NormalizeLang(property.Name)] = name;
        }

        return names;
    }

    /// <summary>Name of the country in the language of the application; the code itself when the system does not know it.</summary>
    private static string? CountryName(string? code)
    {
        if (code == null)
            return null;
        try
        {
            return new RegionInfo(code).DisplayName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static async Task<string> GetStringAsync(string url, CancellationToken token)
    {
        using var response = await Client.GetAsync(url, token);
        // The message holds the status code only: a URL with a key stays out of it.
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }
}
