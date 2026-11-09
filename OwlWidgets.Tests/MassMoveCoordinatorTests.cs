using Microsoft.Extensions.Logging.Abstractions;
using OwlWidgets.Hosting;
using Xunit;

namespace OwlWidgets.Tests;

public sealed class MassMoveCoordinatorTests : IDisposable
{
    private readonly FakeMovable _first = new("first");
    private readonly FakeMovable _second = new("second");
    private readonly FakeMovable _stopped = new("stopped", InstanceState.Stopped);
    private readonly FakeMovables _instances;
    private readonly FakeDesktop _desktop = new();
    private readonly MassMoveCoordinator _massMove;
    private int _changes;

    public MassMoveCoordinatorTests()
    {
        _instances = new FakeMovables(_first, _second, _stopped);
        _massMove = new MassMoveCoordinator(_instances, _desktop, NullLogger<MassMoveCoordinator>.Instance);
        _massMove.Changed += () => _changes++;
    }

    public void Dispose() => _massMove.Dispose();

    private void ClearCommands()
    {
        foreach (var instance in _instances.Instances)
            instance.Commands.Clear();
    }

    [Fact]
    public void Entering_puts_every_instance_into_the_move_mode()
    {
        Assert.True(_massMove.Enter("the tests"));

        Assert.True(_massMove.Active);
        Assert.Equal(["enter"], _first.Commands);
        Assert.Equal(["enter"], _second.Commands);
        // A stopped instance gets the command too and ignores it, as InstanceSession does.
        Assert.Equal(["enter"], _stopped.Commands);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Entering_again_changes_nothing()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        Assert.False(_massMove.Enter("the tests"));

        Assert.Empty(_first.Commands);
        Assert.True(_massMove.Active);
    }

    [Fact]
    public void Nothing_starts_without_a_running_instance()
    {
        _instances.ChangeState(_first, InstanceState.Stopped);
        _instances.ChangeState(_second, InstanceState.Faulted);

        Assert.False(_massMove.CanStart);
        Assert.False(_massMove.Enter("the tests"));
        Assert.False(_massMove.Active);
        Assert.Empty(_first.Commands);
    }

    [Fact]
    public void The_check_on_one_card_saves_the_others()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        _instances.EndMoveMode(_first, positionSaved: true);

        Assert.False(_massMove.Active);
        Assert.Empty(_first.Commands);
        Assert.Equal(["confirm"], _second.Commands);
        Assert.Equal(["confirm"], _stopped.Commands);
    }

    [Fact]
    public void The_cross_on_one_card_restores_the_others()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        _instances.EndMoveMode(_second, positionSaved: false);

        Assert.False(_massMove.Active);
        Assert.Equal(["restore"], _first.Commands);
        Assert.Empty(_second.Commands);
    }

    [Fact]
    public void The_reports_of_the_other_cards_do_not_end_the_mode_again()
    {
        _massMove.Enter("the tests");
        _instances.EndMoveMode(_first, positionSaved: true);
        ClearCommands();
        var changes = _changes;

        // The others confirm their positions in answer to confirm-move-mode.
        _instances.EndMoveMode(_second, positionSaved: true);
        _instances.EndMoveMode(_stopped, positionSaved: true);

        Assert.Empty(_first.Commands);
        Assert.Empty(_second.Commands);
        Assert.Equal(changes, _changes);
    }

    [Fact]
    public void Cancelling_restores_every_card()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        _massMove.Cancel("the tray menu");

        Assert.False(_massMove.Active);
        Assert.Equal(["restore"], _first.Commands);
        Assert.Equal(["restore"], _second.Commands);
    }

    [Fact]
    public void Cancelling_outside_the_mass_move_changes_nothing()
    {
        _massMove.Cancel("the tray menu");

        Assert.Empty(_first.Commands);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void An_instance_started_meanwhile_joins()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        _instances.ChangeState(_stopped, InstanceState.Starting);
        Assert.Empty(_stopped.Commands);
        _instances.ChangeState(_stopped, InstanceState.Running);

        Assert.Equal(["enter"], _stopped.Commands);
        Assert.True(_massMove.Active);
    }

    [Fact]
    public void A_stopping_instance_leaves_the_others_in_the_mode()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        _instances.ChangeState(_first, InstanceState.Stopping);

        Assert.True(_massMove.Active);
        Assert.Empty(_second.Commands);
    }

    [Fact]
    public void The_mode_ends_when_nothing_is_left_running()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        _instances.ChangeState(_first, InstanceState.Stopped);
        Assert.True(_massMove.Active);
        _instances.ChangeState(_second, InstanceState.Faulted);

        Assert.False(_massMove.Active);
        Assert.Equal(["restore"], _first.Commands);
        Assert.Equal(["restore"], _second.Commands);
    }

    [Fact]
    public void Raising_the_desktop_restores_every_card_in_the_mass_move()
    {
        _massMove.Enter("the tests");
        ClearCommands();

        _desktop.RaiseDesktop();

        Assert.False(_massMove.Active);
        Assert.Equal(["restore"], _first.Commands);
        Assert.Equal(["restore"], _second.Commands);
    }

    [Fact]
    public void Raising_the_desktop_outside_the_mass_move_cancels_each_own_mode()
    {
        _desktop.RaiseDesktop();

        Assert.Equal(["cancel-own"], _first.Commands);
        Assert.Equal(["cancel-own"], _second.Commands);
        Assert.False(_massMove.Active);
    }

    [Fact]
    public void One_card_leaving_its_own_mode_does_not_touch_the_others()
    {
        _instances.EndMoveMode(_first, positionSaved: true);

        Assert.Empty(_second.Commands);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void Every_state_change_is_announced_for_the_buttons()
    {
        _instances.ChangeState(_stopped, InstanceState.Starting);
        _instances.ChangeState(_stopped, InstanceState.Running);

        Assert.Equal(2, _changes);
    }

    [Fact]
    public void After_disposal_nothing_is_followed()
    {
        _massMove.Enter("the tests");
        _massMove.Dispose();
        ClearCommands();

        _desktop.RaiseDesktop();
        _instances.EndMoveMode(_first, positionSaved: true);
        _instances.ChangeState(_stopped, InstanceState.Running);

        Assert.All(_instances.Instances, instance => Assert.Empty(instance.Commands));
    }
}
