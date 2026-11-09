namespace OwlWidgets.Services;

/// <summary>
/// The end of the manager process: exit and restart.
/// </summary>
internal interface IAppLifetime
{
    /// <summary>The exit has begun: windows are not shown any more.</summary>
    bool IsClosing { get; }

    /// <summary>Stops the instances and exits.</summary>
    void Close();

    /// <summary>
    /// Stops the instances, exits and starts the manager again.
    /// </summary>
    void Restart();
}
