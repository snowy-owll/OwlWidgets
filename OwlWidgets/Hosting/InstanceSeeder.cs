using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;
using OwlWidgets.Services;

namespace OwlWidgets.Hosting;

/// <summary>
/// The first start without a registry: one Weather instance with the settings its descriptor gives for it
/// (<see cref="IWidgetDescriptor.CreateStarterSettingsAsync"/>). Without the Weather package the list stays empty.
/// </summary>
internal sealed class InstanceSeeder
{
    public static readonly Guid WeatherId = Guid.Parse("235c158b-6399-4aa1-a8e8-54fd58fa3326");

    /// <summary>The first start waits for the descriptor no longer: the instance is then created with the defaults.</summary>
    public static readonly TimeSpan StarterSettingsTimeout = TimeSpan.FromSeconds(10);

    private readonly InstanceRegistry _registry;
    private readonly IInstanceSettingsStore _settings;
    private readonly ILogger<InstanceSeeder> _log;

    public InstanceSeeder(InstanceRegistry registry, IInstanceSettingsStore settings, ILogger<InstanceSeeder> log)
    {
        _registry = registry;
        _settings = settings;
        _log = log;
    }

    public async Task SeedAsync(IReadOnlyList<WidgetPackage> packages)
    {
        var package = packages.FirstOrDefault(package => package.Descriptor.Id == WeatherId);
        if (package == null)
        {
            _log.LogWarning("Weather package not found, no instance is created");
            _registry.Save();
            return;
        }

        var record = new InstanceRecord
        {
            InstanceId = Guid.NewGuid(),
            WidgetId = package.Descriptor.Id,
            X = 80,
            Y = 80
        };
        var settings = await CreateSettingsAsync(package).ConfigureAwait(true);
        if (settings != null)
            _settings.Write(record.InstanceId, settings);
        _registry.Instances.Add(record);
        _registry.Save();
        _log.LogInformation("Instance {InstanceId} of {Widget} created with {Settings}", record.InstanceId,
            package.Descriptor.Name, settings == null ? "default settings" : "starter settings");
    }

    private async Task<string?> CreateSettingsAsync(WidgetPackage package)
    {
        using var cancellation = new CancellationTokenSource(StarterSettingsTimeout);
        try
        {
            return await package.Descriptor.CreateStarterSettingsAsync(new DescriptorLogger(_log), cancellation.Token)
                .WaitAsync(StarterSettingsTimeout).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log.LogWarning(exception, "Starter settings of {Widget} were not created, the defaults are used",
                package.Descriptor.Name);
            return null;
        }
    }

    /// <summary>Records of the descriptor go to the manager log, marked by the seeder category.</summary>
    private sealed class DescriptorLogger(ILogger log) : IWidgetLogger
    {
        public void Log(WidgetLogLevel level, string message, Exception? exception = null) =>
            log.Log(level switch
            {
                WidgetLogLevel.Trace => LogLevel.Trace,
                WidgetLogLevel.Debug => LogLevel.Debug,
                WidgetLogLevel.Information => LogLevel.Information,
                WidgetLogLevel.Warning => LogLevel.Warning,
                WidgetLogLevel.Error => LogLevel.Error,
                _ => LogLevel.Critical
            }, exception, "{Message}", message);
    }
}
