using OwlWidgets.Common.Devices;

namespace OwlWidgets.Widgets.SystemMonitor.Sensors;

/// <summary>The device node of a video card by its PCI address, to read its power state without asking the driver.</summary>
internal static class PciNodes
{
    /// <summary>Present PCI devices by bus number and address (device &lt;&lt; 16 | function).</summary>
    public static Dictionary<(uint Bus, uint Address), uint> Read()
    {
        var nodes = new Dictionary<(uint, uint), uint>();
        foreach (var id in DeviceNodes.GetPresentIds("PCI"))
        {
            if (DeviceNodes.Find(id) is { } node
                && DeviceNodes.ReadUInt32(node, DeviceNodes.BusNumber) is { } bus
                && DeviceNodes.ReadUInt32(node, DeviceNodes.Address) is { } address)
                nodes.TryAdd((bus, address), node);
        }

        return nodes;
    }

    public static uint Address(uint device, uint function) => (device << 16) | function;

    /// <summary>A card at rest: a discrete card of a laptop is powered off while nothing draws on it.</summary>
    public static bool IsAsleep(uint? node) => node is { } n && DeviceNodes.ReadPowerState(n) is > 0;
}
