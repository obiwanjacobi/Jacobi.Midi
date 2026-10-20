using System.Collections;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// The single root item of the schema tree: it shows the name of the <see cref="Models.EditDocument"/>
/// and holds the items of the active view (the document members, or the document collection categories) as its children.
/// It only exists in the tree; the host sees the selected root as the <see cref="Models.EditDocument"/> itself.
/// </summary>
/// <param name="document">The document this root represents.</param>
/// <param name="children">The root items of the view this root belongs to.</param>
internal sealed class SchemaTreeRoot(EditDocument document, IEnumerable children)
{
    public EditDocument Document { get; } = document;
    public IEnumerable Children { get; } = children;
}
