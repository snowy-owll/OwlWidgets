using System.Xml.Serialization;

namespace OwlWidgets.Widgets.Weather
{
    /// <summary>OpenWeatherMap 5 day / 3 hour forecast, XML mode. Times are UTC.</summary>
    [XmlRoot("weatherdata")]
    public class ForecastData
    {
        [XmlElement("location")]
        public ForecastLocation Location { get; set; } = new();

        [XmlArray("forecast")]
        [XmlArrayItem("time")]
        public List<ForecastTime> Times { get; set; } = [];
    }

    public class ForecastLocation
    {
        [XmlElement("name")]
        public string? Name { get; set; }

        /// <summary>Offset of the location from UTC, in seconds.</summary>
        [XmlElement("timezone")]
        public int Timezone { get; set; }
    }

    public class ForecastTime
    {
        [XmlAttribute("from")]
        public DateTime From { get; set; }

        [XmlAttribute("to")]
        public DateTime To { get; set; }

        [XmlElement("symbol")]
        public ForecastSymbol Symbol { get; set; } = new();

        [XmlElement("temperature")]
        public Temperature Temperature { get; set; } = new();
    }

    public class ForecastSymbol
    {
        [XmlAttribute("number")]
        public int Number { get; set; }

        [XmlAttribute("name")]
        public string? Name { get; set; }

        /// <summary>Icon code, e.g. <c>01d</c>.</summary>
        [XmlAttribute("var")]
        public string? Icon { get; set; }
    }
}
