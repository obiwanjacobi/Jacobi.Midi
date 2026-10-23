using System.Runtime.CompilerServices;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// The fixed first child of a document in the schema tree (and of an embedded schema in the assembly tree):
/// it holds the <see cref="EditDocument.NodeDefs"/> of the document as its children.
/// There is one instance per document (see <see cref="Get"/>), so it can be selected and found again.
/// </summary>
internal sealed class NodeDefsItem
{
    private static readonly ConditionalWeakTable<EditDocument, NodeDefsItem> Instances = new();

    private NodeDefsItem(EditDocument document) => Document = document;

    public EditDocument Document { get; }

    /// <summary>
    /// Returns the node defs item of <paramref name="document"/>.
    /// </summary>
    public static NodeDefsItem Get(EditDocument document) => Instances.GetValue(document, owner => new NodeDefsItem(owner));
}
