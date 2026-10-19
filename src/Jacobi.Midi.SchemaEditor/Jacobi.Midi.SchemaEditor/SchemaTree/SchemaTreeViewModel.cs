using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// View model behind <see cref="SchemaTreeControl"/>.
/// Exposes the document whose members the tree shows and the selected node.
/// The tree binds directly to the (observable) <see cref="EditNode"/> model.
/// </summary>
internal sealed partial class SchemaTreeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial EditDocument? Document { get; set; }

    [ObservableProperty]
    public partial EditNode? SelectedNode { get; set; }

    partial void OnDocumentChanged(EditDocument? value) => SelectedNode = null;
}
