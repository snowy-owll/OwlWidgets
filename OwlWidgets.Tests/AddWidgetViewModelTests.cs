using OwlWidgets.Properties;
using OwlWidgets.ViewModels;
using Xunit;

namespace OwlWidgets.Tests;

public sealed class AddWidgetViewModelTests : IDisposable
{
    private readonly FakeDescriptor _withSettings;
    private readonly FakeDescriptor _plain = new() { Name = "B plain" };
    private readonly ManagerFixture _fixture;
    private FakeEditor? _nextEditor;

    public AddWidgetViewModelTests()
    {
        _withSettings = new FakeDescriptor { Name = "A with settings", Editor = () => _nextEditor ?? new FakeEditor() };
        _fixture = new ManagerFixture([_plain.ToPackage(), _withSettings.ToPackage()]);
    }

    public void Dispose() => _fixture.Dispose();

    private AddWidgetViewModel Create() => _fixture.CreateAddDialog();

    [Fact]
    public void Types_are_listed_by_name_and_the_first_is_selected()
    {
        var dialog = Create();

        Assert.Equal(["A with settings", "B plain"], dialog.Packages.Select(package => package.Descriptor.Name));
        Assert.Same(dialog.Packages[0], dialog.SelectedPackage);
    }

    [Fact]
    public void A_type_with_a_settings_screen_has_a_second_step()
    {
        var dialog = Create();

        Assert.Equal(Strings.AddWidget_Next, dialog.PrimaryText);
        dialog.PrimaryCommand.Execute(null);

        Assert.True(dialog.SettingsStep);
        Assert.False(dialog.ChooseStep);
        Assert.Equal(Strings.AddWidget_Create, dialog.PrimaryText);
        Assert.Equal(0, _fixture.Dialogs.CloseCount);

        dialog.BackCommand.Execute(null);

        Assert.False(dialog.SettingsStep);
        Assert.Equal(Strings.AddWidget_Next, dialog.PrimaryText);
    }

    [Fact]
    public void A_type_without_a_settings_screen_is_created_at_once()
    {
        var dialog = Create();

        dialog.SelectedPackage = dialog.Packages[1];

        Assert.Equal(Strings.AddWidget_Create, dialog.PrimaryText);
    }

    [Fact]
    public void Choosing_another_type_releases_the_settings_screen()
    {
        var dialog = Create();
        var editor = _withSettings.Editors.Single();

        dialog.SelectedPackage = dialog.Packages[1];

        Assert.True(editor.Disposed);
    }

    [Fact]
    public void An_input_error_keeps_the_dialog_open()
    {
        _nextEditor = new FakeEditor { SerializeSucceeds = false };
        var dialog = Create();
        dialog.PrimaryCommand.Execute(null);

        dialog.PrimaryCommand.Execute(null);

        Assert.Null(dialog.Created);
        Assert.Equal(0, _fixture.Dialogs.CloseCount);
        Assert.False(_nextEditor.Disposed);
        Assert.Empty(_fixture.Manager.Instances);
    }

    [Fact]
    public void A_widget_failing_to_serialize_keeps_the_dialog_open()
    {
        _nextEditor = new FakeEditor { ThrowOnSerialize = true };
        var dialog = Create();
        dialog.PrimaryCommand.Execute(null);

        dialog.PrimaryCommand.Execute(null);

        Assert.Null(dialog.Created);
        Assert.Equal(0, _fixture.Dialogs.CloseCount);
        Assert.Empty(_fixture.Manager.Instances);
    }

    [Fact]
    public void Cancelling_releases_the_settings_screen_and_closes()
    {
        var dialog = Create();
        var editor = _withSettings.Editors.Single();

        dialog.CancelCommand.Execute(null);

        Assert.True(editor.Disposed);
        Assert.Equal(1, _fixture.Dialogs.CloseCount);
        Assert.Null(dialog.Created);
    }

    [Fact]
    public void Disposing_releases_the_settings_screen_of_a_dialog_closed_otherwise()
    {
        var dialog = Create();
        var editor = _withSettings.Editors.Single();

        dialog.Dispose();

        Assert.True(editor.Disposed);
    }

    [Fact]
    public void The_settings_screen_gets_the_files_folder_of_the_new_instance()
    {
        Create();

        var instance = _withSettings.EditedInstances.Single();
        Assert.Equal(_fixture.Paths.InstanceDataFolder(instance.InstanceId), instance.InstanceDataFolder);
        Assert.Equal(Path.Combine(_fixture.Paths.DataRoot, "storage", instance.InstanceId.ToString("N")),
            instance.InstanceDataFolder);
    }

    [Fact]
    public void Creating_accepts_the_screen_and_keeps_the_files_it_wrote()
    {
        _nextEditor = new FakeEditor { HasChanges = true };
        var dialog = Create();
        var file = WriteFile(_withSettings.EditedInstances.Single());
        dialog.PrimaryCommand.Execute(null);

        dialog.PrimaryCommand.Execute(null);
        dialog.Dispose();

        Assert.NotNull(dialog.Created);
        Assert.Equal(1, _nextEditor.AcceptCount);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Cancelling_deletes_the_files_the_screen_wrote()
    {
        var dialog = Create();
        var instance = _withSettings.EditedInstances.Single();
        WriteFile(instance);

        dialog.CancelCommand.Execute(null);

        Assert.False(Directory.Exists(instance.InstanceDataFolder));
        Assert.Equal(0, _withSettings.Editors.Single().AcceptCount);
    }

    [Fact]
    public void A_dialog_closed_otherwise_deletes_the_files_the_screen_wrote()
    {
        var dialog = Create();
        var instance = _withSettings.EditedInstances.Single();
        WriteFile(instance);

        dialog.Dispose();

        Assert.False(Directory.Exists(instance.InstanceDataFolder));
    }

    [Fact]
    public void Choosing_another_type_deletes_the_files_the_previous_screen_wrote()
    {
        var dialog = Create();
        var instance = _withSettings.EditedInstances.Single();
        WriteFile(instance);

        dialog.SelectedPackage = dialog.Packages[1];

        Assert.False(Directory.Exists(instance.InstanceDataFolder));
    }

    private static string WriteFile(OwlWidgets.Contracts.WidgetInstanceInfo instance)
    {
        Directory.CreateDirectory(instance.InstanceDataFolder);
        var file = Path.Combine(instance.InstanceDataFolder, "pack.txt");
        File.WriteAllText(file, "files of the new instance");
        return file;
    }
}
