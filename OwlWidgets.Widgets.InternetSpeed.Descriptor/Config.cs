using System.Text.Json.Serialization;
using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.InternetSpeed;

/// <summary>Servers that measure the connection of an instance.</summary>
public enum SpeedTestProviderKind
{
    /// <summary>speed.cloudflare.com: the nearest Cloudflare data center over HTTPS, several connections at once.</summary>
    Cloudflare,

    /// <summary>Measurement Lab, NDT7 over a WebSocket: one connection, about 10 seconds per direction.</summary>
    MLab
}

public class Config
{
    /// <summary>Seconds per direction.</summary>
    public const int DefaultDuration = 10;

    public const int MinDuration = 5;

    public const int MaxDuration = 30;

    public const int DefaultStreams = 6;

    public const int MinStreams = 1;

    public const int MaxStreams = 16;

    /// <summary>Minutes; a shorter interval than this, other than 0, is not accepted.</summary>
    public const int MinAutoInterval = 30;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SpeedTestProviderKind Provider { get; set; }

    /// <summary>Seconds of the download and of the upload each. Cloudflare only: NDT7 fixes it at 10 seconds.</summary>
    public int Duration { get; set; } = DefaultDuration;

    /// <summary>Connections at once in each direction. Cloudflare only: NDT7 measures over one.</summary>
    public int Streams { get; set; } = DefaultStreams;

    /// <summary>Minutes between automatic tests; 0: only by the button on the card.</summary>
    public int AutoInterval { get; set; }

    /// <summary>
    /// Corner of the buttons that hide the card and move it; null: the anchor corner of the instance.
    /// A hidden card shrinks towards it inside a window of the same size, so the buttons stay in place.
    /// </summary>
    public CardCorner? ButtonCorner { get; set; }
}
