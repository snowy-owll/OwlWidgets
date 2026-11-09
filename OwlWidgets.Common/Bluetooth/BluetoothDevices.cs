using OwlWidgets.Common.Devices;

namespace OwlWidgets.Common.Bluetooth;

/// <summary>A paired Bluetooth device: the PnP nodes of one Bluetooth address.</summary>
/// <param name="Address">12 hex digits, upper case, without separators.</param>
/// <param name="IsConnected">null: Windows does not tell.</param>
/// <param name="BatteryLevel">Percent, the last one Windows got from the device; null: the device does not report it.</param>
public sealed record BluetoothDevice(string Address, string Name, bool? IsConnected, int? BatteryLevel);

/// <summary>
/// Paired Bluetooth devices, classic and Low Energy, as PnP shows them. A paired device stays present
/// while it is disconnected and keeps its last battery level.
/// </summary>
/// <remarks>Blocking calls: call from a thread pool thread, not from the UI thread.</remarks>
public static class BluetoothDevices
{
    /// <summary><c>DEVPKEY_Bluetooth_DeviceAddress</c>: on every node of a device, a string of 12 hex digits.</summary>
    public static readonly DevicePropertyKey AddressKey = new(new Guid("2bd67d8b-8beb-48d5-87e0-6cda3428040a"), 1);

    /// <summary>Battery level in percent, a byte: on the device node of a Low Energy device and on the Hands-Free node of a classic one.</summary>
    public static readonly DevicePropertyKey BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);

    /// <summary>Whether the device is connected, a boolean: on the device node.</summary>
    public static readonly DevicePropertyKey ConnectedKey = new(new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), 15);

    private static readonly string[] Enumerators = ["BTHENUM", "BTHLE"];

    public static IReadOnlyList<BluetoothDevice> GetAll()
    {
        var nodes = new List<Node>();
        foreach (var enumerator in Enumerators)
        {
            foreach (var id in DeviceNodes.GetPresentIds(enumerator))
            {
                if (DeviceNodes.Find(id) is not { } node)
                    continue;
                var address = BluetoothAddress.Normalize(DeviceNodes.ReadString(node, AddressKey));
                if (address.Length == 0)
                    continue;
                // "BTHENUM\DEV_{address}" and "BTHLE\DEV_{address}" are the device itself, the other nodes are its services.
                var isDevice = id.StartsWith(enumerator + @"\DEV_", StringComparison.OrdinalIgnoreCase);
                nodes.Add(new Node(
                    address,
                    isDevice,
                    DeviceNodes.ReadString(node, DeviceNodes.FriendlyName),
                    isDevice ? DeviceNodes.ReadBoolean(node, ConnectedKey) : null,
                    DeviceNodes.ReadByte(node, BatteryKey)));
            }
        }

        return nodes
            .GroupBy(node => node.Address)
            .Select(Combine)
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A service node is named after its service ("... Hands-Free AG"), so the name comes from the device node.
    /// A dual mode device has two device nodes: it is connected when either of them is.
    /// </summary>
    private static BluetoothDevice Combine(IGrouping<string, Node> group)
    {
        var devices = group.Where(node => node.IsDevice).ToList();
        var name = devices.FirstOrDefault(node => !string.IsNullOrEmpty(node.Name))?.Name
            ?? group.FirstOrDefault(node => !string.IsNullOrEmpty(node.Name))?.Name
            ?? BluetoothAddress.Format(group.Key);
        bool? connected = devices.Any(node => node.Connected == true) ? true
            : devices.Any(node => node.Connected == false) ? false
            : null;
        return new BluetoothDevice(group.Key, name, connected, group.FirstOrDefault(node => node.Battery != null)?.Battery);
    }

    private sealed record Node(string Address, bool IsDevice, string? Name, bool? Connected, byte? Battery);
}
