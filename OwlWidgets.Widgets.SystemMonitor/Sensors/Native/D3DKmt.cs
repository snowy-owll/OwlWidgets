using System.Runtime.InteropServices;

namespace OwlWidgets.Widgets.SystemMonitor.Sensors.Native;

/// <summary>
/// Graphics adapters through the WDDM kernel thunks of gdi32.dll, the source of the GPU page of Task Manager.
/// Common to every vendor: the driver fills the values, so a field it does not report reads 0.
/// </summary>
internal static unsafe class D3DKmt
{
    private const int KMTQAITYPE_ADAPTERADDRESS = 6;
    private const int KMTQAITYPE_ADAPTERREGISTRYINFO = 8;
    private const int KMTQAITYPE_ADAPTERTYPE = 15;

    /// <summary>WDDM 2.4 (Windows 10 1803) and later.</summary>
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    private const int MAX_PATH = 260;

    /// <summary>Adapters open now; each handle is closed by <see cref="Close"/>.</summary>
    public static List<(uint Handle, long Luid)> Enumerate()
    {
        var request = new D3DKMT_ENUMADAPTERS2();
        if (D3DKMTEnumAdapters2(&request) != 0 || request.NumAdapters == 0)
            return [];

        // The list may grow between the two calls; the second call then fails and the next refresh tries again.
        var adapters = new D3DKMT_ADAPTERINFO[request.NumAdapters];
        fixed (D3DKMT_ADAPTERINFO* pointer = adapters)
        {
            request.pAdapters = pointer;
            if (D3DKMTEnumAdapters2(&request) != 0)
                return [];
        }

        return adapters.Take((int)request.NumAdapters)
            .Select(a => (a.hAdapter, ((long)a.AdapterLuid.HighPart << 32) | a.AdapterLuid.LowPart))
            .ToList();
    }

    public static void Close(uint handle)
    {
        var request = new D3DKMT_CLOSEADAPTER { hAdapter = handle };
        D3DKMTCloseAdapter(&request);
    }

    /// <returns><see cref="AdapterFlags"/>; null: the adapter does not report its type.</returns>
    public static AdapterFlags? ReadType(uint handle)
    {
        uint flags = 0;
        return Query(handle, KMTQAITYPE_ADAPTERTYPE, &flags, sizeof(uint)) ? (AdapterFlags)flags : null;
    }

    /// <summary>The name the driver gives the adapter, as in Device Manager.</summary>
    public static string? ReadName(uint handle)
    {
        // D3DKMT_ADAPTERREGISTRYINFO: AdapterString, BiosString, DacType, ChipType, MAX_PATH characters each.
        var buffer = stackalloc char[MAX_PATH * 4];
        if (!Query(handle, KMTQAITYPE_ADAPTERREGISTRYINFO, buffer, MAX_PATH * 4 * sizeof(char)))
            return null;
        var name = new string(buffer, 0, MAX_PATH).Split('\0')[0].Trim();
        return name.Length == 0 ? null : name;
    }

    /// <returns>PCI bus, device and function; null: the adapter is not on a PCI bus.</returns>
    public static (uint Bus, uint Device, uint Function)? ReadAddress(uint handle)
    {
        var address = stackalloc uint[3];
        if (!Query(handle, KMTQAITYPE_ADAPTERADDRESS, address, 3 * sizeof(uint)) || address[0] == uint.MaxValue)
            return null;
        return (address[0], address[1], address[2]);
    }

    /// <returns>°C; null: the driver does not report it.</returns>
    public static double? ReadTemperature(uint handle)
    {
        var data = new D3DKMT_ADAPTER_PERFDATA();
        if (!Query(handle, KMTQAITYPE_ADAPTERPERFDATA, &data, sizeof(D3DKMT_ADAPTER_PERFDATA)))
            return null;
        // Tenths of a degree.
        return data.Temperature == 0 ? null : data.Temperature / 10.0;
    }

    private static bool Query(uint handle, int type, void* data, int size)
    {
        var request = new D3DKMT_QUERYADAPTERINFO { hAdapter = handle, Type = type, pPrivateDriverData = data, PrivateDriverDataSize = (uint)size };
        return D3DKMTQueryAdapterInfo(&request) == 0;
    }

    /// <summary>The bits of <c>D3DKMT_ADAPTERTYPE</c> used here.</summary>
    [Flags]
    public enum AdapterFlags : uint
    {
        RenderSupported = 1 << 0,
        SoftwareDevice = 1 << 2,
        HybridDiscrete = 1 << 4,
        HybridIntegrated = 1 << 5,
        IndirectDisplayDevice = 1 << 6,
        ComputeOnly = 1 << 11
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO
    {
        public uint hAdapter;
        public LUID AdapterLuid;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2
    {
        public uint NumAdapters;
        public D3DKMT_ADAPTERINFO* pAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public void* pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTEnumAdapters2(D3DKMT_ENUMADAPTERS2* request);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTCloseAdapter(D3DKMT_CLOSEADAPTER* request);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTQueryAdapterInfo(D3DKMT_QUERYADAPTERINFO* request);
}
