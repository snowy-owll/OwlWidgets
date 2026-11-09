using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using OwlWidgets.Widgets.Weather.Properties;

namespace OwlWidgets.Widgets.Weather.Providers;

/// <summary>
/// OpenWeatherMap, free plan: current weather and the 5 day / 3 hour forecast, XML, metric.
/// Times in the responses are UTC.
/// </summary>
internal sealed class OpenWeatherMapProvider : IWeatherProvider
{
    // The place in a request: lat={lat}&lon={lon}.
    private readonly string _location;
    private readonly string _name;
    private readonly string _apiKey;

    /// <param name="place">
    /// Place chosen on the settings screen. Its name is shown instead of the one in the answer:
    /// by coordinates the answer names the nearest place the service knows, which may be another one.
    /// </param>
    public OpenWeatherMapProvider(WeatherPlace place, string apiKey)
    {
        _location = string.Format(CultureInfo.InvariantCulture, "lat={0}&lon={1}", place.Latitude, place.Longitude);
        _name = place.Name;
        _apiKey = apiKey;
    }

    public string Name => "OpenWeatherMap";

    public WeatherAttribution? Attribution { get; } =
        new(Strings.Attribution_OpenWeatherMap, new Uri("https://openweathermap.org/"));

    public async Task<CurrentWeather> GetCurrentAsync(CancellationToken token)
    {
        var data = Parse<WeatherData>(await WeatherHttp.GetStringAsync(
            $"http://api.openweathermap.org/data/2.5/weather?{_location}&appid={_apiKey}&units=metric&mode=xml",
            token));
        return new CurrentWeather(
            _name,
            TimeSpan.FromSeconds(data.City.Timezone),
            data.Temperature.Value,
            data.FeelsLike.Value,
            data.Humidity.Value,
            data.Wind.Speed.Value,
            data.Wind.Direction?.Value,
            data.Pressure.Value,
            data.City.Sun != null ? Utc(data.City.Sun.Rise) : null,
            data.City.Sun != null ? Utc(data.City.Sun.Set) : null,
            data.Weather.Icon);
    }

    public async Task<WeatherForecast> GetForecastAsync(CancellationToken token)
    {
        var data = Parse<ForecastData>(await WeatherHttp.GetStringAsync(
            $"http://api.openweathermap.org/data/2.5/forecast?{_location}&appid={_apiKey}&units=metric&mode=xml",
            token));
        return new WeatherForecast(
            TimeSpan.FromSeconds(data.Location.Timezone),
            // The values of a time element are for its "to": the JSON mode gives them with dt equal to it.
            // A point then covers the step after that moment, as an hourly point covers its hour.
            data.Times
                .Select(time => new ForecastPoint(Utc(time.To), Utc(time.To) + (time.To - time.From), time.Temperature.Value, time.Symbol.Icon ?? "01d"))
                .OrderBy(point => point.FromUtc)
                .ToList());
    }

    private static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);

    private static T Parse<T>(string xml)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var reader = new StringReader(xml);
        return (T)serializer.Deserialize(reader)!;
    }
}
