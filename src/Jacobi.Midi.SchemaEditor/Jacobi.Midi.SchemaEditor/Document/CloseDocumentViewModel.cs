using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Document;

/// <summary>
/// The view model of the close document dialog: asks the user to confirm closing a document that has unsaved changes.
/// </summary>
internal sealed partial class CloseDocumentViewModel : ViewModelBase
{
    private readonly Action<CloseDocumentViewModel, bool> _close;

    /// <param name="document">The document to close.</param>
    /// <param name="close">Called with this view model and whether the user confirmed (true) or cancelled (false).</param>
    public CloseDocumentViewModel(SchemaWorkspaceDocument document, Action<CloseDocumentViewModel, bool> close)
    {
        Document = document;
        _close = close;
    }

    public SchemaWorkspaceDocument Document { get; }

    public string Title => $"Close {Document.Schema.Name}";

    [RelayCommand]
    private void Confirm() => _close(this, true);

    [RelayCommand]
    private void Cancel() => _close(this, false);
}
