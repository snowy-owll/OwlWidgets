using OwlWidgets.Common;
using OwlWidgets.Hosting;
using OwlWidgets.Services;
using OwlWidgets.UI.Common;

namespace OwlWidgets.ViewModels;

/// <summary>
/// Asks before an instance is deleted with its settings and log. The dialog only answers;
/// the widgets screen deletes after it is closed, when <see cref="Confirmed"/> is set.
/// </summary>
internal sealed class DeleteInstanceViewModel : ViewModelBase
{
    public DeleteInstanceViewModel(InstanceSession session, IDialogService dialogs)
    {
        Title = session.Tag == null ? session.Name : $"{session.Tag} · {session.Name}";
        DeleteCommand = new RelayCommand(() =>
        {
            Confirmed = true;
            dialogs.Close();
        });
        CancelCommand = new RelayCommand(dialogs.Close);
    }

    /// <summary>The instance as the list shows it, with the type when the tag hides it.</summary>
    public string Title { get; }

    public bool Confirmed { get; private set; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand CancelCommand { get; }
}
