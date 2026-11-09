using System.Globalization;
using System.Text.Json;
using OwlWidgets.Widgets.Weather.Properties;

namespace OwlWidgets.Widgets.Weather.Providers;

/// <summary>
/// Open-Meteo, no key: current weather and the hourly forecast for today and 7 days by coordinates.
/// Times are requested as Unix time, UTC.
/// </summary>
internal sealed class OpenMeteoProvider : IWeatherProvider
{
    private const string Forecast = "https://api.open-meteo.com/v1/forecast";

    private readonly string _name;
    // The place in a request: latitude={lat}&longitude={lon}.
    private readonly string _location;

    /// <param name="place">Place chosen on the settings screen: its coordinates and name are used as they are.</param>
    public OpenMeteoProvider(WeatherPlace place)
    {
        _name = place.Name;
        _location = string.Format(CultureInfo.InvariantCulture, "latitude={0}&longitude={1}", place.Latitude, place.Longitude);
    }

    public string Name => "Open-Meteo";

    public WeatherAttribution? Attribution { get; } =
        new(Strings.Attribution_OpenMeteo, new Uri("https://open-meteo.com/"));

    public async Task<CurrentWeather> GetCurrentAsync(CancellationToken token)
    {
        using var json = JsonDocument.Parse(await WeatherHttp.GetStringAsync(
            $"{Forecast}?{_location}&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m,wind_direction_10m,pressure_msl"
            + "&daily=sunrise,sunset&forecast_days=1&wind_speed_unit=ms&timezone=auto&timeformat=unixtime",
            token));
        var root = json.RootElement;
        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        return new CurrentWeather(
            _name,
            TimeSpan.FromSeconds(root.GetProperty("utc_offset_seconds").GetInt32()),
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            (int)Math.Round(current.GetProperty("relative_humidity_2m").GetDouble()),
            current.GetProperty("wind_speed_10m").GetDouble(),
            current.TryGetProperty("wind_direction_10m", out var direction) && direction.ValueKind == JsonValueKind.Number
                ? direction.GetDouble()
                : null,
            current.GetProperty("pressure_msl").GetDouble(),
            FirstTime(daily, "sunrise"),
            FirstTime(daily, "sunset"),
            Icon(current.GetProperty("weather_code").GetInt32(), current.GetProperty("is_day").GetInt32() == 1));
    }

    public async Task<WeatherForecast> GetForecastAsync(CancellationToken token)
    {
        using var json = JsonDocument.Parse(await WeatherHttp.GetStringAsync(
            $"{Forecast}?{_location}&hourly=temperature_2m,weather_code,is_day&forecast_days=8&timezone=auto&timeformat=unixtime",
            token));
        var root = json.RootElement;
        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time").EnumerateArray().ToList();
        var temperatures = hourly.GetProperty("temperature_2m").EnumerateArray().ToList();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().ToList();
        var days = hourly.GetProperty("is_day").EnumerateArray().ToList();
        var points = new List<ForecastPoint>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            // The far end of the forecast may have no values yet.
            if (temperatures[i].ValueKind != JsonValueKind.Number || codes[i].ValueKind != JsonValueKind.Number)
                continue;
            var from = DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64()).UtcDateTime;
            points.Add(new ForecastPoint(from, from.AddHours(1), temperatures[i].GetDouble(), Icon(codes[i].GetInt32(), days[i].GetInt32() == 1)));
        }

        return new WeatherForecast(TimeSpan.FromSeconds(root.GetProperty("utc_offset_seconds").GetInt32()), points);
    }

    private static DateTime? FirstTime(JsonElement daily, string name) =>
        daily.TryGetProperty(name, out var values) && values.GetArrayLength() > 0 && values[0].ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(values[0].GetInt64()).UtcDateTime
            : null;

    /// <summary>WMO weather interpretation code to the OpenWeatherMap icon set the widget ships.</summary>
    private static string Icon(int code, bool isDay)
    {
        var icon = code switch
        {
            0 => "01",
            1 => "02",
            2 => "03",
            3 => "04",
            45 or 48 => "50",
            >= 51 and <= 57 => "09",
            >= 61 and <= 67 => "10",
            >= 71 and <= 77 => "13",
            >= 80 and <= 82 => "09",
            85 or 86 => "13",
            >= 95 => "11",
            _ => "03"
        };
        return icon + (isDay ? "d" : "n");
    }
}
