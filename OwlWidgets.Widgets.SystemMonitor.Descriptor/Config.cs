using System.Text.Json.Serialization;
using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>
/// The sensor program of the card: what it reports is taken instead of what Windows reports, the rest comes from Windows.
/// </summary>
public enum SensorProviderKind
{
    /// <summary>The first running program of those the widget knows.</summary>
    Auto,

    /// <summary>No program: what Windows reports by itself, without the CPU temperature.</summary>
    Windows,

    /// <summary>The shared memory of HWiNFO.</summary>
    HWiNFO,

    /// <summary>The shared memory of MSI Afterburner.</summary>
    Afterburner,

    /// <summary>The web server of LibreHardwareMonitor, see <see cref="Config.LibreHardwareMonitor"/>.</summary>
    LibreHardwareMonitor
}

/// <summary>How the card looks; the card takes another one only when it starts again.</summary>
public enum CardView
{
    /// <summary>Tiles, rings and bars under a coloured header.</summary>
    Classic,

    /// <summary>The terminal styles: a grid of characters in a monospaced font.</summary>
    Report,
    Htop,
    Prompt,
    Btop,
    Board,

    /// <summary>The widgets of Dear ImGui in its dark style, as in ImHex.</summary>
    ImHex
}

/// <summary>A section turned off is not read at all, not only hidden.</summary>
public class Config
{
    /// <summary>The opacity of the card background of <c>CardStyle</c>, black at 50 %.</summary>
    public const int DefaultBackgroundOpacity = 50;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CardView Style { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SensorProviderKind Provider { get; set; }

    /// <summary>Where the web server of LibreHardwareMonitor is, used when it is the provider or the provider is automatic.</summary>
    public LibreHardwareMonitorServer LibreHardwareMonitor { get; set; } = new();

    public bool ShowDrives { get; set; } = true;

    /// <summary>CPU, memory and video card load.</summary>
    public bool ShowSystem { get; set; } = true;

    public bool ShowTemperatures { get; set; } = true;

    public bool ShowNetworkDrives { get; set; } = true;

    /// <summary>USB, FireWire, memory cards and optical discs, see <see cref="DriveKind.External"/>.</summary>
    public bool ShowExternalDrives { get; set; } = true;

    /// <summary>The drives turned off one by one; a drive not listed is shown, so a new one appears on the card at once.</summary>
    public List<HiddenDrive> HiddenDrives { get; set; } = [];

    /// <summary>
    /// Corner of the buttons that move the card, keep it above all windows and collapse it; null: the anchor corner
    /// of the instance. A collapsed card stands in this corner inside a window of the same size, so the buttons stay in place.
    /// </summary>
    public CardCorner? ButtonCorner { get; set; }

    /// <summary>The values of the collapsed card; a value of a section turned off is not shown, it is not read.</summary>
    public CollapsedValues Collapsed { get; set; } = new();

    /// <summary>The opacity of the card background in percent, 0…100, in every style; the content stays opaque.</summary>
    public int BackgroundOpacity { get; set; } = DefaultBackgroundOpacity;
}

/// <summary>What the collapsed card shows, in this order.</summary>
public class CollapsedValues
{
    public bool CpuLoad { get; set; } = true;

    public bool Memory { get; set; } = true;

    /// <summary>The load of every video card.</summary>
    public bool GpuLoad { get; set; }

    public bool CpuTemperature { get; set; } = true;

    /// <summary>The temperature of every video card that reports one or sleeps.</summary>
    public bool GpuTemperature { get; set; } = true;

    /// <summary>The space used on every drive shown.</summary>
    public bool Drives { get; set; }
}

public class HiddenDrive
{
    /// <summary><see cref="DriveKinds.Id"/>: the volume, whatever letter it gets, or the root of a network drive.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>How the settings named it when it was turned off, shown while it is not connected.</summary>
    public string Name { get; set; } = string.Empty;
}
