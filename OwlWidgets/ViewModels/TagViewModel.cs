using OwlWidgets.Common;
using OwlWidgets.Hosting;
using OwlWidgets.Services;
using OwlWidgets.UI.Common;

namespace OwlWidgets.ViewModels;

/// <summary>
/// Changing the tag of an existing instance. An empty field removes the tag.
/// </summary>
internal sealed class TagViewModel : ViewModelBase
{
    public TagViewModel(InstanceSession session, IDialogService dialogs)
    {
        TypeName = session.Name;
        Tag = session.Tag ?? string.Empty;
        SaveCommand = new RelayCommand(() =>
        {
            session.Tag = Tag;
            dialogs.Close();
        });
        CancelCommand = new RelayCommand(dialogs.Close);
    }

    public string TypeName { get; }

    public string? Tag
    {
        get => GetValue(() => Tag);
        set => SetValue(() => Tag, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }
}
