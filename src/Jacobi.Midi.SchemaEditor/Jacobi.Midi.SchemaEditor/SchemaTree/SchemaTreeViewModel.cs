using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// View model behind <see cref="SchemaTreeControl"/>.
/// Builds the tree of <see cref="SchemaTreeItem"/>s for the members of an <see cref="EditDocument"/>
/// and tracks the selected item.
/// </summary>
internal sealed partial class SchemaTreeViewModel : ObservableObject
{
    private EditDocument? _document;

    public ObservableCollection<SchemaTreeItem> SchemaNodes { get; } = [];

    [ObservableProperty]
    public partial SchemaTreeItem? SelectedNode { get; set; }

    /// <summary>
    /// Rebuilds the tree from the members of <paramref name="document"/> (or clears it when null).
    /// </summary>
    public void Initialize(EditDocument? document)
    {
        if (_document is not null)
        {
            _document.Members.CollectionChanged -= OnRootMembersChanged;
        }
        foreach (var item in SchemaNodes)
        {
            item.Detach();
        }
        SelectedNode = null;
        SchemaNodes.Clear();

        _document = document;
        if (document is null)
        {
            return;
        }
        foreach (var item in CreateRoots())
        {
            SchemaNodes.Add(item);
        }
        document.Members.CollectionChanged += OnRootMembersChanged;
    }

    /// <summary>
    /// Selects the item that wraps <paramref name="node"/> and expands its ancestors.
    /// Clears the selection when the node is null or not part of the tree.
    /// </summary>
    public void Select(EditNode? node)
    {
        var item = node is null ? null : Find(node);
        for (var parent = item?.Parent; parent is not null; parent = parent.Parent)
        {
            parent.IsExpanded = true;
        }
        SelectedNode = item;
    }

    public SchemaTreeItem? Find(EditNode node)
    {
        foreach (var root in SchemaNodes)
        {
            if (root.Find(node) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private IEnumerable<SchemaTreeItem> CreateRoots()
        => _document?.Members.Select(m => new SchemaTreeItem(m)) ?? [];

    private void OnRootMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => SchemaTreeItem.SyncItems(SchemaNodes, e, node => new SchemaTreeItem(node), CreateRoots);
}
