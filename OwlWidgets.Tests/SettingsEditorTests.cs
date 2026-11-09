using OwlWidgets.Hosting;
using OwlWidgets.ViewModels;
using Xunit;

namespace OwlWidgets.Tests;

/// <summary>
/// The settings screen of an instance through the real guards: a widget that fails must not take the manager down.
/// </summary>
public sealed class SettingsEditorTests : IDisposable
{
    private FakeEditor _editor = new();
    private readonly FakeDescriptor _descriptor;
    private readonly ManagerFixture _fixture;
    private readonly InstanceViewModel _instance;

    public SettingsEditorTests()
    {
        _descriptor = new FakeDescriptor { Editor = () => _editor };
        var package = _descriptor.ToPackage();
        _fixture = new ManagerFixture([package], ManagerFixture.Record(package));
        _instance = _fixture.CreateInstance(0);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void The_screen_is_made_on_first_selection()
    {
        Assert.False(_instance.HasSettings);

        _instance.EnsureEditor();
        _instance.EnsureEditor();

        Assert.True(_instance.HasSettings);
        Assert.Single(_descriptor.Editors);
    }

    [Fact]
    public void Saving_writes_the_file_and_then_accepts()
    {
        _instance.EnsureEditor();
        _editor.HasChanges = true;
        Assert.True(_instance.SaveCommand.CanExecute(null));

        _instance.SaveCommand.Execute(null);

        Assert.Equal(_editor.Settings, _instance.Session.ReadSettings());
        Assert.Equal(1, _editor.AcceptCount);
        Assert.False(_instance.CanSave);
        Assert.True(_instance.HasJson);
    }

    [Fact]
    public void An_incomplete_input_writes_nothing()
    {
        _editor.SerializeSucceeds = false;
        _instance.EnsureEditor();
        _editor.HasChanges = true;

        _instance.SaveCommand.Execute(null);

        Assert.Null(_instance.Session.ReadSettings());
        Assert.Equal(0, _editor.AcceptCount);
        Assert.True(_instance.CanSave);
    }

    [Fact]
    public void A_widget_failing_to_serialize_writes_nothing_and_does_not_throw()
    {
        _editor.ThrowOnSerialize = true;
        _instance.EnsureEditor();
        _editor.HasChanges = true;

        _instance.SaveCommand.Execute(null);

        Assert.Null(_instance.Session.ReadSettings());
        Assert.Equal(0, _editor.AcceptCount);
    }

    [Fact]
    public void Settings_that_need_a_restart_do_not_start_a_stopped_card()
    {
        _instance.EnsureEditor();
        _editor.NeedsRestart = true;
        _editor.HasChanges = true;

        _instance.SaveCommand.Execute(null);

        Assert.Equal(_editor.Settings, _instance.Session.ReadSettings());
        Assert.Equal(1, _editor.AcceptCount);
        Assert.Equal(InstanceState.Stopped, _instance.Session.State);
    }

    [Fact]
    public void A_widget_failing_to_say_whether_to_restart_still_saves()
    {
        _instance.EnsureEditor();
        _editor.ThrowOnRequiresRestart = true;
        _editor.HasChanges = true;

        _instance.SaveCommand.Execute(null);

        Assert.Equal(_editor.Settings, _instance.Session.ReadSettings());
        Assert.Equal(1, _editor.AcceptCount);
    }

    [Fact]
    public void A_widget_failing_to_report_changes_cannot_be_saved()
    {
        _instance.EnsureEditor();
        _editor.ThrowOnHasChanges = true;

        Assert.False(_instance.CanSave);
        Assert.False(_instance.SaveCommand.CanExecute(null));
        Assert.False(_instance.RevertCommand.CanExecute(null));
    }

    [Fact]
    public void Reverting_puts_the_file_back_on_the_screen()
    {
        _instance.EnsureEditor();
        _editor.HasChanges = true;
        _instance.SaveCommand.Execute(null);
        _editor.HasChanges = true;

        _instance.RevertCommand.Execute(null);

        Assert.True(_editor.WasReplaced);
        Assert.Equal(_editor.Settings, _editor.Replaced);
        Assert.False(_instance.CanSave);
    }

    [Fact]
    public void A_widget_failing_to_make_its_screen_has_no_settings_page()
    {
        _editor = null!;
        var broken = new FakeDescriptor { Editor = () => throw new InvalidOperationException("No screen") };
        var package = broken.ToPackage();
        using var fixture = new ManagerFixture([package], ManagerFixture.Record(package));
        var instance = fixture.CreateInstance(0);

        instance.EnsureEditor();

        Assert.False(instance.HasSettings);
        Assert.Null(instance.SettingsContent);
    }

    [Fact]
    public void Leaving_the_screen_releases_the_settings_screen_even_when_it_fails_to()
    {
        _editor.ThrowOnDispose = true;
        _instance.EnsureEditor();

        _instance.Detach();

        Assert.True(_editor.Disposed);
        Assert.False(_instance.HasSettings);
    }

    [Fact]
    public async Task Deleting_an_instance_releases_its_settings_screen()
    {
        var widgets = _fixture.CreateWidgets();
        Assert.Same(widgets.Instances[0], widgets.SelectedInstance);
        var editor = _descriptor.Editors.Last();
        _fixture.Dialogs.Answer = content => ((DeleteInstanceViewModel)content).DeleteCommand.Execute(null);

        await widgets.DeleteCommand.ExecuteAsync();

        Assert.True(editor.Disposed);
    }

    [Fact]
    public void The_exit_releases_every_settings_screen()
    {
        var widgets = _fixture.CreateWidgets();
        var editor = _descriptor.Editors.Last();

        widgets.Dispose();

        Assert.True(editor.Disposed);
    }
}
