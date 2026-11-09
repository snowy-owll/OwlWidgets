using Microsoft.Extensions.Logging;
using OwlWidgets.Services;

namespace OwlWidgets.Hosting;

/// <summary>
/// An instance as the move mode sees it. The commands go to the wrapper of a running instance;
/// the instance ignores them in any other state.
/// </summary>
internal interface IMovableInstance
{
    Guid InstanceId { get; }

    InstanceState State { get; }

    void EnterMoveMode(string initiator);

    /// <summary>Ends the mode as the check (<paramref name="save"/>) or the cross does.</summary>
    void EndMoveMode(bool save);

    /// <summary>Ends the instance's own move mode without saving; nothing happens outside the mode.</summary>
    void CancelMoveMode();
}

/// <summary>
/// The instances of the manager as the move mode sees them.
/// </summary>
internal interface IMovableInstances
{
    IEnumerable<IMovableInstance> Movable { get; }

    /// <summary>An instance changed its state.</summary>
    event Action<IMovableInstance>? InstanceStateChanged;

    /// <summary>An instance left the move mode; true: its position was saved.</summary>
    event Action<IMovableInstance, bool>? MoveModeEnded;
}

/// <summary>
/// The move mode of the instances as a whole: the mass move, and the cancellation when the desktop is raised.
/// </summary>
internal sealed class MassMoveCoordinator : IDisposable
{
    private readonly IMovableInstances _instances;
    private readonly IDesktopLayer _desktop;
    private readonly ILogger<MassMoveCoordinator> _log;

    public MassMoveCoordinator(IMovableInstances instances, IDesktopLayer desktop, ILogger<MassMoveCoordinator> log)
    {
        _instances = instances;
        _desktop = desktop;
        _log = log;
        _instances.InstanceStateChanged += OnInstanceStateChanged;
        _instances.MoveModeEnded += OnMoveModeEnded;
        _desktop.DesktopRaised += OnDesktopRaised;
    }

    /// <summary><see cref="Active"/> or <see cref="CanStart"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Every running instance is in the move mode, and the first confirmation or cancellation ends it for all.
    /// </summary>
    public bool Active { get; private set; }

    public bool CanStart => _instances.Movable.Any(instance => instance.State == InstanceState.Running);

    /// <summary>
    /// Puts every running instance into the move mode. An instance that is already there keeps
    /// the origin it started with.
    /// </summary>
    public bool Enter(string initiator)
    {
        if (Active || !CanStart)
            return false;

        Active = true;
        _log.LogInformation("Mass move entered from {Initiator}", initiator);
        foreach (var instance in _instances.Movable)
            instance.EnterMoveMode("the mass move");
        Changed?.Invoke();
        return true;
    }

    public void Cancel(string initiator) => End(save: false, initiator, except: null);

    public void Dispose()
    {
        _instances.InstanceStateChanged -= OnInstanceStateChanged;
        _instances.MoveModeEnded -= OnMoveModeEnded;
        _desktop.DesktopRaised -= OnDesktopRaised;
    }

    /// <summary>An instance left the move mode by its own buttons: in the mass move the others follow it.</summary>
    private void OnMoveModeEnded(IMovableInstance instance, bool positionSaved) =>
        End(positionSaved, $"instance {instance.InstanceId:N}", instance);

    /// <summary>
    /// An instance that starts during the mass move joins it; the mass move ends when nothing is left running.
    /// </summary>
    private void OnInstanceStateChanged(IMovableInstance instance)
    {
        if (Active)
        {
            if (instance.State == InstanceState.Running)
                instance.EnterMoveMode("the mass move");
            else if (!CanStart)
                End(save: false, "the last running instance stopping", except: null);
        }

        Changed?.Invoke();
    }

    private void OnDesktopRaised()
    {
        if (Active)
        {
            End(save: false, "the desktop being raised", except: null);
            return;
        }

        foreach (var instance in _instances.Movable)
            instance.CancelMoveMode();
    }

    private void End(bool save, string initiator, IMovableInstance? except)
    {
        if (!Active)
            return;

        // Cleared first: the reports of the other instances must not end the mode again.
        Active = false;
        _log.LogInformation("Mass move ended by {Initiator}, positions {Outcome}", initiator, save ? "saved" : "restored");
        foreach (var instance in _instances.Movable)
        {
            if (instance != except)
                instance.EndMoveMode(save);
        }

        Changed?.Invoke();
    }
}
