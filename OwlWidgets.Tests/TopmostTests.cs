using Xunit;

namespace OwlWidgets.Tests;

/// <summary>Above all windows: a property of the instance in the registry, not of the widget settings.</summary>
public sealed class TopmostTests
{
    private readonly FakeDescriptor _descriptor = new();

    [Fact]
    public void A_new_instance_is_under_regular_windows()
    {
        using var fixture = new ManagerFixture([_descriptor.ToPackage()], ManagerFixture.Record(_descriptor.ToPackage()));

        Assert.False(fixture.Session(0).Topmost);
    }

    [Fact]
    public void The_check_box_stores_the_choice_with_the_instance()
    {
        var package = _descriptor.ToPackage();
        using var fixture = new ManagerFixture([package], ManagerFixture.Record(package));
        var instance = fixture.CreateInstance(0);
        var changed = new List<string?>();
        instance.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        instance.Topmost = true;

        Assert.True(instance.Session.Topmost);
        Assert.True(fixture.Stored(instance.Session.InstanceId).Topmost);
        Assert.Contains(nameof(instance.Topmost), changed);
        Assert.Null(instance.Session.SettingsJson);

        instance.Topmost = false;

        Assert.False(fixture.Stored(instance.Session.InstanceId).Topmost);
    }
}
