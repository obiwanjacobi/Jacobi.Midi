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

    /// <summary>
    /// The reverse of <see cref="Find"/>: the authored node of a compiled node, given by the name of its document and its path
    /// from a root of that document (node defs expanded, as in a plan). A member that comes from a node def is found in the
    /// node def (which may be in another of the <paramref name="documents"/>). Null when the path does not lead to an authored node.
    /// </summary>
    public static EditNode? FindAuthored(IReadOnlyList<EditDocument> documents, string documentName, SchemaPath path)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var names = path.Segments.ToList();
        var owner = FindDocument(documents, documentName);
        EditNode? current = owner?.Members.FirstOrDefault(member => SameName(member.Name, names.FirstOrDefault()));
        foreach (var name in names.Skip(1))
        {
            if (current is null || owner is null)
            {
                return null;
            }
            (current, owner) = FindMember(documents, current, owner, name);
        }
        return current;
    }

    // a member by name: an authored member, or one of the node def the group conforms to (and of the node defs it conforms to)
    private static (EditNode? Member, EditDocument? Owner) FindMember(IReadOnlyList<EditDocument> documents, EditNode node, EditDocument owner, string name)
    {
        var visited = new HashSet<EditNode>();
        for (var current = node; current is not null && visited.Add(current);)
        {
            if (current is EditGroup group && group.Members.FirstOrDefault(member => SameName(member.Name, name)) is { } member)
            {
                return (member, owner);
            }
            if (current.NodeDef is not { } nodeDef)
            {
                break;
            }
            owner = String.IsNullOrEmpty(nodeDef.Namespace) ? owner : FindDocument(documents, nodeDef.Namespace) ?? owner;
            current = owner.NodeDefs.FirstOrDefault(def => SameName(def.Name, nodeDef.Name));
        }
        return (null, null);
    }

    private static EditDocument? FindDocument(IReadOnlyList<EditDocument> documents, string name)
        => documents.FirstOrDefault(document => SameName(document.Name, name));

    private static bool SameName(string? left, string? right) => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
