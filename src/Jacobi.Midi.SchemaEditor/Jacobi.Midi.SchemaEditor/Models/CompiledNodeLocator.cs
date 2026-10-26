using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// Maps an authored <see cref="EditNode"/> to its place in the schema: the names from its outermost node (a root of its
/// document or a node def) down to the node, and its counterpart in a compiled <see cref="SchemaSet"/> (with node defs expanded).
/// The one place that relates the authored nodes to the compiled ones.
/// </summary>
internal static class CompiledNodeLocator
{
    /// <summary>
    /// The names from the outermost node (a root of the document or a node def) down to <paramref name="node"/>,
    /// and that outermost node.
    /// </summary>
    public static (IReadOnlyList<string> Names, EditNode Top) GetNames(EditNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var names = new List<string>();
        var top = node;
        for (EditNode? current = node; current is not null and not EditDocument; current = current.Parent)
        {
            names.Insert(0, current.Name);
            top = current;
        }
        return (names, top);
    }

    /// <summary>
    /// Finds the compiled counterpart of <paramref name="node"/> in <paramref name="schemas"/>.
    /// </summary>
    /// <param name="documents">The authored documents that were compiled: the document of a node def is looked up in them.</param>
    /// <returns>
    /// The compiled node (null when the compiled schema has no node with its names), the compiled outermost node
    /// (null when not found), the names from the outermost node down to the node, and the name of the document that holds it.
    /// </returns>
    public static (SchemaNode? Node, SchemaNode? Top, IReadOnlyList<string> Names, string Document) Find(
        SchemaSet schemas, IReadOnlyList<EditDocument> documents, EditNode node)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(documents);
        var (names, topNode) = GetNames(node);
        var owner = topNode.Parent as EditDocument ?? documents.FirstOrDefault(edit => edit.NodeDefs.Contains(topNode)) ?? documents[0];
        var document = schemas.Documents.First(candidate => String.Equals(candidate.Name, owner.Name, StringComparison.OrdinalIgnoreCase));
        // the outermost node is a root of the document or a node def (which has no parent)
        var top = owner.Members.Contains(topNode) ? (IReadOnlyList<SchemaNode>)document.Roots : document.NodeDefs;
        var outermost = top.FirstOrDefault(candidate => String.Equals(candidate.Name, names[0], StringComparison.OrdinalIgnoreCase));
        var current = outermost;
        foreach (var name in names.Skip(1))
        {
            current = (current as SchemaGroup)?.Members.FirstOrDefault(member => String.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        return (current, outermost, names, owner.Name);
    }
}
