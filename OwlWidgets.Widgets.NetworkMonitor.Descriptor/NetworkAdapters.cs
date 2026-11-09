using System.Net.NetworkInformation;

namespace OwlWidgets.Widgets.NetworkMonitor;

/// <summary>The adapters a card can watch: the settings screen lists them, the runtime looks the chosen one up among them.</summary>
public static class NetworkAdapters
{
    /// <summary>
    /// Adapters of Windows that carry traffic of their own: not the loopback and not the tunnels of IPv6 over IPv4
    /// (Teredo, ISATAP, 6to4), whose bytes are already counted on the adapter under them.
    /// </summary>
    public static IReadOnlyList<NetworkInterface> List()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>"Wi-Fi — Intel(R) Wi-Fi 6E AX211 160MHz": the name of the connection, then the device.</summary>
    public static string DisplayName(NetworkInterface adapter) =>
        string.IsNullOrEmpty(adapter.Description) || adapter.Description == adapter.Name
            ? adapter.Name
            : $"{adapter.Name} — {adapter.Description}";
}
