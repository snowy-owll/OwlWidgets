namespace OwlWidgets.Widgets.Weather.Providers;

/// <summary>
/// Source of the weather data. The view model knows only these types: the requests, the formats
/// and the codes of the conditions stay inside the provider.
/// </summary>
internal interface IWeatherProvider
{
    /// <summary>Name for the log, the same in every language.</summary>
    string Name { get; }

    /// <summary>
    /// Credit the provider asks to show next to its data; null when it asks for none.
    /// The card then shows the info button with this text and link.
    /// </summary>
    WeatherAttribution? Attribution { get; }

    Task<CurrentWeather> GetCurrentAsync(CancellationToken token);

    Task<WeatherForecast> GetForecastAsync(CancellationToken token);
}

internal sealed record WeatherAttribution(string Text, Uri Link);

/// <param name="Location">Name of the chosen place, as stored in the settings.</param>
/// <param name="UtcOffset">Offset of the place from UTC; days, sunrise and sunset are shown in the time of the place.</param>
/// <param name="WindDirection">Where the wind blows from, degrees clockwise from north; null when unknown.</param>
/// <param name="Pressure">Sea level pressure, hPa.</param>
/// <param name="Icon">Code of the condition in the OpenWeatherMap icon set, e.g. <c>01d</c>.</param>
internal sealed record CurrentWeather(
    string Location,
    TimeSpan UtcOffset,
    double Temperature,
    double FeelsLike,
    int Humidity,
    double WindSpeed,
    double? WindDirection,
    double Pressure,
    DateTime? SunriseUtc,
    DateTime? SunsetUtc,
    string Icon);

/// <param name="Points">Points of any step, ordered by time; each covers <c>FromUtc</c> to <c>ToUtc</c>.</param>
internal sealed record WeatherForecast(TimeSpan UtcOffset, IReadOnlyList<ForecastPoint> Points);

/// <param name="Icon">Code of the condition in the OpenWeatherMap icon set, e.g. <c>01d</c>.</param>
internal sealed record ForecastPoint(DateTime FromUtc, DateTime ToUtc, double Temperature, string Icon);
