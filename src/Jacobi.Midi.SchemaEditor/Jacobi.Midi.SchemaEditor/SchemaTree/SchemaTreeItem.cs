using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// Tree-view wrapper around a single <see cref="EditNode"/>.
/// Keeps its <see cref="Children"/> and <see cref="DisplayName"/> in sync with the
/// underlying node until <see cref="Detach"/> is called.
/// </summary>
internal partial class SchemaTreeItem : ObservableObject
{
    public SchemaTreeItem(EditNode node, SchemaTreeItem? parent = null)
    {
        Node = node;
        Parent = parent;
        Children = new ObservableCollection<SchemaTreeItem>(CreateChildren());
        Node.PropertyChanged += OnNodePropertyChanged;
        if (Node is EditGroup group)
        {
            group.Members.CollectionChanged += OnMembersChanged;
        }
    }

    public EditNode Node { get; }

    public SchemaTreeItem? Parent { get; }

    public ObservableCollection<SchemaTreeItem> Children { get; }

    public string Name => Node.Name;

    public string DisplayName => $"{Node.Name}  ·  {Node.Kind}";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Finds the item that wraps <paramref name="node"/> in this item or its descendants.
    /// </summary>
    public SchemaTreeItem? Find(EditNode node)
    {
        if (ReferenceEquals(Node, node))
        {
            return this;
        }
        foreach (var child in Children)
        {
            if (child.Find(node) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Unhooks all change handlers from the node (and descendants) so this item can be discarded.
    /// </summary>
    public void Detach()
    {
        Node.PropertyChanged -= OnNodePropertyChanged;
        if (Node is EditGroup group)
        {
            group.Members.CollectionChanged -= OnMembersChanged;
        }
        foreach (var child in Children)
        {
            child.Detach();
        }
    }

    private IEnumerable<SchemaTreeItem> CreateChildren()
        => Node is EditGroup group
            ? group.Members.Select(m => new SchemaTreeItem(m, this))
            : [];

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditNode.Name))
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => SyncItems(Children, e, node => new SchemaTreeItem(node, this), CreateChildren);

    /// <summary>
    /// Applies a member collection change to the matching collection of tree items.
    /// </summary>
    internal static void SyncItems(
        ObservableCollection<SchemaTreeItem> items,
        NotifyCollectionChangedEventArgs e,
        Func<EditNode, SchemaTreeItem> create,
        Func<IEnumerable<SchemaTreeItem>> rebuild)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                var index = e.NewStartingIndex;
                foreach (EditNode node in e.NewItems)
                {
                    items.Insert(index++, create(node));
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                for (var i = 0; i < e.OldItems.Count; i++)
                {
                    items[e.OldStartingIndex].Detach();
                    items.RemoveAt(e.OldStartingIndex);
                }
                break;
            case NotifyCollectionChangedAction.Move when e.OldItems?.Count == 1:
                items.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            default:
                foreach (var item in items)
                {
                    item.Detach();
                }
                items.Clear();
                foreach (var item in rebuild())
                {
                    items.Add(item);
                }
                break;
        }
    }
}
