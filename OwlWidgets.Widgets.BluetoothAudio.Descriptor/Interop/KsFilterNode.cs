using OwlWidgets.Common.Bluetooth;
using OwlWidgets.Common.Devices;

namespace OwlWidgets.Widgets.BluetoothAudio.Interop;

/// <summary>The PnP device behind a KS filter of an audio driver.</summary>
internal static class KsFilterNode
{
    /// <summary>
    /// The PnP device behind a KS filter id as Core Audio gives it: "{2}.\\?\bthenum#{service}_...#{interface class}\src".
    /// The interface path without the reference string and the class maps to the instance id "BTHENUM\{service}_...\...".
    /// Both the A2DP (BTHENUM) and the Hands-Free (BTHHFENUM) nodes carry the Bluetooth address.
    /// </summary>
    /// <returns>null: not a Bluetooth device, or the node is not found.</returns>
    public static (string Address, string? Name, string InstanceId)? FindBluetooth(string filterId)
    {
        var path = filterId.StartsWith("{2}.", StringComparison.Ordinal) ? filterId[4..] : filterId;
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return null;
        path = path[4..];
        var slash = path.LastIndexOf('\\');
        if (slash > 0)
            path = path[..slash];
        var hash = path.LastIndexOf('#');
        if (hash > 0)
            path = path[..hash];
        var instanceId = path.Replace('#', '\\');

        if (DeviceNodes.Find(instanceId) is not { } node)
            return null;
        var address = DeviceNodes.ReadString(node, BluetoothDevices.AddressKey);
        if (string.IsNullOrEmpty(address))
            return null;
        return (address.ToUpperInvariant(), DeviceNodes.ReadString(node, DeviceNodes.FriendlyName), instanceId);
    }
}
