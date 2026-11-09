namespace OwlWidgets.Widgets.SystemMonitor.Sensors;

/// <summary>One reading of everything the card shows, taken off the UI thread.</summary>
/// <param name="CpuLoad">%; null: Windows did not report it yet (the first second after the start).</param>
/// <param name="CpuTemperature">°C; null: no sensor program reports it.</param>
/// <param name="Drives">null: not read this time, the card keeps the previous list.</param>
internal sealed record SystemSample(
    double? CpuLoad,
    double? CpuTemperature,
    CpuTemperatureSource CpuTemperatureSource,
    MemorySample? Memory,
    IReadOnlyList<GpuLoadSample> GpuLoads,
    IReadOnlyList<TemperatureSample> GpuTemperatures,
    IReadOnlyList<DriveSample>? Drives);

/// <summary>Why the CPU temperature is there or not.</summary>
internal enum CpuTemperatureSource
{
    /// <summary>The provider reports it.</summary>
    Provider,

    /// <summary>The instance is set to Windows only, which does not report it.</summary>
    NoProvider,

    /// <summary>No sensor program of the instance is running.</summary>
    ProviderNotFound,

    /// <summary>The program runs but has no CPU temperature among its sensors.</summary>
    NotReported
}

/// <param name="Used">GB.</param>
/// <param name="Total">GB.</param>
internal sealed record MemorySample(double Used, double Total);

/// <param name="Key">Stable while the adapter or sensor stays: the card updates its row in place by it.</param>
/// <param name="Load">%.</param>
internal sealed record GpuLoadSample(string Key, string Name, double Load);

/// <param name="Value">°C; null: <paramref name="Asleep"/> or no value this time.</param>
/// <param name="Asleep">The video card is powered off (a discrete card of a laptop at rest) and is not woken to be asked.</param>
internal sealed record TemperatureSample(string Key, string Name, double? Value, bool Asleep = false);

/// <param name="Used">GB.</param>
/// <param name="Total">GB.</param>
internal sealed record DriveSample(string Name, double Used, double Total);
