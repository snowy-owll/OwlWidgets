using System.Buffers.Binary;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;
using OwlWidgets.Widgets.SystemMonitor.Sensors;

namespace OwlWidgets.Widgets.SystemMonitor.Providers;

/// <summary>
/// The shared memory of HWiNFO, turned on by Shared Memory Support in its settings. In the free version it
/// works for 12 hours after it is turned on. The layout is that of the HWiNFO SDK; the sections are found
/// by the offsets and sizes in the header, so fields added to the end of an element by a later version are skipped.
/// </summary>
internal sealed partial class HWiNFOProvider : ISensorProvider
{
    private const string SharedMemoryName = @"Global\HWiNFO_SENS_SM2";

    /// <summary>"HWiS" while HWiNFO shares its sensors; "DEAD" after it stops.</summary>
    private const uint ActiveSignature = 0x53695748;

    /// <summary>HWiNFO polls every 2 s by default; a header older than this means it is paused or hung.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private const int StringLength = 128;
    private const int SensorTemperature = 1;
    private const int SensorUsage = 7;

    /// <summary>
    /// The CPU temperature, in order of preference: AMD (Tctl/Tdie, the one the CPU regulates by),
    /// Intel (Package), then the labels of older CPUs.
    /// </summary>
    private static readonly string[] CpuTemperatureLabels =
        ["CPU (Tctl/Tdie)", "CPU Package", "CPU (Tctl)", "CPU (Tdie)", "CPU Die (average)", "CPU", "Core Max"];

    /// <summary>The CPU load: Utility counts the time at the real frequency, as Task Manager and the Windows layer do.</summary>
    private static readonly string[] CpuLoadLabels = ["Total CPU Utility", "Total CPU Usage"];

    /// <summary>The video card load: NVIDIA, AMD, then the busiest engine as Windows counts it (Intel).</summary>
    private static readonly string[] GpuLoadLabels = ["GPU Core Load", "GPU Utilization", "GPU D3D Usage"];

    public string Name => "HWiNFO";

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
            // HWiNFO_SHARED_MEM, packed: signature, version, revision, poll time (__time64_t), then offset, size
            // and count of the sensor section and of the reading section.
            if (view.ReadUInt32(0) != ActiveSignature)
                return null;
            var pollTime = DateTimeOffset.FromUnixTimeSeconds(view.ReadInt64(12));
            if (DateTimeOffset.UtcNow - pollTime > StaleAfter)
                return null;

            var sensors = ReadSensorNames(view, view.ReadUInt32(20), view.ReadUInt32(24), view.ReadUInt32(28));
            return ReadElements(view, sensors, view.ReadUInt32(32), view.ReadUInt32(36), view.ReadUInt32(40));
        }
    }

    public void Dispose()
    {
    }

    private static string[] ReadSensorNames(MemoryMappedViewAccessor view, uint offset, uint size, uint count)
    {
        // HWiNFO_SENSOR: id, instance, original name, name shown to the user (ANSI), then since version 2
        // the name shown in UTF-8. The original name is empty in some builds; the shown one is always there.
        var section = new byte[size * count];
        view.ReadArray(offset, section, 0, section.Length);
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            var sensor = section.AsSpan((int)(i * size), (int)size);
            var name = size >= 264 + StringLength ? Text(sensor.Slice(264, StringLength)) : string.Empty;
            names[i] = name.Length > 0 ? name : Text(sensor.Slice(136, StringLength));
        }

        return names;
    }

    private static SensorReading ReadElements(MemoryMappedViewAccessor view, string[] sensors, uint offset, uint size, uint count)
    {
        // HWiNFO_ELEMENT: type, sensor index, id, original label, label shown, unit, then value, min, max, avg (double).
        var section = new byte[size * count];
        view.ReadArray(offset, section, 0, section.Length);
        var cpuTemperatures = new Dictionary<string, double>();
        var cpuLoads = new Dictionary<string, double>();
        var gpuTemperatures = new List<TemperatureSample>();
        // By sensor, in the order HWiNFO lists the cards.
        var gpuLoads = new Dictionary<string, Dictionary<string, double>>();
        for (var i = 0; i < count; i++)
        {
            var element = section.AsSpan((int)(i * size), (int)size);
            var type = BinaryPrimitives.ReadInt32LittleEndian(element);
            if (type is not (SensorTemperature or SensorUsage))
                continue;
            var sensorIndex = BinaryPrimitives.ReadInt32LittleEndian(element[4..]);
            if (sensorIndex < 0 || sensorIndex >= sensors.Length)
                continue;
            var sensor = sensors[sensorIndex];
            var label = Text(element.Slice(12, StringLength));
            var value = BinaryPrimitives.ReadDoubleLittleEndian(element[284..]);

            // The first CPU: its readings are spread over several sensors ("CPU [#0]: …: Enhanced", "…: DTS").
            if (sensor.StartsWith("CPU [#0]", StringComparison.Ordinal))
                (type == SensorTemperature ? cpuTemperatures : cpuLoads).TryAdd(label, value);
            else if (GpuSensor().Match(sensor) is { Success: true } gpu)
            {
                if (type == SensorTemperature && label == "GPU Temperature")
                    gpuTemperatures.Add(new TemperatureSample(Key(sensor), gpu.Groups[1].Value.Trim(), value));
                else if (type == SensorUsage)
                {
                    if (!gpuLoads.TryGetValue(sensor, out var loads))
                        gpuLoads[sensor] = loads = [];
                    loads.TryAdd(label, value);
                }
            }
        }

        var gpus = new List<GpuLoadSample>();
        foreach (var (sensor, loads) in gpuLoads)
        {
            if (First(loads, GpuLoadLabels) is { } load)
                gpus.Add(new GpuLoadSample(Key(sensor), GpuSensor().Match(sensor).Groups[1].Value.Trim(), Math.Clamp(load, 0, 100)));
        }

        return new SensorReading(
            First(cpuTemperatures, CpuTemperatureLabels),
            First(cpuLoads, CpuLoadLabels) is { } cpuLoad ? Math.Clamp(cpuLoad, 0, 100) : null,
            gpus,
            gpuTemperatures);
    }

    /// <summary>The same for the load and the temperature of a card, so the card updates its rows in place.</summary>
    private static string Key(string sensor) => "HWiNFO " + sensor;

    /// <returns>The value of the first label found, in order of preference.</returns>
    private static double? First(Dictionary<string, double> values, string[] labels)
    {
        foreach (var label in labels)
        {
            if (values.TryGetValue(label, out var value))
                return value;
        }

        return null;
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }

    /// <summary>"GPU [#0]: NVIDIA GeForce RTX 4070 Laptop".</summary>
    [GeneratedRegex(@"^GPU \[#\d+\]:\s*(.+)$")]
    private static partial Regex GpuSensor();
}
