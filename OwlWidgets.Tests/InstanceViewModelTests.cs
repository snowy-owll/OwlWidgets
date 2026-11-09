using OwlWidgets.ViewModels;
using Xunit;

namespace OwlWidgets.Tests;

public sealed class InstanceViewModelTests : IDisposable
{
    private readonly FakeDescriptor _descriptor = new();
    private readonly ManagerFixture _fixture;
    private readonly InstanceViewModel _instance;

    public InstanceViewModelTests()
    {
        var package = _descriptor.ToPackage();
        _fixture = new ManagerFixture([package], ManagerFixture.Record(package, x: 804, y: 26.67));
        _instance = _fixture.CreateInstance(0);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Fields_start_from_the_stored_position_in_whole_units()
    {
        Assert.Equal("804", _instance.PositionX);
        Assert.Equal("27", _instance.PositionY);
        Assert.False(_instance.PositionChanged);
        Assert.False(_instance.ApplyPositionCommand.CanExecute(null));
        Assert.False(_instance.RevertPositionCommand.CanExecute(null));
    }

    [Fact]
    public void Edited_fields_do_not_move_the_card_until_applied()
    {
        _instance.PositionX = "120";
        _instance.PositionY = "-7";

        Assert.True(_instance.PositionChanged);
        Assert.Equal(804, _instance.Session.X);
        Assert.True(_instance.ApplyPositionCommand.CanExecute(null));
    }

    [Fact]
    public void Applying_stores_the_position()
    {
        _instance.PositionX = "120";
        _instance.PositionY = "-7";

        _instance.ApplyPositionCommand.Execute(null);

        Assert.Equal(120, _instance.Session.X);
        Assert.Equal(-7, _instance.Session.Y);
        Assert.False(_instance.PositionChanged);
        var stored = _fixture.Stored(_instance.Session.InstanceId);
        Assert.Equal(120, stored.X);
        Assert.Equal(-7, stored.Y);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("12,5")]
    public void An_incomplete_or_fractional_number_cannot_be_applied(string y)
    {
        _instance.PositionY = y;

        Assert.False(_instance.ApplyPositionCommand.CanExecute(null));
        Assert.True(_instance.RevertPositionCommand.CanExecute(null));
    }

    [Fact]
    public void Reverting_puts_the_stored_position_back()
    {
        _instance.PositionX = "1";

        _instance.RevertPositionCommand.Execute(null);

        Assert.Equal("804", _instance.PositionX);
        Assert.False(_instance.PositionChanged);
        Assert.Equal(804, _instance.Session.X);
    }

    [Fact]
    public void A_stopped_instance_can_only_be_started()
    {
        Assert.True(_instance.StartCommand.CanExecute(null));
        Assert.False(_instance.StopCommand.CanExecute(null));
        Assert.False(_instance.MoveCommand.CanExecute(null));
    }
}
