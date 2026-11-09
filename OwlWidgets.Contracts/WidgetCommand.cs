namespace OwlWidgets.Contracts;

public readonly record struct WidgetCommand(string Code, string Target, ReadOnlyMemory<byte> Body = default);

public static class WidgetCommandTarget
{
    public const string Manager = "manager";
    public const string Wrapper = "wrapper";
    public const string Widget = "widget";
}

public static class WidgetCommandCodes
{
    public const string Shutdown = "shutdown";
    public const string Place = "place";
    public const string SetPosition = "set-position";
    public const string EnterMoveMode = "enter-move-mode";
    public const string CancelMoveMode = "cancel-move-mode";

    /// <summary>
    /// Manager to wrapper, no body: end the move mode keeping the position, as the check button does.
    /// </summary>
    public const string ConfirmMoveMode = "confirm-move-mode";
    public const string PlacementChanged = "placement-changed";
    public const string MoveMode = "move-mode";
    public const string Stopping = "stopping";
    public const string ApplySettings = "apply-settings";
    public const string PersistSettings = "persist-settings";
    public const string ShowNotification = "show-notification";
    public const string Stop = "stop";
    public const string Theme = "theme";

    /// <summary>
    /// Wrapper to widget, <see cref="AnchorBody"/>: the anchor corner of the instance. First right after <c>Start</c>,
    /// then on every change. A widget that has nothing to align to it ignores the command.
    /// </summary>
    public const string Anchor = "anchor";

    /// <summary>
    /// Widget to manager, <see cref="TopmostBody"/>: keep the card above all windows, or put it back under regular ones.
    /// The manager stores it with the instance and answers through the wrapper with <see cref="Topmost"/>.
    /// </summary>
    public const string SetTopmost = "set-topmost";

    /// <summary>
    /// Wrapper to widget, <see cref="TopmostBody"/>: whether the card is above all windows. First right after <c>Start</c>,
    /// then on every change, whoever made it: the widget or the settings screen of the manager.
    /// </summary>
    public const string Topmost = "topmost";
    public const string Language = "language";

    /// <summary>
    /// Widget to manager, no body: show the manager window on this instance, on the widget's own settings page
    /// when it has one, otherwise on the general one.
    /// </summary>
    public const string OpenSettings = "open-settings";
}
