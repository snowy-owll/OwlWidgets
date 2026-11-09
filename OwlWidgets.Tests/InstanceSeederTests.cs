using Microsoft.Extensions.Logging.Abstractions;
using OwlWidgets.Hosting;
using OwlWidgets.Services;
using Xunit;

namespace OwlWidgets.Tests;

/// <summary>The first start without a registry: one Weather instance, whatever else is installed.</summary>
public sealed class InstanceSeederTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OwlWidgets.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly InstanceRegistry _registry;
    private readonly InstanceSettingsStore _settings;
    private readonly InstanceSeeder _seeder;

    public InstanceSeederTests()
    {
        _paths = new AppPaths(_root);
        _registry = new InstanceRegistry(_paths);
        _settings = new InstanceSettingsStore(_paths, NullLogger<InstanceSettingsStore>.Instance);
        _seeder = new InstanceSeeder(_registry, _settings, NullLogger<InstanceSeeder>.Instance);
    }

    [Fact]
    public async Task Only_a_Weather_instance_is_created_with_the_starter_settings_of_its_descriptor()
    {
        var weather = new FakeDescriptor
        {
            Id = InstanceSeeder.WeatherId,
            Name = "Weather",
            StarterSettings = _ => Task.FromResult<string?>("{\"Provider\":\"OpenMeteo\"}")
        };

        await _seeder.SeedAsync([new FakeDescriptor { Name = "Another" }.ToPackage(), weather.ToPackage()]);

        var record = Assert.Single(Stored().Instances);
        Assert.Equal(InstanceSeeder.WeatherId, record.WidgetId);
        Assert.True(record.StartWithManager);
        Assert.Equal("{\"Provider\":\"OpenMeteo\"}", _settings.Read(record.InstanceId));
    }

    [Fact]
    public async Task Failed_starter_settings_leave_the_instance_with_the_defaults()
    {
        var weather = new FakeDescriptor
        {
            Id = InstanceSeeder.WeatherId,
            StarterSettings = _ => throw new InvalidOperationException("No network")
        };

        await _seeder.SeedAsync([weather.ToPackage()]);

        var record = Assert.Single(Stored().Instances);
        Assert.False(File.Exists(_paths.SettingsPath(record.InstanceId)));
    }

    [Fact]
    public async Task Without_the_Weather_package_the_registry_is_written_empty()
    {
        await _seeder.SeedAsync([new FakeDescriptor().ToPackage()]);

        var stored = Stored();
        Assert.True(stored.Existed);
        Assert.Empty(stored.Instances);
    }

    private InstanceRegistry Stored()
    {
        var registry = new InstanceRegistry(_paths);
        registry.Load();
        return registry;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
