using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Extensions.Logging;
using OwlWidgets.Common;
using OwlWidgets.Hosting;
using OwlWidgets.Properties;
using OwlWidgets.Services;
using OwlWidgets.UI.Common;

namespace OwlWidgets.ViewModels;

/// <summary>
/// The pages of the selected instance. Public: the tab items take their values by <c>x:Static</c>.
/// </summary>
public enum InstancePage
{
    General,
    Widget,
    Json
}

/// <summary>
/// The widgets screen: the instance list, the selected instance and its page, adding and the mass move.
/// </summary>
internal sealed class WidgetsViewModel : ViewModelBase, IDisposable
{
    private readonly InstanceManager _instances;
    private readonly MassMoveCoordinator _massMove;
    private readonly IDialogService _dialogs;
    private readonly IMainWindowService _window;
    private readonly IUiDispatcher _ui;
    private readonly ISettingsEditorFactory _editors;
    private readonly ILogger<AddWidgetViewModel> _addLog;
    private int _selectedIndex;

    public WidgetsViewModel(InstanceManager instances, MassMoveCoordinator massMove, IDialogService dialogs,
        IMainWindowService window, IUiDispatcher ui, ISettingsEditorFactory editors, ILogger<AddWidgetViewModel> addLog)
    {
        _instances = instances;
        _massMove = massMove;
        _dialogs = dialogs;
        _window = window;
        _ui = ui;
        _editors = editors;
        _addLog = addLog;

        AddCommand = new AsyncRelayCommand(AddAsync, () => CanAdd);
        MoveAllCommand = new RelayCommand(ToggleMassMove, () => _massMove.Active || _massMove.CanStart);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedInstance != null);
        EditTagCommand = new AsyncRelayCommand(EditTagAsync, () => SelectedInstance != null);

        foreach (var session in instances.Instances)
            Instances.Add(new InstanceViewModel(session, ui, editors));
        instances.Instances.CollectionChanged += OnInstancesChanged;
        massMove.Changed += OnMassMoveChanged;
        SelectedInstance = Instances.FirstOrDefault();
    }

    public ObservableCollection<InstanceViewModel> Instances { get; } = [];

    public AsyncRelayCommand AddCommand { get; }

    public RelayCommand MoveAllCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand EditTagCommand { get; }

    /// <summary>
    /// One instance stays selected while the list is not empty. After a deletion the item
    /// that took the place of the deleted one is selected, or the last one.
    /// </summary>
    public InstanceViewModel? SelectedInstance
    {
        get => GetValue(() => SelectedInstance);
        set
        {
            if (value == SelectedInstance)
                return;
            SetValue(() => SelectedInstance, value);
            if (value != null)
            {
                _selectedIndex = Instances.IndexOf(value);
                value.EnsureEditor();
                if (SelectedPage == InstancePage.Widget && !value.HasSettings)
                    SelectedPage = InstancePage.General;
            }
            else if (Instances.Count > 0)
            {
                // The list cleared its selection; a new value is not taken while it is updating.
                _ui.Post(EnsureSelection);
            }

            OnPropertyChanged(nameof(HasSelection));
            DeleteCommand.RaiseCanExecuteChanged();
            EditTagCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedInstance != null;

    /// <summary>Kept when another instance is selected; the widget page falls back to the general one.</summary>
    public InstancePage SelectedPage
    {
        get => GetValue(() => SelectedPage);
        set => SetValue(() => SelectedPage, value);
    }

    public bool CanAdd => _instances.Packages.Any(_instances.CanAdd);

    public string AddToolTip => CanAdd ? Strings.Widgets_Add : Strings.Widgets_AddNothing;

    public bool MassMoveActive => _massMove.Active;

    public string MoveAllToolTip => _massMove.Active ? Strings.MoveAll_Cancel
        : _massMove.CanStart ? Strings.MoveAll_Start
        : Strings.MoveAll_Nothing;

    /// <summary>
    /// Selects the instance and opens the widget's own settings page, or the general page when there is none.
    /// The page is chosen after the selection: selecting creates the settings screen.
    /// </summary>
    public void ShowSettings(InstanceSession session)
    {
        var instance = Find(session);
        if (instance == null)
            return;
        SelectedInstance = instance;
        SelectedPage = instance.HasSettings ? InstancePage.Widget : InstancePage.General;
    }

    /// <summary>
    /// The cards lie below regular windows, so the manager window gets out of the way when the mass move starts.
    /// </summary>
    private void ToggleMassMove()
    {
        if (_massMove.Active)
            _massMove.Cancel("the widgets screen");
        else if (_massMove.Enter("the widgets screen"))
            _window.Hide();
    }

    private async Task AddAsync()
    {
        var dialog = new AddWidgetViewModel(_instances, _dialogs, _editors, _addLog);
        try
        {
            await _dialogs.ShowAsync(dialog);
        }
        finally
        {
            dialog.Dispose();
        }

        if (dialog.Created != null)
            SelectedInstance = Find(dialog.Created);
    }

    private async Task EditTagAsync()
    {
        if (SelectedInstance != null)
            await _dialogs.ShowAsync(new TagViewModel(SelectedInstance.Session, _dialogs));
    }

    /// <summary>
    /// Deletes after the user confirms. The instance is taken before the dialog: the selection may change
    /// while it is open, and an instance deleted meanwhile is not deleted twice.
    /// </summary>
    private async Task DeleteAsync()
    {
        var session = SelectedInstance?.Session;
        if (session == null)
            return;
        var dialog = new DeleteInstanceViewModel(session, _dialogs);
        await _dialogs.ShowAsync(dialog);
        if (dialog.Confirmed && _instances.Instances.Contains(session))
            await _instances.DeleteAsync(session);
    }

    private void EnsureSelection()
    {
        if (SelectedInstance != null || Instances.Count == 0)
            return;
        SelectedInstance = Instances[Math.Clamp(_selectedIndex, 0, Instances.Count - 1)];
    }

    private InstanceViewModel? Find(InstanceSession session) =>
        Instances.FirstOrDefault(instance => instance.Session == session);

    private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                var index = e.NewStartingIndex;
                foreach (InstanceSession session in e.NewItems)
                {
                    var instance = new InstanceViewModel(session, _ui, _editors);
                    if (index >= 0 && index <= Instances.Count)
                        Instances.Insert(index++, instance);
                    else
                        Instances.Add(instance);
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                foreach (InstanceSession session in e.OldItems)
                    Remove(session);
                break;
            default:
                foreach (var instance in Instances)
                    instance.Detach();
                Instances.Clear();
                foreach (var session in _instances.Instances)
                    Instances.Add(new InstanceViewModel(session, _ui, _editors));
                break;
        }

        EnsureSelection();
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(AddToolTip));
        AddCommand.RaiseCanExecuteChanged();
    }

    private void Remove(InstanceSession session)
    {
        var instance = Find(session);
        if (instance == null)
            return;
        instance.Detach();
        var wasSelected = instance == SelectedInstance;
        Instances.Remove(instance);
        if (wasSelected)
            SelectedInstance = null;
    }

    /// <summary>The manager is exiting: the settings screens of the widgets are released.</summary>
    public void Dispose()
    {
        _instances.Instances.CollectionChanged -= OnInstancesChanged;
        _massMove.Changed -= OnMassMoveChanged;
        foreach (var instance in Instances)
            instance.Detach();
    }

    private void OnMassMoveChanged()
    {
        OnPropertyChanged(nameof(MassMoveActive));
        OnPropertyChanged(nameof(MoveAllToolTip));
        MoveAllCommand.RaiseCanExecuteChanged();
    }
}
