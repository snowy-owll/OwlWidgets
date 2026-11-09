using System.Text.Json;
using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.Weather;

/// <summary>
/// Settings of the instance the manager creates on its first start: Open-Meteo, which needs no key, and the place
/// of the computer by its IP address, London when it is not found.
/// </summary>
internal static class StarterSettings
{
    /// <summary>Shorter than the manager waits: the instance is created with London rather than with the defaults.</summary>
    private static readonly TimeSpan LocateTimeout = TimeSpan.FromSeconds(6);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The place of Open-Meteo, so its name in another language is asked by its id like any other.</summary>
    private static readonly WeatherPlace London = new()
    {
        Name = "London",
        Names = new Dictionary<string, string> { ["en"] = "London", ["ru"] = "Лондон" },
        GeoNamesId = 2643743,
        Region = "England",
        Country = "United Kingdom",
        CountryCode = "GB",
        Latitude = 51.50853,
        Longitude = -0.12574
    };

    public static async Task<string?> CreateAsync(IWidgetLogger log, CancellationToken token)
    {
        var config = new Config
        {
            Provider = WeatherProviderKind.OpenMeteo,
            Place = await LocateAsync(log, token)
        };
        return JsonSerializer.Serialize(config, Json);
    }

    private static async Task<WeatherPlace> LocateAsync(IWidgetLogger log, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(LocateTimeout);
        WeatherPlace? place;
        try
        {
            place = await PlaceSearch.FindByIpAsync("en", timeout.Token);
        }
        catch (Exception exception)
        {
            log.Log(WidgetLogLevel.Warning, "The place was not found by the IP address, London is used", exception);
            return London;
        }

        if (place == null)
        {
            log.Log(WidgetLogLevel.Warning, "The IP address gave no city, London is used");
            return London;
        }

        // Names in the languages of the application, so the card keeps its name when that language changes.
        foreach (var lang in WeatherPlace.AppLanguages.Where(lang => !place.HasName(lang)))
        {
            try
            {
                var name = await PlaceSearch.GetNameAsync(place, lang, null, timeout.Token);
                if (name != null)
                    place = place.WithName(lang, name);
            }
            catch (Exception exception)
            {
                log.Log(WidgetLogLevel.Warning, $"The name of {place.FullName} in \"{lang}\" was not found", exception);
            }
        }

        log.Log(WidgetLogLevel.Information, $"Found {place.FullName} by the IP address");
        return place;
    }
}
