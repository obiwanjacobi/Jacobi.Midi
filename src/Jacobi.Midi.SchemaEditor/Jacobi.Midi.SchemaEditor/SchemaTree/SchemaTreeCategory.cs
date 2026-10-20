using System.Collections;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// A fixed header in the document view of the schema tree that lists one of the
/// collections of an <see cref="Models.EditDocument"/> (includes, node defs, ...).
/// It only exists in the tree; the host sees it as <see cref="SchemaTreeControl.SelectedCategory"/>, not as a selected item.
/// </summary>
/// <param name="collection">The document collection this header shows.</param>
/// <param name="title">The header text.</param>
/// <param name="items">The (observable) document collection shown under the header.</param>
internal sealed class SchemaTreeCategory(DocumentCollection collection, string title, IList items)
{
    public DocumentCollection Collection { get; } = collection;
    public string Title { get; } = title;
    public IList Items { get; } = items;
}
