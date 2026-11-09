using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OwlWidgets.Common.Devices;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.SystemMonitor.Sensors.Native;

namespace OwlWidgets.Widgets.SystemMonitor.Sensors;

/// <summary>
/// What Windows reports by itself, without a sensor program or administrator rights, the same for every vendor:
/// CPU load, memory, load of each video card, the temperature of the video cards whose driver reports it, and disks.
/// </summary>
internal sealed partial class WindowsSensors : IDisposable
{
    private static readonly TimeSpan AdapterRefresh = TimeSpan.FromSeconds(30);
    private const double BytesPerGigabyte = 1024.0 * 1024 * 1024;

    private readonly IWidgetLogger _logger;
    private readonly SourceLog _counterLog;
    private readonly SourceLog _memoryLog;
    private readonly SourceLog _adapterLog;
    private readonly SourceLog _driveLog;
    private readonly PdhQuery? _query;
    private readonly nint _cpu;
    private readonly nint _gpuEngines;
    private List<Adapter> _adapters = [];
    private DateTime _adaptersRead = DateTime.MinValue;

    /// <summary>LUIDs of the adapters that reported a temperature at least once.</summary>
    private readonly HashSet<long> _reportsTemperature = [];

    /// <param name="readLoads">False: no performance counters, no CPU and video card load.</param>
    public WindowsSensors(IWidgetLogger logger, bool readLoads)
    {
        _logger = logger;
        _counterLog = new SourceLog(logger, "Performance counters");
        _memoryLog = new SourceLog(logger, "Memory status");
        _adapterLog = new SourceLog(logger, "Graphics adapters");
        _driveLog = new SourceLog(logger, "Drives");
        if (!readLoads)
            return;
        try
        {
            _query = new PdhQuery();
            // % Processor Utility is what Task Manager shows: it counts the time at the real frequency,
            // so a core at turbo counts for more than 100% and is capped below. Processor Time is older and lower.
            _cpu = TryAdd(_query, @"\Processor Information(_Total)\% Processor Utility")
                ?? TryAdd(_query, @"\Processor(_Total)\% Processor Time")
                ?? 0;
            _gpuEngines = TryAdd(_query, @"\GPU Engine(*)\Utilization Percentage") ?? 0;
            _query.Collect();
        }
        catch (Exception exception)
        {
            _logger.Log(WidgetLogLevel.Error, "Performance counters are not available: no CPU and GPU load.", exception);
            _query?.Dispose();
            _query = null;
        }
    }

    /// <summary>Reads counters; the values below are those of the time since the previous call.</summary>
    public void Collect()
    {
        if (_query == null)
            return;
        try
        {
            _query.Collect();
            _counterLog.Succeeded();
        }
        catch (Exception exception)
        {
            _counterLog.Failed(exception);
        }
    }

    /// <summary>
    /// The model as the firmware names it, without the marks, the clock and the built-in graphics, which has
    /// a tile of its own: "Intel(R) Core(TM) i7-8750H CPU @ 2.20GHz" → "Intel Core i7-8750H",
    /// "AMD Ryzen 7 5800X 8-Core Processor" → "AMD Ryzen 7 5800X", "AMD Ryzen 9 7940HS w/ Radeon 780M Graphics" → "AMD Ryzen 9 7940HS".
    /// </summary>
    /// <returns>Null: not found.</returns>
    public string? ReadCpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is not string name)
                return null;
            name = CpuNameNoise().Replace(name, " ");
            name = Whitespace().Replace(name, " ").Trim();
            return name.Length > 0 ? name : null;
        }
        catch (Exception exception)
        {
            _logger.Log(WidgetLogLevel.Warning, "The CPU name was not read.", exception);
            return null;
        }
    }

    /// <returns>%; null: no value yet.</returns>
    public double? ReadCpuLoad()
    {
        if (_cpu == 0)
            return null;
        return PdhQuery.Read(_cpu) is { } load ? Math.Clamp(load, 0, 100) : null;
    }

    public MemorySample? ReadMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            _memoryLog.Failed(new System.ComponentModel.Win32Exception());
            return null;
        }

        _memoryLog.Succeeded();
        return new MemorySample((status.ullTotalPhys - status.ullAvailPhys) / BytesPerGigabyte, status.ullTotalPhys / BytesPerGigabyte);
    }

    /// <summary>Load and temperature of each video card; what is not asked for is not read and comes empty.</summary>
    public (List<GpuLoadSample> Loads, List<TemperatureSample> Temperatures) ReadGpus(bool readLoads, bool readTemperatures)
    {
        var loads = new List<GpuLoadSample>();
        var temperatures = new List<TemperatureSample>();
        if (!readLoads && !readTemperatures)
            return (loads, temperatures);

        RefreshAdapters();
        var engines = readLoads ? ReadEngineLoads() : [];
        foreach (var adapter in _adapters)
        {
            if (readLoads)
                loads.Add(new GpuLoadSample(adapter.Key, adapter.Name, engines.GetValueOrDefault(adapter.Luid)));
            if (!readTemperatures)
                continue;

            // A discrete card of a laptop sleeps while nothing draws on it. The power state is kept by
            // the PnP manager; the driver is not asked, so a sleeping card is not woken up every second.
            if (PciNodes.IsAsleep(adapter.Node))
            {
                if (adapter.Discrete || _reportsTemperature.Contains(adapter.Luid))
                    temperatures.Add(new TemperatureSample(adapter.Key, adapter.Name, null, Asleep: true));
                continue;
            }

            var temperature = D3DKmt.ReadTemperature(adapter.Handle);
            if (temperature != null)
                _reportsTemperature.Add(adapter.Luid);
            if (temperature != null || _reportsTemperature.Contains(adapter.Luid))
                temperatures.Add(new TemperatureSample(adapter.Key, adapter.Name, temperature));
        }

        return (loads, temperatures);
    }

    /// <summary>The drives the settings turn on; the others are not read, so a sleeping disk or a lost server is left alone.</summary>
    public List<DriveSample>? ReadDrives(Config config)
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => DriveKinds.IsShown(d, config) && d.IsReady && d.TotalSize > 0)
                .Select(d => new DriveSample(d.Name, (d.TotalSize - d.AvailableFreeSpace) / BytesPerGigabyte, d.TotalSize / BytesPerGigabyte))
                .ToList();
            _driveLog.Succeeded();
            return drives;
        }
        catch (Exception exception)
        {
            _driveLog.Failed(exception);
            return null;
        }
    }

    public void Dispose()
    {
        CloseAdapters();
        _query?.Dispose();
    }

    private nint? TryAdd(PdhQuery query, string path)
    {
        try
        {
            return query.Add(path);
        }
        catch (Exception exception)
        {
            _logger.Log(WidgetLogLevel.Debug, $"Counter {path} is not available.", exception);
            return null;
        }
    }

    /// <summary>
    /// The load of an adapter as Task Manager shows it: the busiest of its engines (3D, Copy, Video Decode…),
    /// each engine being the sum of the processes that use it.
    /// </summary>
    private Dictionary<long, double> ReadEngineLoads()
    {
        var byEngine = new Dictionary<(long Luid, string Engine), double>();
        if (_gpuEngines != 0)
        {
            foreach (var (instance, value) in PdhQuery.ReadAll(_gpuEngines))
            {
                var match = EngineInstance().Match(instance);
                if (!match.Success)
                    continue;
                var luid = (long.Parse(match.Groups[1].ValueSpan, NumberStyles.HexNumber) << 32)
                    | long.Parse(match.Groups[2].ValueSpan, NumberStyles.HexNumber);
                var key = (luid, match.Groups[3].Value);
                byEngine[key] = byEngine.GetValueOrDefault(key) + value;
            }
        }

        return byEngine.GroupBy(e => e.Key.Luid).ToDictionary(g => g.Key, g => Math.Min(g.Max(e => e.Value), 100));
    }

    private void RefreshAdapters()
    {
        if (DateTime.UtcNow - _adaptersRead < AdapterRefresh)
            return;
        _adaptersRead = DateTime.UtcNow;
        try
        {
            CloseAdapters();
            var pciNodes = PciNodes.Read();
            var adapters = new List<Adapter>();
            foreach (var (handle, luid) in D3DKmt.Enumerate())
            {
                // Not a video card of its own: the software renderer, an NPU, a virtual display (spacedesk, Parsec).
                var type = D3DKmt.ReadType(handle);
                if (type is not { } flags
                    || !flags.HasFlag(D3DKmt.AdapterFlags.RenderSupported)
                    || (flags & (D3DKmt.AdapterFlags.SoftwareDevice | D3DKmt.AdapterFlags.ComputeOnly | D3DKmt.AdapterFlags.IndirectDisplayDevice)) != 0)
                {
                    D3DKmt.Close(handle);
                    continue;
                }

                uint? node = D3DKmt.ReadAddress(handle) is { } address
                    && pciNodes.TryGetValue((address.Bus, PciNodes.Address(address.Device, address.Function)), out var found) ? found : null;
                adapters.Add(new Adapter(handle, luid, luid.ToString("X", CultureInfo.InvariantCulture),
                    D3DKmt.ReadName(handle) ?? "GPU", node, flags.HasFlag(D3DKmt.AdapterFlags.HybridDiscrete)));
            }

            _adapters = adapters;
            _adapterLog.Succeeded();
        }
        catch (Exception exception)
        {
            _adapterLog.Failed(exception);
        }
    }

    private void CloseAdapters()
    {
        foreach (var adapter in _adapters)
            D3DKmt.Close(adapter.Handle);
        _adapters = [];
    }

    [GeneratedRegex(@"luid_0x([0-9A-Fa-f]{8})_0x([0-9A-Fa-f]{8})_phys_\d+_eng_\d+_engtype_(.*)$")]
    private static partial Regex EngineInstance();

    [GeneratedRegex(@"\((R|TM)\)|\s(CPU\s+)?@\s*[\d.]+\s*GHz|\s\d+-Core\s+Processor\b|\s(w/|with)\s+Radeon\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex CpuNameNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <param name="Node">The PCI device node; null: not found, the power state is not checked.</param>
    /// <param name="Discrete">The discrete card of a laptop with two video cards.</param>
    private sealed record Adapter(uint Handle, long Luid, string Key, string Name, uint? Node, bool Discrete);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
