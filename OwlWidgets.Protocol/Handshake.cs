using OwlWidgets.Contracts;

namespace OwlWidgets.Protocol;

public static class HandshakeCodes
{
    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

/// <summary>
/// Channel codes between the manager and the wrapper that are not widget commands.
/// </summary>
public static class ChannelCodes
{
    /// <summary>Wrapper to manager: the body is a <see cref="LogRecord"/> in JSON.</summary>
    public const string Log = "log";

    /// <summary>Manager to wrapper: the body is a <see cref="HostThemeBody"/> in JSON.</summary>
    public const string HostTheme = "host-theme";

    /// <summary>
    /// Manager to wrapper, <see cref="AnchorBody"/>: give the position for another corner of the card. The card stays
    /// where it is; the wrapper answers with <c>placement-changed</c> holding the position of the new corner.
    /// </summary>
    public const string HostAnchor = "host-anchor";

    /// <summary>
    /// Wrapper to manager, <see cref="CardSizeBody"/>: the size of the card once it has settled. The manager keeps it
    /// to change the anchor of a stopped instance without moving the card.
    /// </summary>
    public const string CardSize = "card-size";

    /// <summary>
    /// Manager to wrapper, <see cref="TopmostBody"/>: keep the window above all windows, or go back under regular ones
    /// and follow <c>place</c> again. The wrapper tells the widget with <c>topmost</c>.
    /// </summary>
    public const string HostTopmost = "host-topmost";
}

/// <summary>Size of the card without the transparent margin of the window, device-independent units.</summary>
public sealed class CardSizeBody
{
    public double Width { get; set; }

    public double Height { get; set; }
}

public sealed class HelloBody
{
    public Guid InstanceId { get; set; }

    public string Token { get; set; } = string.Empty;

    public int ProtocolVersion { get; set; }
}

public sealed class WelcomeBody
{
    public string RuntimeAssembly { get; set; } = string.Empty;

    public string RuntimeType { get; set; } = string.Empty;

    public string? Settings { get; set; }

    /// <summary>Position of the <see cref="Anchor"/> corner of the card.</summary>
    public double X { get; set; }

    /// <inheritdoc cref="X"/>
    public double Y { get; set; }

    public CardCorner Anchor { get; set; }

    /// <summary>Above all windows instead of under regular ones; <c>place</c> is ignored meanwhile.</summary>
    public bool Topmost { get; set; }

    public string ContentRoot { get; set; } = string.Empty;

    /// <summary>Files of the instance, see <see cref="IWidgetHostContext.InstanceDataFolder"/>.</summary>
    public string InstanceDataFolder { get; set; } = string.Empty;

    public Guid WidgetId { get; set; }

    /// <summary>Records below this level are not sent to the manager.</summary>
    public WidgetLogLevel LogLevel { get; set; } = WidgetLogLevel.Information;

    public HostThemeBody Theme { get; set; } = new();
}

/// <summary>
/// The theme of one instance as the manager resolved it. The wrapper applies it to the move frame
/// and, when <see cref="InheritMaterialDesign"/> is set, to the widget view, then tells the widget.
/// </summary>
public sealed class HostThemeBody
{
    public bool Dark { get; set; }

    public bool InheritMaterialDesign { get; set; }
}

public sealed class ReadyBody
{
    public string Hwnd { get; set; } = "0";
}

public sealed class FailedBody
{
    public string Message { get; set; } = string.Empty;
}

public static class PipeNames
{
    public static string ForInstance(int managerPid, Guid instanceId) =>
        $"owlwidgets-{managerPid}-{instanceId:N}";
}
