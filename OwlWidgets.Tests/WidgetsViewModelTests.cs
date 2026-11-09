using Microsoft.Extensions.Logging.Abstractions;
using OwlWidgets.Hosting;
using OwlWidgets.Properties;
using OwlWidgets.ViewModels;
using Xunit;

namespace OwlWidgets.Tests;

public sealed class WidgetsViewModelTests : IDisposable
{
    private readonly FakeDescriptor _plain = new() { Name = "Plain" };
    private readonly FakeDescriptor _withSettings = new() { Name = "With settings", Editor = () => new FakeEditor() };
    private ManagerFixture? _fixture;

    public void Dispose() => _fixture?.Dispose();

    private WidgetsViewModel Create(int plainInstances, int instancesWithSettings = 0)
    {
        var plain = _plain.ToPackage();
        var withSettings = _withSettings.ToPackage();
        var records = Enumerable.Range(0, plainInstances).Select(_ => ManagerFixture.Record(plain))
            .Concat(Enumerable.Range(0, instancesWithSettings).Select(_ => ManagerFixture.Record(withSettings)))
            .ToArray();
        _fixture = new ManagerFixture([plain, withSettings], records);
        // The deletion dialog is confirmed unless a test answers otherwise.
        _fixture.Dialogs.Answer = content =>
        {
            if (content is DeleteInstanceViewModel delete)
                delete.DeleteCommand.Execute(null);
        };
        return _fixture.CreateWidgets();
    }

    [Fact]
    public void The_first_instance_is_selected_at_start()
    {
        var widgets = Create(plainInstances: 3);

        Assert.Same(widgets.Instances[0], widgets.SelectedInstance);
        Assert.True(widgets.HasSelection);
    }

    [Fact]
    public async Task Deleting_asks_first_and_deletes_after_the_confirmation()
    {
        var widgets = Create(plainInstances: 2);
        var deleted = widgets.Instances[0].Session;

        await widgets.DeleteCommand.ExecuteAsync();

        var dialog = Assert.IsType<DeleteInstanceViewModel>(Assert.Single(_fixture!.Dialogs.Shown));
        Assert.True(dialog.Confirmed);
        Assert.Equal(1, _fixture.Dialogs.CloseCount);
        Assert.DoesNotContain(deleted, _fixture.Manager.Instances);
        Assert.Single(widgets.Instances);
    }

    [Fact]
    public async Task Deleting_an_instance_deletes_its_files_and_keeps_those_of_others()
    {
        var widgets = Create(plainInstances: 2);
        var deleted = widgets.Instances[0].Session.InstanceId;
        var kept = widgets.Instances[1].Session.InstanceId;
        var deletedFile = WriteFile(deleted);
        var keptFile = WriteFile(kept);

        await widgets.DeleteCommand.ExecuteAsync();

        Assert.False(Directory.Exists(_fixture!.Storage.Folder(deleted)));
        Assert.False(File.Exists(deletedFile));
        Assert.True(File.Exists(keptFile));
    }

    private string WriteFile(Guid instanceId)
    {
        var folder = Path.Combine(_fixture!.Storage.Folder(instanceId), "icons");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "01d.png");
        File.WriteAllText(file, "png");
        return file;
    }

    [Fact]
    public async Task Cancelling_the_deletion_keeps_the_instance_and_the_selection()
    {
        var widgets = Create(plainInstances: 2);
        var selected = widgets.Instances[1];
        widgets.SelectedInstance = selected;
        _fixture!.Dialogs.Answer = content => ((DeleteInstanceViewModel)content).CancelCommand.Execute(null);

        await widgets.DeleteCommand.ExecuteAsync();
        _fixture.Ui.RunPending();

        Assert.False(((DeleteInstanceViewModel)Assert.Single(_fixture.Dialogs.Shown)).Confirmed);
        Assert.Equal(2, widgets.Instances.Count);
        Assert.Contains(selected.Session, _fixture.Manager.Instances);
        Assert.Same(selected, widgets.SelectedInstance);
    }

    [Fact]
    public async Task The_deletion_dialog_shows_the_type_when_the_tag_hides_it()
    {
        var widgets = Create(plainInstances: 1);
        widgets.Instances[0].Session.Tag = "Kitchen";
        _fixture!.Dialogs.Answer = null;

        await widgets.DeleteCommand.ExecuteAsync();

        Assert.Equal("Kitchen · Plain", ((DeleteInstanceViewModel)Assert.Single(_fixture.Dialogs.Shown)).Title);
    }

    [Fact]
    public async Task Deleting_selects_the_instance_that_took_its_place()
    {
        var widgets = Create(plainInstances: 3);
        var next = widgets.Instances[2];
        widgets.SelectedInstance = widgets.Instances[1];

        await widgets.DeleteCommand.ExecuteAsync();
        _fixture!.Ui.RunPending();

        Assert.Equal(2, widgets.Instances.Count);
        Assert.Same(next, widgets.SelectedInstance);
    }

    [Fact]
    public async Task Deleting_the_last_instance_selects_the_new_last()
    {
        var widgets = Create(plainInstances: 3);
        var previous = widgets.Instances[1];
        widgets.SelectedInstance = widgets.Instances[2];

        await widgets.DeleteCommand.ExecuteAsync();
        _fixture!.Ui.RunPending();

        Assert.Same(previous, widgets.SelectedInstance);
    }

    [Fact]
    public async Task Deleting_the_only_instance_leaves_nothing_selected()
    {
        var widgets = Create(plainInstances: 1);

        await widgets.DeleteCommand.ExecuteAsync();
        _fixture!.Ui.RunPending();

        Assert.Null(widgets.SelectedInstance);
        Assert.False(widgets.HasSelection);
        Assert.False(widgets.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void The_widget_page_falls_back_to_general_for_an_instance_without_a_settings_screen()
    {
        var widgets = Create(plainInstances: 1, instancesWithSettings: 1);
        widgets.SelectedInstance = widgets.Instances[1];
        widgets.SelectedPage = InstancePage.Widget;

        widgets.SelectedInstance = widgets.Instances[0];

        Assert.Equal(InstancePage.General, widgets.SelectedPage);
    }

    [Fact]
    public void Other_pages_are_kept_when_the_selection_changes()
    {
        var widgets = Create(plainInstances: 2);
        widgets.SelectedPage = InstancePage.Json;

        widgets.SelectedInstance = widgets.Instances[1];

        Assert.Equal(InstancePage.Json, widgets.SelectedPage);
    }

    [Fact]
    public void Settings_requested_by_a_widget_open_its_own_screen_when_it_has_one()
    {
        var widgets = Create(plainInstances: 1, instancesWithSettings: 1);

        widgets.ShowSettings(_fixture!.Session(1));

        Assert.Same(widgets.Instances[1], widgets.SelectedInstance);
        Assert.Equal(InstancePage.Widget, widgets.SelectedPage);
    }

    [Fact]
    public void Settings_requested_by_a_widget_without_a_screen_open_the_general_page()
    {
        var widgets = Create(plainInstances: 2);
        widgets.SelectedPage = InstancePage.Json;

        widgets.ShowSettings(_fixture!.Session(1));

        Assert.Same(widgets.Instances[1], widgets.SelectedInstance);
        Assert.Equal(InstancePage.General, widgets.SelectedPage);
    }

    [Fact]
    public void Nothing_to_add_when_every_single_instance_type_exists()
    {
        var single = new FakeDescriptor { AllowMultipleInstances = false };
        var package = single.ToPackage();
        _fixture = new ManagerFixture([package], ManagerFixture.Record(package));
        var widgets = _fixture.CreateWidgets();

        Assert.False(widgets.AddCommand.CanExecute(null));
        Assert.Equal(Strings.Widgets_AddNothing, widgets.AddToolTip);
    }

    [Fact]
    public void The_mass_move_button_hides_the_window_and_then_cancels()
    {
        Create(plainInstances: 1);
        using var massMove = RunningMassMove();
        var widgets = _fixture!.CreateWidgets(massMove);
        Assert.True(widgets.MoveAllCommand.CanExecute(null));
        Assert.Equal(Strings.MoveAll_Start, widgets.MoveAllToolTip);

        widgets.MoveAllCommand.Execute(null);

        Assert.True(widgets.MassMoveActive);
        Assert.Equal(1, _fixture.Window.HideCount);
        Assert.Equal(Strings.MoveAll_Cancel, widgets.MoveAllToolTip);

        widgets.MoveAllCommand.Execute(null);

        Assert.False(widgets.MassMoveActive);
        Assert.Equal(1, _fixture.Window.HideCount);
        Assert.Equal(Strings.MoveAll_Start, widgets.MoveAllToolTip);
    }

    [Fact]
    public void The_mass_move_button_follows_a_mass_move_started_elsewhere()
    {
        Create(plainInstances: 1);
        using var massMove = RunningMassMove();
        var widgets = _fixture!.CreateWidgets(massMove);
        var changed = new List<string?>();
        widgets.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        massMove.Enter("the tray menu");

        Assert.Contains(nameof(WidgetsViewModel.MassMoveActive), changed);
        Assert.Contains(nameof(WidgetsViewModel.MoveAllToolTip), changed);
        Assert.True(widgets.MassMoveActive);
        Assert.Equal(0, _fixture.Window.HideCount);
    }

    private MassMoveCoordinator RunningMassMove() =>
        new(new FakeMovables(new FakeMovable("card")), _fixture!.Desktop, NullLogger<MassMoveCoordinator>.Instance);

    [Fact]
    public void Mass_move_cannot_start_without_a_running_instance()
    {
        var widgets = Create(plainInstances: 2);

        Assert.False(widgets.MoveAllCommand.CanExecute(null));
        Assert.False(_fixture!.MassMove.Enter("the tests"));
        Assert.False(widgets.MassMoveActive);
        Assert.Equal(Strings.MoveAll_Nothing, widgets.MoveAllToolTip);
    }
}
