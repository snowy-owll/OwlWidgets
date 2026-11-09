namespace OwlWidgets.Widgets.SystemMonitor.Sensors;

/// <summary>
/// A sensor program that runs apart from the widget (HWiNFO, MSI Afterburner…). What it reports is taken
/// instead of what Windows reports: the CPU temperature first of all, which Windows does not give, then the
/// CPU load and the video cards. Memory and disks always come from Windows.
/// </summary>
internal interface ISensorProvider : IDisposable
{
    /// <summary>Name of the program for the log.</summary>
    string Name { get; }

    /// <summary>Called every second off the UI thread.</summary>
    /// <returns>null: the program is not running or does not share its sensors now.</returns>
    SensorReading? Read();
}

/// <param name="CpuTemperature">°C; null: the program has no CPU temperature.</param>
/// <param name="CpuLoad">%; null: the program has none, the card shows that of Windows.</param>
/// <param name="GpuLoads">The video cards with a load.</param>
/// <param name="GpuTemperatures">The video cards with a temperature.</param>
internal sealed record SensorReading(
    double? CpuTemperature,
    double? CpuLoad,
    IReadOnlyList<GpuLoadSample> GpuLoads,
    IReadOnlyList<TemperatureSample> GpuTemperatures)
{
    /// <summary>
    /// The program knows a video card: then all of them come from it, load and temperature, and none from Windows,
    /// so cards named differently by the two are never matched.
    /// </summary>
    public bool HasGpus => GpuLoads.Count > 0 || GpuTemperatures.Count > 0;
}
