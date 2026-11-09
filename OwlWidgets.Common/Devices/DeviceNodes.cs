using System.Runtime.InteropServices;
using System.Text;

namespace OwlWidgets.Common.Devices;

/// <summary>Key of a PnP device property, <c>DEVPROPKEY</c>.</summary>
public readonly record struct DevicePropertyKey(Guid FormatId, uint PropertyId);

/// <summary>PnP device nodes and their properties through cfgmgr32.</summary>
public static class DeviceNodes
{
    public static readonly DevicePropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    /// <summary><c>DEVPKEY_Device_BusNumber</c>: for a PCI device, the number of its bus.</summary>
    public static readonly DevicePropertyKey BusNumber = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 23);

    /// <summary><c>DEVPKEY_Device_Address</c>: for a PCI device, its device number in the high word and function in the low one.</summary>
    public static readonly DevicePropertyKey Address = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 30);

    /// <summary><c>DEVPKEY_Device_PowerData</c>, <c>CM_POWER_DATA</c>.</summary>
    private static readonly DevicePropertyKey PowerData = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 32);

    private const uint CR_SUCCESS = 0;
    private const uint CR_BUFFER_SMALL = 0x1A;
    private const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x1;
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x100;
    private const uint DEVPROP_TYPE_BYTE = 0x3;
    private const uint DEVPROP_TYPE_UINT32 = 0x7;
    private const uint DEVPROP_TYPE_BOOLEAN = 0x11;

    /// <summary>Instance ids of the present devices of an enumerator, for example "BTHENUM".</summary>
    /// <returns>An empty list: the enumerator has no devices or does not exist.</returns>
    public static IReadOnlyList<string> GetPresentIds(string enumerator)
    {
        const uint flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;
        // The list may grow between the two calls.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags) != CR_SUCCESS || length == 0)
                return [];
            var buffer = new byte[length * 2];
            var result = CM_Get_Device_ID_ListW(enumerator, buffer, length, flags);
            if (result == CR_BUFFER_SMALL)
                continue;
            if (result != CR_SUCCESS)
                return [];
            return Encoding.Unicode.GetString(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        return [];
    }

    /// <returns>null: no present device has this instance id.</returns>
    public static uint? Find(string instanceId) =>
        CM_Locate_DevNodeW(out var node, instanceId, 0) == CR_SUCCESS ? node : null;

    public static string? ReadString(uint node, DevicePropertyKey key)
    {
        var buffer = Read(node, key, out _);
        return buffer == null ? null : Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    public static byte? ReadByte(uint node, DevicePropertyKey key)
    {
        var buffer = Read(node, key, out var type);
        return buffer is { Length: 1 } && type == DEVPROP_TYPE_BYTE ? buffer[0] : null;
    }

    public static bool? ReadBoolean(uint node, DevicePropertyKey key)
    {
        var buffer = Read(node, key, out var type);
        return buffer is { Length: 1 } && type == DEVPROP_TYPE_BOOLEAN ? buffer[0] != 0 : null;
    }

    public static uint? ReadUInt32(uint node, DevicePropertyKey key)
    {
        var buffer = Read(node, key, out var type);
        return buffer is { Length: 4 } && type == DEVPROP_TYPE_UINT32 ? BitConverter.ToUInt32(buffer) : null;
    }

    /// <summary>
    /// The power state the device was put in last, from the PnP manager: reading it does not touch the device,
    /// so it does not wake one that sleeps.
    /// </summary>
    /// <returns>0 for D0 (working) to 3 for D3 (off); null: the device does not report it.</returns>
    public static int? ReadPowerState(uint node)
    {
        // CM_POWER_DATA: PD_Size, then PD_MostRecentPowerState, DEVICE_POWER_STATE, where PowerDeviceD0 is 1.
        var buffer = Read(node, PowerData, out _);
        if (buffer is not { Length: >= 8 })
            return null;
        var state = BitConverter.ToInt32(buffer, 4);
        return state is >= 1 and <= 4 ? state - 1 : null;
    }

    private static byte[]? Read(uint node, DevicePropertyKey key, out uint type)
    {
        var native = new DEVPROPKEY { fmtid = key.FormatId, pid = key.PropertyId };
        uint size = 0;
        CM_Get_DevNode_PropertyW(node, ref native, out type, null, ref size, 0);
        if (size == 0)
            return null;
        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(node, ref native, out type, buffer, ref size, 0) == CR_SUCCESS ? buffer : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string? filter, byte[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, byte[]? buffer, ref uint size, uint flags);
}
