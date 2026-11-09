using System.Windows;
using OwlWidgets.Contracts;
using OwlWidgets.Hosting;
using OwlWidgets.Services;
using OwlWidgets.Updates;

namespace OwlWidgets.Tests;

/// <summary>Runs <see cref="Invoke"/> at once and keeps <see cref="Post"/> until <see cref="RunPending"/>, as a dispatcher would.</summary>
internal sealed class FakeUi : IUiDispatcher
{
    private readonly Queue<Action> _pending = new();

    public void Invoke(Action action) => action();

    public void Post(Action action) => _pending.Enqueue(action);

    public void RunPending()
    {
        while (_pending.TryDequeue(out var action))
            action();
    }
}

internal sealed class FakeCatalog : IWidgetCatalog
{
    public IReadOnlyList<WidgetPackage> Packages { get; set; } = [];

    public void Load()
    {
    }
}

internal sealed class FakeDesktop : IDesktopLayer
{
    public event Action? DesktopRaised;

    public event Action<IntPtr>? WidgetPlacement;

    /// <summary>Win+D or "Show desktop".</summary>
    public void RaiseDesktop() => DesktopRaised?.Invoke();

    public void Place(IntPtr cover) => WidgetPlacement?.Invoke(cover);

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Register(IntPtr hwnd)
    {
    }

    public void Unregister(IntPtr hwnd)
    {
    }
}

/// <summary>
/// An instance that records the move mode commands. The wrapper's answers are played by the test
/// through <see cref="FakeMovables"/>.
/// </summary>
internal sealed class FakeMovable(string name, InstanceState state = InstanceState.Running) : IMovableInstance
{
    public string Name { get; } = name;

    public Guid InstanceId { get; } = Guid.NewGuid();

    public InstanceState State { get; set; } = state;

    /// <summary>The commands in the order they came: "enter", "confirm", "cancel-own", "restore".</summary>
    public List<string> Commands { get; } = [];

    public void EnterMoveMode(string initiator) => Commands.Add("enter");

    public void EndMoveMode(bool save) => Commands.Add(save ? "confirm" : "restore");

    public void CancelMoveMode() => Commands.Add("cancel-own");

    public override string ToString() => Name;
}

internal sealed class FakeMovables : IMovableInstances
{
    public FakeMovables(params FakeMovable[] instances) => Instances.AddRange(instances);

    public List<FakeMovable> Instances { get; } = [];

    public IEnumerable<IMovableInstance> Movable => Instances;

    public event Action<IMovableInstance>? InstanceStateChanged;

    public event Action<IMovableInstance, bool>? MoveModeEnded;

    public void ChangeState(FakeMovable instance, InstanceState state)
    {
        instance.State = state;
        InstanceStateChanged?.Invoke(instance);
    }

    /// <summary>The wrapper reports the end of its move mode: the check (saved) or the cross.</summary>
    public void EndMoveMode(FakeMovable instance, bool positionSaved) => MoveModeEnded?.Invoke(instance, positionSaved);
}

internal sealed class FakeNotifications : INotificationService
{
    public List<(string Title, string Message, Action? Clicked)> Shown { get; } = [];

    public void Show(string title, string message, Action? clicked = null) => Shown.Add((title, message, clicked));
}

/// <summary>Answers with <see cref="Latest"/> or throws <see cref="Failure"/>.</summary>
internal sealed class FakeReleaseSource : IReleaseSource
{
    public ReleaseInfo? Latest { get; set; }

    public Exception? Failure { get; set; }

    public int Requests { get; private set; }

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellation)
    {
        Requests++;
        return Failure is null ? Task.FromResult(Latest) : Task.FromException<ReleaseInfo?>(Failure);
    }
}

internal sealed class FakeShell : IShellService
{
    public List<string> OpenedFolders { get; } = [];

    public List<string> OpenedWebPages { get; } = [];

    public void OpenFolder(string path) => OpenedFolders.Add(path);

    public void OpenWebPage(string url) => OpenedWebPages.Add(url);
}

internal sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }

    public void SetText(string text) => Text = text;
}

internal sealed class FakeDialogs : IDialogService
{
    public List<object> Shown { get; } = [];

    public int CloseCount { get; private set; }

    /// <summary>The user's part while the dialog is open: presses a button of the shown model.</summary>
    public Action<object>? Answer { get; set; }

    public Task ShowAsync(object content)
    {
        Shown.Add(content);
        Answer?.Invoke(content);
        return Task.CompletedTask;
    }

    public void Close() => CloseCount++;
}

/// <summary>The value in Windows; <see cref="Fails"/>: every change fails and leaves it as it was.</summary>
internal sealed class FakeAutostart : IAutostartService
{
    public bool IsEnabled { get; set; }

    public bool Fails { get; set; }

    public bool SetEnabled(bool enabled)
    {
        if (Fails)
            return false;
        IsEnabled = enabled;
        return true;
    }
}

internal sealed class FakeLifetime : IAppLifetime
{
    public bool IsClosing => false;

    public void Close()
    {
    }

    public void Restart()
    {
    }
}

internal sealed class FakeWindow : IMainWindowService
{
    public int HideCount { get; private set; }

    public void Show()
    {
    }

    public void Hide() => HideCount++;

    public void ShowSettings(InstanceSession session)
    {
    }
}

/// <summary>
/// A settings screen without a view: the models never read it unless it is shown.
/// </summary>
internal sealed class FakeEditor : IWidgetSettingsEditor
{
    private bool _hasChanges;

    public FrameworkElement View => null!;

    /// <summary>Raises <see cref="HasChangesChanged"/>, as a screen does when the user edits it.</summary>
    public bool HasChanges
    {
        get => ThrowOnHasChanges ? throw new InvalidOperationException("HasChanges failed") : _hasChanges;
        set
        {
            _hasChanges = value;
            HasChangesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Settings { get; set; } = "{\"value\":1}";

    public bool SerializeSucceeds { get; set; } = true;

    public bool ThrowOnSerialize { get; set; }

    public bool ThrowOnHasChanges { get; set; }

    public bool ThrowOnDispose { get; set; }

    public int AcceptCount { get; private set; }

    /// <summary>The settings of the last <see cref="ReplaceSettings"/>; not set until it is called.</summary>
    public string? Replaced { get; private set; }

    public bool WasReplaced { get; private set; }

    public bool Disposed { get; private set; }

    public event EventHandler? HasChangesChanged;

    public bool NeedsRestart { get; set; }

    public bool ThrowOnRequiresRestart { get; set; }

    public bool RequiresRestart => ThrowOnRequiresRestart ? throw new InvalidOperationException("RequiresRestart failed") : NeedsRestart;

    public bool TrySerialize(out string settings)
    {
        if (ThrowOnSerialize)
            throw new InvalidOperationException("TrySerialize failed");
        settings = Settings;
        return SerializeSucceeds;
    }

    public void Accept()
    {
        AcceptCount++;
        HasChanges = false;
    }

    public void ReplaceSettings(string? settings)
    {
        Replaced = settings;
        WasReplaced = true;
        HasChanges = false;
    }

    public void Dispose()
    {
        Disposed = true;
        if (ThrowOnDispose)
            throw new InvalidOperationException("Dispose failed");
    }
}

internal sealed class FakeDescriptor : IWidgetDescriptor
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "Widget";

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = "1.0.0";

    public string Author { get; init; } = "Tests";

    public bool AllowMultipleInstances { get; init; } = true;

    /// <summary>null: the widget has no settings screen. A function that throws stands for a broken one.</summary>
    public Func<FakeEditor>? Editor { get; init; }

    public List<FakeEditor> Editors { get; } = [];

    /// <summary>What every screen was made for, in order; a screen not made is not listed.</summary>
    public List<WidgetInstanceInfo> EditedInstances { get; } = [];

    /// <summary>null: the default of the contract, no starter settings.</summary>
    public Func<CancellationToken, Task<string?>>? StarterSettings { get; init; }

    public Task<string?> CreateStarterSettingsAsync(IWidgetLogger log, CancellationToken token) =>
        StarterSettings?.Invoke(token) ?? Task.FromResult<string?>(null);

    public IWidgetSettingsEditor? CreateSettingsEditor(string? settings, WidgetInstanceInfo instance)
    {
        if (Editor == null)
            return null;
        var editor = Editor();
        Editors.Add(editor);
        EditedInstances.Add(instance);
        return editor;
    }

    public WidgetPackage ToPackage() => new()
    {
        Folder = Path.GetTempPath(),
        FolderName = Name,
        DescriptorAssemblyPath = "descriptor.dll",
        DescriptorTypeName = "Descriptor",
        RuntimeAssemblyPath = "runtime.dll",
        RuntimeTypeName = "Runtime",
        Descriptor = this
    };
}
