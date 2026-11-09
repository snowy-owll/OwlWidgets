using System.Windows.Media;

namespace OwlWidgets.Contracts;

public interface IWidgetDescriptor
{
    Guid Id { get; }

    string Name { get; }

    string Description { get; }

    string Version { get; }

    string Author { get; }

    /// <summary>
    /// true: the type may have several instances, each with its own settings.
    /// </summary>
    bool AllowMultipleInstances { get; }

    /// <summary>
    /// Icon of the widget type; null: the widget has none. The manager shows it at a fixed size,
    /// so the image may be of any size and aspect ratio. Usually a <c>Resource</c> of the descriptor
    /// assembly loaded by a pack URI. Read once, in the manager process.
    /// </summary>
    ImageSource? Icon => null;

    /// <summary>
    /// Whether the widget view takes the application's Material Design theme.
    /// The default implementation keeps existing widgets as they are.
    /// </summary>
    HostThemeInheritance HostTheme => HostThemeInheritance.Off;

    /// <summary>
    /// Settings of the instance the manager creates by itself on its very first start; null: the defaults.
    /// Called in the manager process, on the UI thread, at most once per start. The manager cancels the call
    /// after a few seconds and then creates the instance with the defaults, so a widget that asks the network
    /// gives up on its own sooner and returns what it has. A failure goes to <paramref name="log"/>.
    /// </summary>
    Task<string?> CreateStarterSettingsAsync(IWidgetLogger log, CancellationToken token) => Task.FromResult<string?>(null);

    /// <summary>
    /// null: the widget has no settings screen of its own.
    /// </summary>
    IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance);
}

/// <summary>
/// How a widget view relates to the application's Material Design theme.
/// </summary>
public enum HostThemeInheritance
{
    /// <summary>Not inherited by default; the user may turn it on for an instance.</summary>
    Off,

    /// <summary>Inherited by default; the user may turn it off for an instance.</summary>
    On,

    /// <summary>The view references theme resources and does not load without them; always inherited.</summary>
    Required
}

public sealed class WidgetInstanceInfo
{
    public required Guid InstanceId { get; init; }

    public required string ContentRoot { get; init; }

    /// <inheritdoc cref="IWidgetHostContext.InstanceDataFolder"/>
    public required string InstanceDataFolder { get; init; }
}
