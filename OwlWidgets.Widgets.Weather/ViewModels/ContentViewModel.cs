using OwlWidgets.Common;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.Weather.Properties;
using OwlWidgets.Widgets.Weather.Providers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;

namespace OwlWidgets.Widgets.Weather.ViewModels
{
    internal class ContentViewModel : ViewModelBase
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ForecastInterval = TimeSpan.FromMinutes(30);

        /// <summary>In place of a number before the first answer.</summary>
        private const string NoValue = "—";

        /// <summary>Sunrise and sunset unknown: the shape of two times.</summary>
        private const string NoSun = NoValue + " / " + NoValue;

        /// <summary>Marks the records with the received data in the log of the instance, to pick them out later.</summary>
        public const string DataLogMarker = "[weather-data]";

        private static readonly JsonSerializerOptions LogJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        [Obsolete("Design-time only", true)]
        public ContentViewModel()
        {
            _config = new Config();
            _icons = WeatherIcons.BuiltIn;
            OpenSettingsCommand = new RelayCommand(() => { });
            RefreshCommand = new RelayCommand(() => { });
            AttributionText = Strings.Attribution_OpenMeteo;
            Location = "San Francisco";
            ShowForecastPanel = true;
            Temperature = FormatDegrees(34);
            Humidity = FormatHumidity(65);
            WindSpeed = FormatWindSpeed(12);
            WindArrowAngle = 225;
            FeelsLike = string.Format(CultureInfo.CurrentCulture, Strings.Format_FeelsLike, 31);
            Pressure = string.Format(CultureInfo.CurrentCulture, Strings.Format_PressureHpa, 1013);
            Sun = "6:58 / 18:41";
            WeatherIcon = _icons.Get("09d");
            _updated = DateTime.Now;
            ShowStatus(false);

            var today = DateTime.Today;
            var slots = Enumerable.Range(0, 8)
                .Select(i => new ForecastSlotViewModel(
                    today.AddHours(i * 3).ToString("t", CultureInfo.CurrentCulture),
                    _icons.Get(i is > 1 and < 6 ? "02d" : "01n"),
                    14 + i))
                .ToList();
            ForecastDays =
            [
                new ForecastDayViewModel(today, true, null, null, slots.Skip(5).ToList()),
                .. Enumerable.Range(1, 4).Select(i => new ForecastDayViewModel(today.AddDays(i), false, _icons.Get("10d"), 20 + i, slots))
            ];
            SelectedForecastDay = ForecastDays[0];
        }

        /// <param name="provider">null when the settings lack what the provider needs: the card asks to open the settings.</param>
        /// <param name="icons">Icons of the pack of the settings, else the built-in ones.</param>
        public ContentViewModel(Config config, IWeatherProvider? provider, WeatherIcons icons, IWidgetLogger logger, Action openSettings)
        {
            _config = config;
            _provider = provider;
            _icons = icons;
            _logger = logger;
            OpenSettingsCommand = new RelayCommand(openSettings);
            RefreshCommand = new RelayCommand(Refresh);
            AttributionText = provider?.Attribution?.Text;
            AttributionLink = provider?.Attribution?.Link;
            if (provider == null)
                EmptyMessage = config.Place == null ? Strings.Empty_NoPlace : Strings.Empty_NoKey;
            // The name in the language of the settings, else of the application; it does not come from the answer.
            // Until the first answer the card keeps its layout: the place and dashes for the values.
            Location = config.PlaceName;
            ShowForecastPanel = config.ShowForecast;
            ShowPlaceholders();
        }

        #region Public properties
        /// <summary>
        /// The settings lack a place or a key: the card shows only <see cref="EmptyMessage"/> and the settings button,
        /// over its hidden layout, so its size stays that of a card with the weather.
        /// </summary>
        public bool IsEmpty => EmptyMessage != null;

        /// <summary>What the settings lack; null when the weather is requested.</summary>
        public string? EmptyMessage { get; }

        public ICommand OpenSettingsCommand { get; }

        /// <summary>Requests the weather at once instead of waiting for the retry.</summary>
        public ICommand RefreshCommand { get; }

        public string? Location
        {
            get => GetValue(() => Location);
            set => SetValue(() => Location, value);
        }

        // The values come formatted, with the unit: before the first answer the same text holds a dash,
        // so the card has its size from the start.

        /// <summary>Temperature with the degree sign.</summary>
        public string? Temperature
        {
            get => GetValue(() => Temperature);
            set => SetValue(() => Temperature, value);
        }

        /// <summary>Humidity with the percent sign.</summary>
        public string? Humidity
        {
            get => GetValue(() => Humidity);
            set => SetValue(() => Humidity, value);
        }

        /// <summary>Wind speed with its unit.</summary>
        public string? WindSpeed
        {
            get => GetValue(() => WindSpeed);
            set => SetValue(() => WindSpeed, value);
        }

        /// <summary>The line under the temperature: "Feels like N°".</summary>
        public string? FeelsLike
        {
            get => GetValue(() => FeelsLike);
            set => SetValue(() => FeelsLike, value);
        }

        /// <summary>The forecast is on in the settings: its room is kept on the card before it is loaded.</summary>
        public bool ShowForecastPanel
        {
            get => GetValue(() => ShowForecastPanel);
            set => SetValue(() => ShowForecastPanel, value);
        }

        /// <summary>In the room of the forecast while it is not loaded: loading or failed; null once it is shown.</summary>
        public string? ForecastStatus
        {
            get => GetValue(() => ForecastStatus);
            set => SetValue(() => ForecastStatus, value);
        }

        /// <summary>Where the wind blows to, degrees clockwise from north; null when the direction is unknown.</summary>
        public double? WindArrowAngle
        {
            get => GetValue(() => WindArrowAngle);
            set => SetValue(() => WindArrowAngle, value);
        }

        /// <summary>Pressure with its unit.</summary>
        public string? Pressure
        {
            get => GetValue(() => Pressure);
            set => SetValue(() => Pressure, value);
        }

        /// <summary>Sunrise and sunset, the time of the location.</summary>
        public string? Sun
        {
            get => GetValue(() => Sun);
            set => SetValue(() => Sun, value);
        }

        public ImageSource? WeatherIcon
        {
            get => GetValue(() => WeatherIcon);
            set => SetValue(() => WeatherIcon, value);
        }

        /// <summary>Credit of the data source for the info button; null hides the button.</summary>
        public string? AttributionText
        {
            get => GetValue(() => AttributionText);
            set => SetValue(() => AttributionText, value);
        }

        public Uri? AttributionLink
        {
            get => GetValue(() => AttributionLink);
            set => SetValue(() => AttributionLink, value);
        }

        public string? Status
        {
            get => GetValue(() => Status);
            set => SetValue(() => Status, value);
        }

        /// <summary>The last request of the current weather failed: the refresh button is shown.</summary>
        public bool Failed
        {
            get => GetValue(() => Failed);
            set => SetValue(() => Failed, value);
        }

        /// <summary>Tabs of the forecast: today and the following days; null while the forecast is off or not loaded.</summary>
        public IReadOnlyList<ForecastDayViewModel>? ForecastDays
        {
            get => GetValue(() => ForecastDays);
            set => SetValue(() => ForecastDays, value);
        }

        public ForecastDayViewModel? SelectedForecastDay
        {
            get => GetValue(() => SelectedForecastDay);
            set => SetValue(() => SelectedForecastDay, value);
        }
        #endregion

        #region Internal methods
        /// <summary>Runs on the UI thread: the request is awaited there and the properties are set in place.</summary>
        internal void Start()
        {
            if (_provider == null)
                return;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            Status = Strings.Status_Loading;
            Failed = false;
            _ = RunAsync(_provider, _cancellationTokenSource.Token);
            if (_config.ShowForecast)
                _ = RunForecastAsync(_provider, _cancellationTokenSource.Token);
        }

        /// <summary>Opens the website of the data source in the default browser.</summary>
        internal void OpenAttribution()
        {
            if (AttributionLink == null)
                return;
            try
            {
                Process.Start(new ProcessStartInfo(AttributionLink.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                _logger?.Log(WidgetLogLevel.Warning, "The website of the weather data source was not opened.", exception);
            }
        }

        internal void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = null;
        }

        /// <summary>Starts the requests over: the current weather and the forecast are requested at once.</summary>
        private void Refresh()
        {
            if (_provider == null || _cancellationTokenSource == null)
                return;
            _logger?.Log(WidgetLogLevel.Information, "Refresh requested.");
            Stop();
            Start();
        }
        #endregion

        #region Private fields

        private readonly Config _config;
        private readonly IWeatherProvider? _provider;
        private readonly WeatherIcons _icons;
        private readonly IWidgetLogger? _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private DateTime? _updated;

        #endregion

        #region Private methods
        private async Task RunAsync(IWeatherProvider provider, CancellationToken token)
        {
            var interval = TimeSpan.FromMilliseconds(
                _config.UpdateInterval > 0 ? _config.UpdateInterval : Config.DefaultUpdateInterval);
            TimeSpan? utcOffset = null;
            while (!token.IsCancellationRequested)
            {
                CurrentWeather? weather = null;
                try
                {
                    weather = await provider.GetCurrentAsync(token);
                    LogData(provider, weather);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger?.Log(WidgetLogLevel.Warning, "Weather was not updated.", exception);
                }

                var updated = weather != null;
                if (weather != null)
                {
                    Show(weather);
                    utcOffset = weather.UtcOffset;
                }
                ShowStatus(!updated);

                var delay = updated || interval < RetryDelay ? interval : RetryDelay;
                if (utcOffset != null)
                {
                    var untilMidnight = NextMidnight(utcOffset.Value) - DateTime.UtcNow;
                    if (untilMidnight < delay)
                        delay = untilMidnight;
                }

                try
                {
                    await WallClock.DelayAsync(delay, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// The forecast is requested less often than the current weather: its points are hours apart.
        /// The tabs are also rebuilt from the last response when a point begins, so the past points of today
        /// drop out at once, even when the request fails. At midnight of the location the forecast is requested
        /// whenever it was requested before, and the tabs move to the new day.
        /// </summary>
        private async Task RunForecastAsync(IWeatherProvider provider, CancellationToken token)
        {
            WeatherForecast? forecast = null;
            var nextRequest = DateTime.MinValue;
            while (!token.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= nextRequest)
                {
                    // Tabs shown before a refresh stay until the new answer.
                    if (ForecastDays == null)
                        ForecastStatus = Strings.Forecast_Loading;
                    try
                    {
                        forecast = await provider.GetForecastAsync(token);
                        ForecastStatus = null;
                        var nextInterval = DateTime.UtcNow + ForecastInterval;
                        var midnight = NextMidnight(forecast.UtcOffset);
                        nextRequest = midnight < nextInterval ? midnight : nextInterval;
                        LogData(provider, forecast);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        nextRequest = DateTime.UtcNow + RetryDelay;
                        if (ForecastDays == null)
                            ForecastStatus = Strings.Forecast_Failed;
                        _logger?.Log(WidgetLogLevel.Warning, "Forecast was not updated.", exception);
                    }
                }

                var wake = nextRequest;
                if (forecast != null)
                {
                    ShowForecast(forecast);
                    var now = DateTime.UtcNow;
                    var nextPoint = forecast.Points.Select(point => point.FromUtc).Where(from => from > now).DefaultIfEmpty(DateTime.MaxValue).Min();
                    if (nextPoint < wake)
                        wake = nextPoint;
                }

                try
                {
                    await WallClock.DelayAsync(wake - DateTime.UtcNow, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>Next midnight of the location, UTC.</summary>
        private static DateTime NextMidnight(TimeSpan utcOffset) =>
            (DateTime.UtcNow + utcOffset).Date.AddDays(1) - utcOffset;

        private void ShowForecast(WeatherForecast forecast)
        {
            var selected = SelectedForecastDay?.Date;
            var days = ForecastDayViewModel.Build(forecast, DateTime.UtcNow, _icons);
            ForecastDays = days.Count > 0 ? days : null;
            SelectedForecastDay = days.FirstOrDefault(day => day.Date == selected) ?? days.FirstOrDefault();
        }

        private void Show(CurrentWeather weather)
        {
            Temperature = FormatDegrees(Math.Round(weather.Temperature));
            Humidity = FormatHumidity(weather.Humidity);
            WindSpeed = FormatWindSpeed(weather.WindSpeed);
            // The direction is where the wind blows from; the arrow shows where it blows to.
            WindArrowAngle = weather.WindDirection is { } direction ? (direction + 180) % 360 : null;
            FeelsLike = string.Format(CultureInfo.CurrentCulture, Strings.Format_FeelsLike, Math.Round(weather.FeelsLike));
            Pressure = FormatPressure(weather.Pressure);
            Sun = weather is { SunriseUtc: { } rise, SunsetUtc: { } set }
                ? string.Format(CultureInfo.CurrentCulture, "{0:t} / {1:t}", rise + weather.UtcOffset, set + weather.UtcOffset)
                : NoSun;
            WeatherIcon = _icons.Get(weather.Icon);
            _updated = DateTime.Now;
        }

        /// <summary>
        /// Before the first answer: a dash in place of every number, with the units, so the card is about
        /// as wide as with the data. No icon in the circle.
        /// </summary>
        private void ShowPlaceholders()
        {
            Temperature = FormatDegrees(NoValue);
            FeelsLike = string.Format(CultureInfo.CurrentCulture, Strings.Format_FeelsLike, NoValue);
            Humidity = FormatHumidity(NoValue);
            WindSpeed = FormatWindSpeed(NoValue);
            WindArrowAngle = null;
            Pressure = string.Format(CultureInfo.CurrentCulture, PressureFormat, NoValue);
            Sun = NoSun;
            WeatherIcon = null;
        }

        private static string FormatDegrees(object value) => string.Format(CultureInfo.CurrentCulture, "{0}°", value);

        private static string FormatHumidity(object value) => string.Format(CultureInfo.CurrentCulture, "{0}%", value);

        private static string FormatWindSpeed(object value) => string.Format(CultureInfo.CurrentCulture, Strings.Format_WindSpeed, value);

        /// <summary>
        /// The received data go to the log on one line: <see cref="DataLogMarker"/> and JSON. The configured location
        /// and the provider let the records of two instances for one place be compared.
        /// </summary>
        private void LogData(IWeatherProvider provider, CurrentWeather weather) =>
            _logger?.Log(WidgetLogLevel.Information, DataLogMarker + " " + JsonSerializer.Serialize(new
            {
                kind = "current",
                provider = provider.Name,
                config = _config.LocationKey,
                location = weather.Location,
                received = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                offset = weather.UtcOffset.TotalSeconds,
                temp = weather.Temperature,
                feels = weather.FeelsLike,
                humidity = weather.Humidity,
                wind = weather.WindSpeed,
                windFrom = weather.WindDirection,
                pressure = weather.Pressure,
                sunrise = weather.SunriseUtc?.ToString("yyyy-MM-ddTHH:mmZ", CultureInfo.InvariantCulture),
                sunset = weather.SunsetUtc?.ToString("yyyy-MM-ddTHH:mmZ", CultureInfo.InvariantCulture),
                icon = weather.Icon
            }, LogJson));

        /// <summary>Points are <c>[time of the values UTC, temperature, icon]</c>, every point the provider gave.</summary>
        private void LogData(IWeatherProvider provider, WeatherForecast forecast) =>
            _logger?.Log(WidgetLogLevel.Information, DataLogMarker + " " + JsonSerializer.Serialize(new
            {
                kind = "forecast",
                provider = provider.Name,
                config = _config.LocationKey,
                received = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                offset = forecast.UtcOffset.TotalSeconds,
                points = forecast.Points.Select(point => new object[]
                {
                    point.FromUtc.ToString("yyyy-MM-ddTHH:mmZ", CultureInfo.InvariantCulture),
                    point.Temperature,
                    point.Icon
                })
            }, LogJson));

        /// <summary>Millimetres of mercury for the Russian UI, as customary there; hectopascals otherwise.</summary>
        private static bool PressureInMmHg => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

        private static string PressureFormat => PressureInMmHg ? Strings.Format_PressureMmHg : Strings.Format_PressureHpa;

        private static string FormatPressure(double hectopascals) =>
            string.Format(CultureInfo.CurrentCulture, PressureFormat, Math.Round(PressureInMmHg ? hectopascals * 0.750062 : hectopascals));

        private void ShowStatus(bool failed)
        {
            // Time only for today, the date as well for an older update.
            var updated = _updated?.ToString(_updated.Value.Date == DateTime.Today ? "t" : "g", CultureInfo.CurrentCulture);
            Failed = failed;
            Status = (failed, updated) switch
            {
                (true, null) => Strings.Status_Failed,
                (true, _) => string.Format(CultureInfo.CurrentCulture, Strings.Status_FailedUpdated, updated),
                (false, _) => string.Format(CultureInfo.CurrentCulture, Strings.Status_Updated, updated)
            };
        }
        #endregion
    }
}
