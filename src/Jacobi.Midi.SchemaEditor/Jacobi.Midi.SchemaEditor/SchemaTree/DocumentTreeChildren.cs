using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// The children of a document in a tree: its <see cref="NodeDefsItem"/> (fixed at the top) followed by its members.
/// It follows the changes of the <see cref="EditGroup.Members"/> of the document.
/// There is one instance per document (see <see cref="Get"/>); it is not meant to be changed by its users.
/// </summary>
internal sealed class DocumentTreeChildren : ObservableCollection<object>
{
    private static readonly ConditionalWeakTable<EditDocument, DocumentTreeChildren> Instances = new();

    private readonly EditDocument _document;

    private DocumentTreeChildren(EditDocument document)
        : base([NodeDefsItem.Get(document), .. document.Members])
    {
        _document = document;
        document.Members.CollectionChanged += OnMembersChanged;
    }

    /// <summary>
    /// Returns the tree children of <paramref name="document"/>.
    /// </summary>
    public static DocumentTreeChildren Get(EditDocument document) => Instances.GetValue(document, owner => new DocumentTreeChildren(owner));

    // the members start at index 1, after the node defs item
    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                for (var index = 0; index < e.NewItems.Count; index++)
                {
                    Insert(e.NewStartingIndex + 1 + index, e.NewItems[index]!);
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                for (var index = 0; index < e.OldItems.Count; index++)
                {
                    RemoveAt(e.OldStartingIndex + 1);
                }
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                for (var index = 0; index < e.NewItems.Count; index++)
                {
                    this[e.NewStartingIndex + 1 + index] = e.NewItems[index]!;
                }
                break;
            case NotifyCollectionChangedAction.Move:
                Move(e.OldStartingIndex + 1, e.NewStartingIndex + 1);
                break;
            default:
                while (Count > 1)
                {
                    RemoveAt(Count - 1);
                }
                foreach (var member in _document.Members)
                {
                    Add(member);
                }
                break;
        }
    }
}
