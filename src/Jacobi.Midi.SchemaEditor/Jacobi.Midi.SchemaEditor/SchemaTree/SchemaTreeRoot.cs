using System.Collections;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// A root item of the schema tree (one per shown document): it shows the name of the <see cref="Models.EditDocument"/>
/// and holds its <see cref="DocumentTreeChildren"/> (the fixed <see cref="NodeDefsItem"/> and the document members) as its children.
/// It only exists in the tree; the host sees the selected root as the <see cref="Models.EditDocument"/> itself.
/// </summary>
/// <param name="document">The document this root represents.</param>
internal sealed class SchemaTreeRoot(EditDocument document)
{
    public EditDocument Document { get; } = document;
    public IEnumerable Children { get; } = DocumentTreeChildren.Get(document);
}
