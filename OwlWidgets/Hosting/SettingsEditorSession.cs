using Microsoft.Extensions.Logging;
using OwlWidgets.Contracts;
using OwlWidgets.Services;

namespace OwlWidgets.Hosting;

/// <summary>
/// The settings screen of one instance, as the manager uses it. Every call into the widget is guarded:
/// a failure is logged and taken as "nothing happened", so a broken screen does not reach the dispatcher
/// and close the manager. Handlers inside the screen itself are not covered: they run outside these calls.
/// </summary>
internal interface ISettingsEditorSession : IDisposable
{
    /// <summary>The widget's view, shown as it is.</summary>
    object Content { get; }

    /// <summary>The screen differs from the last accepted settings; false when the widget fails to say.</summary>
    bool HasChanges { get; }

    event EventHandler? HasChangesChanged;

    /// <summary>The changes take effect only after the card restarts; false when the widget fails to say.</summary>
    bool RequiresRestart { get; }

    /// <summary>
    /// false: the input is incomplete and the screen shows why, or the widget failed; nothing is to be written.
    /// </summary>
    bool TryGetSettings(out string settings);

    /// <summary>The settings were written: the screen takes them as its new snapshot.</summary>
    void Accept();

    /// <summary>The screen shows these settings instead, dropping unsaved changes.</summary>
    void Replace(string? settings);
}

internal interface ISettingsEditorFactory
{
    /// <summary>
    /// The settings screen of an instance, existing or about to be created. null: the widget has none,
    /// or it failed to make one (logged to <paramref name="log"/>).
    /// </summary>
    ISettingsEditorSession? Create(WidgetPackage package, Guid instanceId, string? settings, ILogger log);
}

internal sealed class WidgetSettingsEditorFactory : ISettingsEditorFactory
{
    private readonly IInstanceStorage _storage;

    public WidgetSettingsEditorFactory(IInstanceStorage storage) => _storage = storage;

    public ISettingsEditorSession? Create(WidgetPackage package, Guid instanceId, string? settings, ILogger log)
    {
        IWidgetSettingsEditor? editor;
        try
        {
            editor = package.Descriptor.CreateSettingsEditor(settings, new WidgetInstanceInfo
            {
                InstanceId = instanceId,
                ContentRoot = package.Folder,
                InstanceDataFolder = _storage.Folder(instanceId)
            });
        }
        catch (Exception exception)
        {
            log.LogError(exception, "Settings editor of {Widget} was not created", package.Descriptor.Name);
            return null;
        }

        if (editor == null)
            return null;

        try
        {
            return new WidgetSettingsEditorSession(editor, package.Descriptor.Name, log);
        }
        catch (Exception exception)
        {
            log.LogError(exception, "Settings editor of {Widget} failed to give its view", package.Descriptor.Name);
            try
            {
                editor.Dispose();
            }
            catch (Exception disposeException)
            {
                log.LogWarning(disposeException, "Settings editor of {Widget} failed to dispose", package.Descriptor.Name);
            }

            return null;
        }
    }
}

internal sealed class WidgetSettingsEditorSession : ISettingsEditorSession
{
    private readonly IWidgetSettingsEditor _editor;
    private readonly string _widget;
    private readonly ILogger _log;
    private bool _hasChangesFailed;
    private bool _disposed;

    /// <summary>Reads the view and subscribes at once: a failure here means the widget has no usable screen.</summary>
    public WidgetSettingsEditorSession(IWidgetSettingsEditor editor, string widget, ILogger log)
    {
        _editor = editor;
        _widget = widget;
        _log = log;
        Content = editor.View;
        editor.HasChangesChanged += OnHasChangesChanged;
    }

    public event EventHandler? HasChangesChanged;

    public object Content { get; }

    /// <summary>Read on every check of the save button, so a failure is logged once.</summary>
    public bool HasChanges
    {
        get
        {
            try
            {
                return _editor.HasChanges;
            }
            catch (Exception exception)
            {
                if (!_hasChangesFailed)
                    _log.LogError(exception, "Settings editor of {Widget} failed to report changes", _widget);
                _hasChangesFailed = true;
                return false;
            }
        }
    }

    /// <summary>Read once per save: the settings are still saved and sent as they are when the widget fails.</summary>
    public bool RequiresRestart
    {
        get
        {
            try
            {
                return _editor.RequiresRestart;
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "Settings editor of {Widget} failed to say whether a restart is needed", _widget);
                return false;
            }
        }
    }

    public bool TryGetSettings(out string settings)
    {
        try
        {
            return _editor.TrySerialize(out settings);
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Settings editor of {Widget} failed to serialize, nothing is saved", _widget);
            settings = string.Empty;
            return false;
        }
    }

    public void Accept() => Guard(_editor.Accept, "accept the saved settings");

    public void Replace(string? settings) => Guard(() => _editor.ReplaceSettings(settings), "show the stored settings");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Guard(() =>
        {
            _editor.HasChangesChanged -= OnHasChangesChanged;
            _editor.Dispose();
        }, "dispose");
    }

    private void OnHasChangesChanged(object? sender, EventArgs e) => HasChangesChanged?.Invoke(this, EventArgs.Empty);

    private void Guard(Action call, string action)
    {
        try
        {
            call();
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Settings editor of {Widget} failed to {Action}", _widget, action);
        }
    }
}
