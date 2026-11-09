using System.Windows;

namespace OwlWidgets.Contracts;

public interface IWidgetRuntime : IDisposable
{
    FrameworkElement? View { get; }

    void Start(IWidgetHostContext context);

    void Receive(WidgetCommand command);

    void Stop();
}

public interface IWidgetHostContext
{
    Guid WidgetId { get; }

    Guid InstanceId { get; }

    string ContentRoot { get; }

    /// <summary>
    /// Files of this instance, <c>Data/storage/{instanceId}</c>: kept across restarts and deleted with the instance.
    /// The folder may not exist yet; the widget creates it when it writes. The settings screen of an instance
    /// being added gets the same folder; the manager deletes it if the instance is not created.
    /// </summary>
    string InstanceDataFolder { get; }

    string? Settings { get; }

    CancellationToken Stopping { get; }

    IWidgetLogger Logger { get; }

    void Send(WidgetCommand command);
}
