using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// View model behind <see cref="SchemaTreeControl"/>.
/// Exposes the <see cref="Root"/> (the document with its members below it) and the selected item.
/// The tree binds directly to the (observable) <see cref="EditNode"/> model.
/// </summary>
internal sealed partial class SchemaTreeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial EditDocument? Document { get; set; }

    /// <summary>
    /// The root of the tree: the document with its members below it. Null when there is no document.
    /// </summary>
    public SchemaTreeRoot? Root { get; private set; }

    /// <summary>
    /// The root items of the tree: the <see cref="Root"/>.
    /// </summary>
    public IEnumerable? Items => Root is { } root ? new[] { root } : null;

    /// <summary>
    /// The selected tree item: the <see cref="SchemaTreeRoot"/>, an <see cref="EditNode"/> or null.
    /// </summary>
    [ObservableProperty]
    public partial object? SelectedItem { get; set; }

    partial void OnDocumentChanged(EditDocument? value)
    {
        Root = value is null ? null : new SchemaTreeRoot(value, value.Members);
        SelectedItem = null;
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Items));
    }

    /// <summary>
    /// Returns the tree item that represents <paramref name="item"/>: the <see cref="Root"/> for the document, otherwise the item itself.
    /// </summary>
    public object? ToTreeItem(object? item) => item is EditDocument document && ReferenceEquals(document, Document) ? Root : item;

    /// <summary>
    /// Returns the item the host sees for <paramref name="treeItem"/>: the document for the root, otherwise the item itself.
    /// </summary>
    public static object? ToItem(object? treeItem) => treeItem is SchemaTreeRoot root ? root.Document : treeItem;

    /// <summary>
    /// Returns the tree items from the <see cref="Root"/> down to (excluding) the item itself; empty for the document.
    /// Returns null when the item is not shown in the tree, e.g. a node def or a node of another document.
    /// </summary>
    public IReadOnlyList<object>? GetPath(object item)
    {
        if (Root is not { } root)
        {
            return null;
        }
        if (item is EditDocument document)
        {
            return ReferenceEquals(document, Document) ? [] : null;
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
        return ReferenceEquals(top.Parent, Document) ? [root, .. path] : null;
    }
}
