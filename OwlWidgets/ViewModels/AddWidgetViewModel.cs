using Microsoft.Extensions.Logging;
using OwlWidgets.Common;
using OwlWidgets.Hosting;
using OwlWidgets.Properties;
using OwlWidgets.Services;
using OwlWidgets.UI.Common;

namespace OwlWidgets.ViewModels;

/// <summary>
/// Creating an instance in two steps: the widget type, then its settings screen if it has one.
/// </summary>
internal sealed class AddWidgetViewModel : ViewModelBase, IDisposable
{
    private readonly InstanceManager _instances;
    private readonly IDialogService _dialogs;
    private readonly ISettingsEditorFactory _editors;
    private readonly ILogger _log;
    private readonly Guid _instanceId = Guid.NewGuid();
    private ISettingsEditorSession? _editor;

    public AddWidgetViewModel(InstanceManager instances, IDialogService dialogs, ISettingsEditorFactory editors,
        ILogger log)
    {
        _instances = instances;
        _dialogs = dialogs;
        _editors = editors;
        _log = log;
        Packages = instances.AddablePackages;
        PrimaryCommand = new RelayCommand(Next, () => SelectedPackage != null);
        BackCommand = new RelayCommand(() => SettingsStep = false);
        CancelCommand = new RelayCommand(Cancel);
        SelectedPackage = Packages.FirstOrDefault();
    }

    public IReadOnlyList<WidgetPackage> Packages { get; }

    public RelayCommand PrimaryCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand CancelCommand { get; }

    /// <summary>
    /// The settings screen is made for the selected type at once: the primary button
    /// says whether there is a second step.
    /// </summary>
    public WidgetPackage? SelectedPackage
    {
        get => GetValue(() => SelectedPackage);
        set
        {
            if (value == SelectedPackage)
                return;
            DisposeEditor();
            // Files the screen of the previous type wrote for the new instance are not for this one.
            _instances.DiscardFiles(_instanceId);
            SetValue(() => SelectedPackage, value);
            if (value != null)
                _editor = _editors.Create(value, _instanceId, settings: null, _log);
            OnStepChanged();
            PrimaryCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The second step: the widget's settings screen.</summary>
    public bool SettingsStep
    {
        get => GetValue(() => SettingsStep);
        private set
        {
            SetValue(() => SettingsStep, value);
            OnStepChanged();
        }
    }

    public bool ChooseStep => !SettingsStep;

    public object? EditorContent => SettingsStep ? _editor?.Content : null;

    public string StepText => SettingsStep
        ? string.Format(Strings.AddWidget_SettingsStep, SelectedPackage?.Descriptor.Name)
        : Strings.AddWidget_ChooseStep;

    public string PrimaryText => !SettingsStep && _editor != null ? Strings.AddWidget_Next : Strings.AddWidget_Create;

    /// <summary>The tag of the new instance; empty: no tag.</summary>
    public string? Tag
    {
        get => GetValue(() => Tag);
        set => SetValue(() => Tag, value);
    }

    /// <summary>The created instance; null when the dialog was cancelled.</summary>
    public InstanceSession? Created { get; private set; }

    /// <summary>
    /// The editor of a dialog closed in any way is released; without a created instance,
    /// the files its screen wrote are deleted.
    /// </summary>
    public void Dispose()
    {
        DisposeEditor();
        if (Created == null)
            _instances.DiscardFiles(_instanceId);
    }

    private void Next()
    {
        if (SelectedPackage == null)
            return;
        if (!SettingsStep && _editor != null)
        {
            SettingsStep = true;
            return;
        }

        Create();
    }

    /// <summary>
    /// Untouched settings are not written: the instance starts without a file, on the widget defaults.
    /// An input error stays on the screen, the editor shows it. The screen accepts the settings before it is
    /// released, as after a save: it keeps the files they refer to.
    /// </summary>
    private void Create()
    {
        if (SelectedPackage == null || !_instances.CanAdd(SelectedPackage))
            return;
        string? settings = null;
        if (_editor != null)
        {
            if (!_editor.TryGetSettings(out var serialized))
                return;
            if (_editor.HasChanges)
                settings = serialized;
            _editor.Accept();
        }

        DisposeEditor();
        Created = _instances.Add(SelectedPackage, _instanceId, settings, Tag);
        _dialogs.Close();
    }

    private void Cancel()
    {
        DisposeEditor();
        _instances.DiscardFiles(_instanceId);
        _dialogs.Close();
    }

    private void OnStepChanged()
    {
        OnPropertyChanged(nameof(ChooseStep));
        OnPropertyChanged(nameof(EditorContent));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(PrimaryText));
    }

    /// <summary>The screen leaves the dialog before the editor is disposed.</summary>
    private void DisposeEditor()
    {
        var editor = _editor;
        if (editor == null)
            return;
        _editor = null;
        OnPropertyChanged(nameof(EditorContent));
        editor.Dispose();
    }
}
