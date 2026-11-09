using System.Text.Json.Serialization;

namespace OwlWidgets.Widgets.NetworkMonitor;

/// <summary>How the card shows a speed. Both scales are decimal: 1 Mbps is 1,000,000 bits per second, 1 MB/s is 1,000,000 bytes.</summary>
public enum SpeedUnit
{
    /// <summary>bps, Kbps, Mbps, Gbps, as the speed of a connection is sold and as Task Manager shows it.</summary>
    Bits,

    /// <summary>B/s, KB/s, MB/s, GB/s, as a browser shows a download.</summary>
    Bytes
}

public class Config
{
    /// <summary><see cref="System.Net.NetworkInformation.NetworkInterface.Id"/> of the adapter; null: the one Windows routes internet traffic through.</summary>
    public string? AdapterId { get; set; }

    /// <summary>The name of the chosen adapter when it was chosen, for the card and the settings screen while it is not connected.</summary>
    public string? AdapterName { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SpeedUnit Unit { get; set; }
}
