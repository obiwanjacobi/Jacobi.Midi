using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// View model behind <see cref="SchemaTreeControl"/>.
/// Exposes the items of the active view (the document members, or the document collections
/// grouped by <see cref="SchemaTreeCategory"/>) and the selected item.
/// The tree binds directly to the (observable) <see cref="EditNode"/> model.
/// </summary>
internal sealed partial class SchemaTreeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial EditDocument? Document { get; set; }

    /// <summary>
    /// True to show the document collections, false to show the document members.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMembersView), nameof(Items))]
    public partial bool IsDocumentView { get; set; }

    public bool IsMembersView
    {
        get => !IsDocumentView;
        set => IsDocumentView = !value;
    }

    /// <summary>
    /// The category headers of the document view. Empty when there is no document.
    /// </summary>
    public IReadOnlyList<SchemaTreeCategory> Categories { get; private set; } = [];

    /// <summary>
    /// The root items of the active view.
    /// </summary>
    public IEnumerable? Items => IsDocumentView ? Categories : Document?.Members;

    /// <summary>
    /// The selected tree item: an <see cref="EditNode"/>, another document item or a <see cref="SchemaTreeCategory"/>.
    /// </summary>
    [ObservableProperty]
    public partial object? SelectedItem { get; set; }

    partial void OnDocumentChanged(EditDocument? value)
    {
        Categories = value is null ? [] :
        [
            new(DocumentCollection.Includes, "Includes", value.Includes),
            new(DocumentCollection.NodeDefs, "Node Defs", value.NodeDefs),
            new(DocumentCollection.DataTypeDefs, "Data Type Defs", value.DataTypeDefs),
            new(DocumentCollection.ProcessorDefs, "Processor Defs", value.ProcessorDefs),
            new(DocumentCollection.Processors, "Processors", value.Processors),
            new(DocumentCollection.Properties, "Properties", value.Properties),
        ];
        SelectedItem = null;
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Items));
    }

    /// <summary>
    /// Returns true when <paramref name="item"/> is shown in the document view, false when it is a member.
    /// </summary>
    public static bool IsDocumentItem(object item)
        => item is not EditNode node || GetRoot(node) is not EditDocument;

    /// <summary>
    /// Returns the tree items from the root of the view containing <paramref name="item"/> down to (excluding) the item itself.
    /// Returns null when the item is not part of <see cref="Document"/>.
    /// </summary>
    public IReadOnlyList<object>? GetPath(object item)
    {
        var path = new List<object>();
        var root = item;
        if (item is EditNode node)
        {
            for (var parent = node.Parent; parent is not null and not EditDocument; parent = parent.Parent)
            {
                path.Insert(0, parent);
            }
            root = path.Count > 0 ? path[0] : node;
            if (GetRoot(node) is EditDocument document)
            {
                return ReferenceEquals(document, Document) ? path : null;
            }
        }
        var category = Categories.FirstOrDefault(c => c.Items.Contains(root));
        if (category is null)
        {
            return null;
        }
        path.Insert(0, category);
        return path;
    }

    private static EditNode GetRoot(EditNode node)
    {
        while (node.Parent is not null)
        {
            node = node.Parent;
        }
        return node;
    }
}
