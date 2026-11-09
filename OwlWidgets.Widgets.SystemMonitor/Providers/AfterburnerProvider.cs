using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;
using OwlWidgets.Widgets.SystemMonitor.Sensors;

namespace OwlWidgets.Widgets.SystemMonitor.Providers;

/// <summary>
/// The shared memory of MSI Afterburner, there while it runs; it holds the graphs turned on on its Monitoring tab.
/// The layout is that of MAHMSharedMemory.h of its SDK, version 2: the sources are found by their id, not by
/// the name, which follows the language of Afterburner; the sections are found by the sizes in the header.
/// </summary>
internal sealed partial class AfterburnerProvider : ISensorProvider
{
    /// <summary>In the session namespace: Afterburner runs in the session of the user.</summary>
    private const string SharedMemoryName = "MAHMSharedMemory";

    /// <summary>"MAHM" while Afterburner shares its graphs; 0xDEAD after it stops.</summary>
    private const uint ActiveSignature = 0x4D41484D;

    /// <summary>dwGpu and dwSrcId of an entry and the section of video cards appeared in version 2.0.</summary>
    private const uint FirstVersion = 0x00020000;

    /// <summary>Afterburner polls every second by default; a header older than this means it is hung.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private const int StringLength = 260;
    /// <summary>The value of a source that has nothing to report (FLT_MAX).</summary>
    private const float NoValue = float.MaxValue;

    // MONITORING_SOURCE_ID_* of the sources used.
    private const uint GpuTemperature = 0x00;
    private const uint GpuUsage = 0x30;
    private const uint CpuTemperature = 0x80;
    private const uint CpuUsage = 0x90;

    /// <summary>dwGpu of the total CPU load; the load of each thread has its number there.</summary>
    private const uint NoIndex = uint.MaxValue;

    /// <summary>How long the PCI device nodes are kept before they are listed again.</summary>
    private static readonly TimeSpan NodeRefresh = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A sleeping card stays asleep this long after the last sign of sleep. Afterburner wakes a sleeping card
    /// every few seconds to read it (4 s on an RTX 4070 Laptop), and a card that just woke up reports its real
    /// temperature for a reading or two before it sleeps again; without this the card would flip between
    /// asleep and a temperature every few seconds.
    /// </summary>
    private static readonly TimeSpan AsleepHold = TimeSpan.FromSeconds(6);

    private Dictionary<(uint Bus, uint Address), uint> _pciNodes = [];
    private DateTime _pciNodesRead = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _lastAsleep = [];

    public string Name => "MSI Afterburner";

    public SensorReading? Read()
    {
        MemoryMappedFile memory;
        try
        {
            memory = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        using (memory)
        using (var view = memory.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
        {
            // MAHM_SHARED_MEMORY_HEADER: signature, version, header size, entry count, entry size,
            // poll time (32-bit time_t), then video card count and size.
            if (view.ReadUInt32(0) != ActiveSignature)
                return null;
            var version = view.ReadUInt32(4);
            if (version < FirstVersion)
                throw new InvalidDataException($"Shared memory version {version >> 16}.{version & 0xFFFF} is older than 2.0.");
            var pollTime = DateTimeOffset.FromUnixTimeSeconds(view.ReadInt32(20));
            if (DateTimeOffset.UtcNow - pollTime > StaleAfter)
                return null;

            var headerSize = view.ReadUInt32(8);
            var entryCount = view.ReadUInt32(12);
            var entrySize = view.ReadUInt32(16);
            var gpus = ReadGpus(view, headerSize + entryCount * entrySize, view.ReadUInt32(28), view.ReadUInt32(24));
            return ReadEntries(view, gpus, headerSize, entrySize, entryCount);
        }
    }

    public void Dispose()
    {
    }

    /// <returns>Key, name and PCI device node of each video card by its number.</returns>
    private Gpu[] ReadGpus(MemoryMappedViewAccessor view, long offset, uint size, uint count)
    {
        // MAHM_SHARED_MEMORY_GPU_ENTRY: id ("VEN_10DE&DEV_2860&…&BUS_1&DEV_0&FN_0"), family, device, driver, BIOS, memory.
        var section = new byte[size * count];
        view.ReadArray(offset, section, 0, section.Length);
        var gpus = new Gpu[count];
        for (var i = 0; i < count; i++)
        {
            var gpu = section.AsSpan((int)(i * size), (int)size);
            var id = Text(gpu[..StringLength]);
            var device = Text(gpu.Slice(2 * StringLength, StringLength));
            gpus[i] = new Gpu("Afterburner " + id, device.Length > 0 ? device : $"GPU{i + 1}", FindNode(id));
        }

        return gpus;
    }

    /// <summary>The device node by the PCI address at the end of the id; the numbers there are decimal.</summary>
    private uint? FindNode(string id)
    {
        var match = PciAddress().Match(id);
        if (!match.Success)
            return null;

        if (DateTime.UtcNow - _pciNodesRead > NodeRefresh)
        {
            _pciNodes = PciNodes.Read();
            _pciNodesRead = DateTime.UtcNow;
        }

        var bus = uint.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        var address = PciNodes.Address(
            uint.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture),
            uint.Parse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture));
        return _pciNodes.TryGetValue((bus, address), out var node) ? node : null;
    }

    private SensorReading ReadEntries(MemoryMappedViewAccessor view, Gpu[] gpus, long offset, uint size, uint count)
    {
        // MAHM_SHARED_MEMORY_ENTRY: name, units, localized name and units, format, then data, min, max (float),
        // flags, then since version 2 the video card or thread number and the source id.
        var section = new byte[size * count];
        view.ReadArray(offset, section, 0, section.Length);
        double? cpuTemperature = null;
        double? cpuLoad = null;
        var gpuLoads = new List<GpuLoadSample>();
        var gpuTemperatures = new List<TemperatureSample>();
        for (var i = 0; i < count; i++)
        {
            var entry = section.AsSpan((int)(i * size), (int)size);
            var data = BinaryPrimitives.ReadSingleLittleEndian(entry[(5 * StringLength)..]);
            var index = BinaryPrimitives.ReadUInt32LittleEndian(entry[(5 * StringLength + 16)..]);
            var source = BinaryPrimitives.ReadUInt32LittleEndian(entry[(5 * StringLength + 20)..]);
            if (source == GpuTemperature && index < gpus.Length)
            {
                var gpu = gpus[index];
                gpuTemperatures.Add(IsAsleep(gpu, data)
                    ? new TemperatureSample(gpu.Key, gpu.Name, null, Asleep: true)
                    : new TemperatureSample(gpu.Key, gpu.Name, data != NoValue && float.IsFinite(data) ? data : null));
                continue;
            }

            if (data == NoValue || !float.IsFinite(data))
                continue;
            switch (source)
            {
                case CpuTemperature:
                    cpuTemperature ??= data;
                    break;
                case CpuUsage when index == NoIndex:
                    cpuLoad = Math.Clamp(data, 0, 100);
                    break;
                case GpuUsage when index < gpus.Length:
                    gpuLoads.Add(new GpuLoadSample(gpus[index].Key, gpus[index].Name, Math.Clamp(data, 0, 100)));
                    break;
            }
        }

        return new SensorReading(cpuTemperature, cpuLoad, gpuLoads, gpuTemperatures);
    }

    /// <summary>
    /// Afterburner cannot read a sleeping card and writes 0 °C for it. The power state of the PnP node is not
    /// enough: the driver of an RTX 4070 Laptop powers the card down by itself (GC6) and the node mostly stays
    /// in D0, in D3 only for a moment after Afterburner has woken the card. So 0 °C is sleep as well.
    /// </summary>
    private bool IsAsleep(Gpu gpu, float data)
    {
        var now = DateTime.UtcNow;
        if (data <= 0 || PciNodes.IsAsleep(gpu.Node))
        {
            _lastAsleep[gpu.Key] = now;
            return true;
        }

        return _lastAsleep.TryGetValue(gpu.Key, out var last) && now - last < AsleepHold;
    }

    /// <summary>ANSI; the names used here are Latin.</summary>
    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }

    [GeneratedRegex(@"&BUS_(\d+)&DEV_(\d+)&FN_(\d+)$")]
    private static partial Regex PciAddress();

    /// <param name="Node">The PCI device node; null: not found, the power state is not checked.</param>
    private sealed record Gpu(string Key, string Name, uint? Node);
}
