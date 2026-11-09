using System.Globalization;
using OwlWidgets.Widgets.NetworkMonitor.Properties;

namespace OwlWidgets.Widgets.NetworkMonitor.Traffic;

/// <summary>A speed in bytes per second as the card shows it: in bits or bytes, decimal prefixes, numbers by the regional settings.</summary>
internal static class SpeedFormat
{
    /// <summary>
    /// The lowest top of a graph, in the units shown: 100 Kbps or 10 KB/s. Below it the background traffic of Windows,
    /// a few kilobits, would fill the graph as if the connection were busy.
    /// </summary>
    private const double MinScaleBits = 100_000, MinScaleBytes = 10_000;

    /// <summary>"12.4 Mbps", "850 KB/s", "0 bps".</summary>
    public static string Speed(double bytesPerSecond, SpeedUnit unit)
    {
        var (value, suffix) = Scale(Shown(bytesPerSecond, unit), unit);
        var format = value < 10 ? "0.##" : value < 100 ? "0.#" : "0";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + suffix;
    }

    /// <summary>
    /// The top of the graph for its highest value: the next 1, 2 or 5 times a power of ten, in bytes per second,
    /// so the grid line in the middle of the graph is a round number too.
    /// </summary>
    public static double Top(double peakBytesPerSecond, SpeedUnit unit)
    {
        var factor = unit == SpeedUnit.Bits ? 8 : 1;
        var shown = Math.Max(peakBytesPerSecond * factor, unit == SpeedUnit.Bits ? MinScaleBits : MinScaleBytes);
        var power = Math.Pow(10, Math.Floor(Math.Log10(shown)));
        var step = shown / power;
        var nice = step <= 1 ? 1 : step <= 2 ? 2 : step <= 5 ? 5 : 10;
        return nice * power / factor;
    }

    private static double Shown(double bytesPerSecond, SpeedUnit unit) => unit == SpeedUnit.Bits ? bytesPerSecond * 8 : bytesPerSecond;

    private static (double Value, string Suffix) Scale(double value, SpeedUnit unit)
    {
        string[] suffixes = unit == SpeedUnit.Bits
            ? [Strings.Unit_Bps, Strings.Unit_Kbps, Strings.Unit_Mbps, Strings.Unit_Gbps]
            : [Strings.Unit_BytesPerSecond, Strings.Unit_KBPerSecond, Strings.Unit_MBPerSecond, Strings.Unit_GBPerSecond];
        var i = 0;
        // 999.6 would round to "1000 Kbps": move to the next prefix before that.
        while (i < suffixes.Length - 1 && value >= 999.5)
        {
            value /= 1000;
            i++;
        }

        return (value, suffixes[i]);
    }
}
