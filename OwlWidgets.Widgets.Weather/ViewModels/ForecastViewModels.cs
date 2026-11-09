using OwlWidgets.Widgets.Weather.Providers;
using System.Globalization;
using System.Windows.Media;

namespace OwlWidgets.Widgets.Weather.ViewModels
{
    /// <summary>Forecast for one point of the 3 hour step.</summary>
    internal sealed class ForecastSlotViewModel
    {
        public ForecastSlotViewModel(string time, ImageSource? icon, double temperature)
        {
            Time = time;
            WeatherIcon = icon;
            Temperature = temperature;
        }

        public string Time { get; }

        public ImageSource? WeatherIcon { get; }

        public double Temperature { get; }
    }

    /// <summary>Tab of one day: the summary on the tab and the 3 hour points below the tabs.</summary>
    internal sealed class ForecastDayViewModel
    {
        public ForecastDayViewModel(DateTime date, bool isToday, ImageSource? icon, double? temperature, IReadOnlyList<ForecastSlotViewModel> slots)
        {
            Date = date;
            IsToday = isToday;
            WeatherIcon = icon;
            Temperature = temperature;
            Slots = slots;
        }

        public DateTime Date { get; }

        public bool IsToday { get; }

        public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        /// <summary>
        /// Short day name and day of the month, e.g. "Fri 9", in the application language like the rest of the card;
        /// some cultures write the name in lower case.
        /// </summary>
        public string Title
        {
            get
            {
                var title = Date.ToString("ddd d", CultureInfo.CurrentUICulture);
                return char.ToUpper(title[0], CultureInfo.CurrentUICulture) + title[1..];
            }
        }

        /// <summary>Summary icon; null for today.</summary>
        public ImageSource? WeatherIcon { get; }

        /// <summary>Maximum of the day; null for today.</summary>
        public double? Temperature { get; }

        public IReadOnlyList<ForecastSlotViewModel> Slots { get; }

        /// <summary>Points under the tabs: as many as fit the card at the narrowest step.</summary>
        public const int MaxSlots = 8;

        /// <summary>
        /// Splits the forecast into days of the location. Today keeps only the points after <paramref name="utcNow"/>
        /// and is left out once none remain, late in the evening; a following day is shown only if the forecast covers it to the end.
        /// A day with more than <see cref="MaxSlots"/> points shows every second or third of them: hourly data give
        /// 3 hour steps for a whole day and denser steps for the rest of today.
        /// </summary>
        public static IReadOnlyList<ForecastDayViewModel> Build(WeatherForecast forecast, DateTime utcNow, WeatherIcons icons)
        {
            var offset = forecast.UtcOffset;
            var today = (utcNow + offset).Date;
            var points = forecast.Points
                .Select(point => (Local: point.FromUtc + offset, Point: point))
                .OrderBy(point => point.Local)
                .ToList();

            var days = new List<ForecastDayViewModel>();
            var todayPoints = points.Where(point => point.Local.Date == today && point.Point.FromUtc >= utcNow).ToList();
            if (todayPoints.Count > 0)
                days.Add(new ForecastDayViewModel(today, true, null, null, ToSlots(todayPoints, icons)));

            foreach (var group in points.Where(point => point.Local.Date > today).GroupBy(point => point.Local.Date))
            {
                var dayPoints = group.ToList();
                if (dayPoints[^1].Point.ToUtc + offset < group.Key.AddDays(1))
                    break;

                // The point nearest to the early afternoon stands for the day, with its day variant of the icon.
                var noon = group.Key.AddHours(13);
                var main = dayPoints.MinBy(point => Math.Abs((point.Local + (point.Point.ToUtc - point.Point.FromUtc) / 2 - noon).Ticks)).Point;
                var mainIcon = main.Icon.Length == 3 ? main.Icon[..2] + "d" : main.Icon;
                days.Add(new ForecastDayViewModel(
                    group.Key,
                    false,
                    icons.Get(mainIcon),
                    Math.Round(dayPoints.Max(point => point.Point.Temperature)),
                    ToSlots(dayPoints, icons)));
            }

            return days;
        }

        private static List<ForecastSlotViewModel> ToSlots(List<(DateTime Local, ForecastPoint Point)> points, WeatherIcons icons)
        {
            var stride = (points.Count + MaxSlots - 1) / MaxSlots;
            return points
                .Where((_, index) => index % stride == 0)
                .Select(point => new ForecastSlotViewModel(
                    point.Local.ToString("t", CultureInfo.CurrentCulture),
                    icons.Get(point.Point.Icon),
                    Math.Round(point.Point.Temperature)))
                .ToList();
        }
    }
}
