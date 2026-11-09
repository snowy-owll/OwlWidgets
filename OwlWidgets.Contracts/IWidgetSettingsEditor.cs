using System.Windows;

namespace OwlWidgets.Contracts;

public interface IWidgetSettingsEditor : IDisposable
{
    FrameworkElement View { get; }

    bool HasChanges { get; }

    event EventHandler? HasChangesChanged;

    /// <summary>
    /// The changes on the screen take effect only when the card starts again, such as another view of the card:
    /// after saving them the manager restarts a running instance instead of sending <c>apply-settings</c>.
    /// Read before <see cref="Accept"/>. The default implementation keeps existing widgets as they are.
    /// </summary>
    bool RequiresRestart => false;

    bool TrySerialize(out string settings);

    void Accept();

    void ReplaceSettings(string? settings);
}
