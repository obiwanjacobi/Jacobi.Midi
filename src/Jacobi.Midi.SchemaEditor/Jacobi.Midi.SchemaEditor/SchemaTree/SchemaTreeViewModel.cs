using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// View model behind <see cref="SchemaTreeControl"/>.
/// Exposes the <see cref="Roots"/> (one per document, with its members below it) and the selected item.
/// The tree binds directly to the (observable) <see cref="EditNode"/> model.
/// </summary>
internal sealed partial class SchemaTreeViewModel : ObservableObject
{
    /// <summary>
    /// The roots of the tree: one per document, in the order of <see cref="SetDocuments"/>.
    /// </summary>
    public ObservableCollection<SchemaTreeRoot> Roots { get; } = [];

    /// <summary>
    /// The root items of the tree: the <see cref="Roots"/>.
    /// </summary>
    public IEnumerable Items => Roots;

    /// <summary>
    /// The selected tree item: a <see cref="SchemaTreeRoot"/>, an <see cref="EditNode"/> or null.
    /// </summary>
    [ObservableProperty]
    public partial object? SelectedItem { get; set; }

    /// <summary>
    /// Shows <paramref name="documents"/> as the roots of the tree. The roots of documents that stay keep their tree items
    /// (and so their expanded state); the selection is cleared when its document is removed.
    /// </summary>
    /// <returns>The roots that were added.</returns>
    public IReadOnlyList<SchemaTreeRoot> SetDocuments(IReadOnlyList<EditDocument> documents)
    {
        for (var index = Roots.Count - 1; index >= 0; index--)
        {
            if (!documents.Any(document => ReferenceEquals(document, Roots[index].Document)))
            {
                if (SelectedItem is { } selected && ReferenceEquals(FindRoot(GetDocument(selected)), Roots[index]))
                {
                    SelectedItem = null;
                }
                Roots.RemoveAt(index);
            }
        }

        var added = new List<SchemaTreeRoot>();
        for (var index = 0; index < documents.Count; index++)
        {
            var existing = FindRoot(documents[index]);
            if (existing is null)
            {
                existing = new SchemaTreeRoot(documents[index], documents[index].Members);
                added.Add(existing);
                Roots.Insert(Math.Min(index, Roots.Count), existing);
            }
            else if (Roots.IndexOf(existing) is var current && current != index)
            {
                Roots.Move(current, index);
            }
        }
        return added;
    }

    /// <summary>
    /// Returns the tree item that represents <paramref name="item"/>: the root of a shown document, otherwise the item itself.
    /// </summary>
    public object? ToTreeItem(object? item) => item is EditDocument document && FindRoot(document) is { } root ? root : item;

    /// <summary>
    /// Returns the item the host sees for <paramref name="treeItem"/>: the document for a root, otherwise the item itself.
    /// </summary>
    public static object? ToItem(object? treeItem) => treeItem is SchemaTreeRoot root ? root.Document : treeItem;

    /// <summary>
    /// Returns the tree items from the root of its document down to (excluding) the item itself; empty for a document.
    /// Returns null when the item is not shown in the tree, e.g. a node def or a node of a document that is not shown.
    /// </summary>
    public IReadOnlyList<object>? GetPath(object item)
    {
        if (item is EditDocument document)
        {
            return FindRoot(document) is null ? null : [];
        }
        if (item is not EditNode node)
        {
            return null;
        }

        var path = new List<object>();
        for (var parent = node.Parent; parent is not null and not EditDocument; parent = parent.Parent)
        {
            path.Insert(0, parent);
        }
        var top = path.Count > 0 ? (EditNode)path[0] : node;
        return top.Parent is EditDocument owner && FindRoot(owner) is { } root ? [root, .. path] : null;
    }

    private SchemaTreeRoot? FindRoot(EditDocument? document)
        => document is null ? null : Roots.FirstOrDefault(root => ReferenceEquals(root.Document, document));

    /// <summary>
    /// The document a tree item belongs to; null for an item outside a document (a node def).
    /// </summary>
    private static EditDocument? GetDocument(object item)
    {
        var node = ToItem(item) as EditNode;
        while (node is not null and not EditDocument)
        {
            node = node.Parent;
        }
        return node as EditDocument;
    }
}
