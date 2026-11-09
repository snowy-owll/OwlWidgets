using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.SystemMonitor.Sensors;

/// <summary>
/// Puts together a <see cref="SystemSample"/> from the first running sensor program of the instance and from Windows,
/// value by value: the CPU temperature only from the program; the CPU load from the program when it has it,
/// otherwise from Windows; the video cards, load and temperature, all from the program when it knows any,
/// otherwise all from Windows, so cards named differently by the two are never matched; memory and disks from Windows.
/// </summary>
internal sealed class SystemSampler : IDisposable
{
    private static readonly TimeSpan DriveRefresh = TimeSpan.FromSeconds(30);

    private readonly Config _config;
    private readonly IWidgetLogger _logger;
    private readonly WindowsSensors _windows;
    private readonly IReadOnlyList<ISensorProvider> _providers;
    private readonly Dictionary<ISensorProvider, SourceLog> _providerLogs;
    private ISensorProvider? _current;

    /// <summary>The absence of a program is logged once at the start and then only after it is lost.</summary>
    private bool _missingLogged;
    private DateTime _drivesRead = DateTime.MinValue;

    /// <param name="config">The sections and drives turned off are not read.</param>
    /// <param name="providers">In order of preference; empty: Windows only, or neither System nor Temperature is shown.</param>
    public SystemSampler(Config config, IReadOnlyList<ISensorProvider> providers, IWidgetLogger logger)
    {
        _config = config;
        _logger = logger;
        _providers = providers;
        _providerLogs = providers.ToDictionary(p => p, p => new SourceLog(logger, p.Name));
        _windows = new WindowsSensors(logger, readLoads: config.ShowSystem);
        CpuName = config.ShowTemperatures ? _windows.ReadCpuName() : null;
        if (providers.Count == 0 && (config.ShowSystem || config.ShowTemperatures))
            _logger.Log(WidgetLogLevel.Information, "No sensor program is used: Windows only, no CPU temperature.");
    }

    /// <summary>Read once: the model does not change while Windows runs. Null: not found.</summary>
    public string? CpuName { get; }

    /// <summary>Called every second off the UI thread, one call at a time.</summary>
    public SystemSample Read()
    {
        var reading = _providers.Count > 0 ? ReadProvider() : null;
        if (_config.ShowSystem)
            _windows.Collect();
        // The adapters of Windows are not even listed while the program has the cards.
        var (gpuLoads, gpuTemperatures) = reading is { HasGpus: true }
            ? (_config.ShowSystem ? reading.GpuLoads : [], _config.ShowTemperatures ? reading.GpuTemperatures : [])
            : _windows.ReadGpus(_config.ShowSystem, _config.ShowTemperatures);

        List<DriveSample>? drives = null;
        if (_config.ShowDrives && DateTime.UtcNow - _drivesRead >= DriveRefresh)
        {
            _drivesRead = DateTime.UtcNow;
            drives = _windows.ReadDrives(_config);
        }

        var source = _providers.Count == 0 ? CpuTemperatureSource.NoProvider
            : reading == null ? CpuTemperatureSource.ProviderNotFound
            : reading.CpuTemperature == null ? CpuTemperatureSource.NotReported
            : CpuTemperatureSource.Provider;
        return new SystemSample(
            _config.ShowSystem ? reading?.CpuLoad ?? _windows.ReadCpuLoad() : null,
            _config.ShowTemperatures ? reading?.CpuTemperature : null,
            source,
            _config.ShowSystem ? _windows.ReadMemory() : null,
            gpuLoads,
            gpuTemperatures,
            drives);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
        _windows.Dispose();
    }

    private SensorReading? ReadProvider()
    {
        foreach (var provider in _providers)
        {
            SensorReading? reading;
            try
            {
                reading = provider.Read();
                _providerLogs[provider].Succeeded();
            }
            catch (Exception exception)
            {
                _providerLogs[provider].Failed(exception);
                continue;
            }

            if (reading == null)
                continue;
            if (_current != provider)
            {
                _current = provider;
                _logger.Log(WidgetLogLevel.Information, $"Sensors come from {provider.Name}.");
            }

            return reading;
        }

        if (_current != null)
        {
            _logger.Log(WidgetLogLevel.Information, $"{_current.Name} no longer shares its sensors; Windows only, no CPU temperature until it does.");
            _current = null;
        }
        else if (!_missingLogged && _providers.Count > 0)
        {
            _logger.Log(WidgetLogLevel.Information,
                $"No sensor program shares its sensors ({string.Join(", ", _providers.Select(p => p.Name))}); Windows only, no CPU temperature until one does.");
        }

        _missingLogged = true;
        return null;
    }
}
