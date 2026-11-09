using MaterialDesignThemes.Wpf;

namespace OwlWidgets.Services;

/// <summary>
/// Dialogs over the whole manager window. The content is a model; its view is picked by a data template.
/// </summary>
internal interface IDialogService
{
    /// <summary>Completes when the dialog is closed, in any way.</summary>
    Task ShowAsync(object content);

    /// <summary>Closes the open dialog; nothing happens when there is none.</summary>
    void Close();
}

internal sealed class DialogHostService : IDialogService
{
    /// <summary>The <c>DialogHost</c> of <c>MainWindow</c>.</summary>
    private const string Identifier = "RootDialog";

    public Task ShowAsync(object content) => DialogHost.Show(content, Identifier);

    public void Close()
    {
        if (DialogHost.IsDialogOpen(Identifier))
            DialogHost.Close(Identifier);
    }
}
