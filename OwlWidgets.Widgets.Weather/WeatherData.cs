using System.Xml.Serialization;

namespace OwlWidgets.Widgets.Weather
{
    [XmlRoot("current")]
    public class WeatherData
    {
        [XmlElement("city")]
        public City City { get; set; }

        [XmlElement("temperature")]
        public Temperature Temperature { get; set; }

        [XmlElement("feels_like")]
        public FeelsLike FeelsLike { get; set; }

        [XmlElement("humidity")]
        public Humidity Humidity { get; set; }

        [XmlElement("pressure")]
        public Pressure Pressure { get; set; }

        [XmlElement("wind")]
        public Wind Wind { get; set; }

        [XmlElement("clouds")]
        public Clouds Clouds { get; set; }

        [XmlElement("visibility")]
        public Visibility Visibility { get; set; }

        [XmlElement("precipitation")]
        public Precipitation Precipitation { get; set; }

        [XmlElement("weather")]
        public Weather Weather { get; set; }

        [XmlElement("lastupdate")]
        public LastUpdate LastUpdate { get; set; }
    }

    public class City
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement("coord")]
        public Coord Coord { get; set; }

        [XmlElement("country")]
        public string Country { get; set; }

        [XmlElement("timezone")]
        public int Timezone { get; set; }

        [XmlElement("sun")]
        public Sun Sun { get; set; }
    }

    public class Coord
    {
        [XmlAttribute("lon")]
        public double Lon { get; set; }

        [XmlAttribute("lat")]
        public double Lat { get; set; }
    }

    public class Sun
    {
        [XmlAttribute("rise")]
        public DateTime Rise { get; set; }

        [XmlAttribute("set")]
        public DateTime Set { get; set; }
    }

    public class Temperature
    {
        [XmlAttribute("value")]
        public double Value { get; set; }

        [XmlAttribute("min")]
        public double Min { get; set; }

        [XmlAttribute("max")]
        public double Max { get; set; }

        [XmlAttribute("unit")]
        public string Unit { get; set; }
    }

    public class FeelsLike
    {
        [XmlAttribute("value")]
        public double Value { get; set; }

        [XmlAttribute("unit")]
        public string Unit { get; set; }
    }

    public class Humidity
    {
        [XmlAttribute("value")]
        public int Value { get; set; }

        [XmlAttribute("unit")]
        public string Unit { get; set; }
    }

    public class Pressure
    {
        [XmlAttribute("value")]
        public int Value { get; set; }

        [XmlAttribute("unit")]
        public string Unit { get; set; }
    }

    public class Wind
    {
        [XmlElement("speed")]
        public Speed Speed { get; set; }

        [XmlElement("gusts")]
        public Gusts Gusts { get; set; }

        [XmlElement("direction")]
        public Direction Direction { get; set; }
    }

    public class Speed
    {
        [XmlAttribute("value")]
        public double Value { get; set; }

        [XmlAttribute("unit")]
        public string Unit { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }
    }

    public class Gusts
    {
        [XmlAttribute("value")]
        public double Value { get; set; }
    }

    public class Direction
    {
        [XmlAttribute("value")]
        public int Value { get; set; }

        [XmlAttribute("code")]
        public string Code { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }
    }

    public class Clouds
    {
        [XmlAttribute("value")]
        public int Value { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }
    }

    public class Visibility
    {
        [XmlAttribute("value")]
        public int Value { get; set; }
    }

    public class Precipitation
    {
        [XmlAttribute("mode")]
        public string Mode { get; set; }
    }

    public class Weather
    {
        [XmlAttribute("number")]
        public int Number { get; set; }

        [XmlAttribute("value")]
        public string Value { get; set; }

        [XmlAttribute("icon")]
        public string Icon { get; set; }
    }

    public class LastUpdate
    {
        [XmlAttribute("value")]
        public DateTime Value { get; set; }
    }
}
