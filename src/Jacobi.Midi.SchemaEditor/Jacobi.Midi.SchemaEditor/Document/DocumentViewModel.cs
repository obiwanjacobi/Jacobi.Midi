using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Document;

/// <summary>
/// The view model of the rename document dialog: asks the user to confirm renaming a document.
/// When other open documents include the old name the user can choose to rename those includes as well;
/// when none do, the dialog warns that includes (in documents that are not open) may become invalid.
/// </summary>
internal sealed partial class DocumentViewModel : ViewModelBase
{
    private readonly Action<DocumentViewModel, bool> _close;

    /// <param name="oldName">The current name of the document.</param>
    /// <param name="newName">The name the document is renamed to.</param>
    /// <param name="dependents">The names of the other open documents that include <paramref name="oldName"/>.</param>
    /// <param name="close">Called with this view model and whether the user confirmed (true) or cancelled (false).</param>
    public DocumentViewModel(string oldName, string newName, IReadOnlyList<string> dependents, Action<DocumentViewModel, bool> close)
    {
        OldName = oldName;
        NewName = newName;
        Dependents = dependents;
        _close = close;
    }

    public string OldName { get; }

    public string NewName { get; }

    /// <summary>The names of the other open documents that include <see cref="OldName"/>.</summary>
    public IReadOnlyList<string> Dependents { get; }

    public bool HasDependents => Dependents.Count > 0;

    public bool HasNoDependents => !HasDependents;

    public string Title => $"Rename {OldName} to {NewName}";

    /// <summary>True to also rename the includes of <see cref="Dependents"/>.</summary>
    [ObservableProperty]
    public partial bool UpdateIncludes { get; set; } = true;

    [RelayCommand]
    private void Confirm() => _close(this, true);

    [RelayCommand]
    private void Cancel() => _close(this, false);
}
