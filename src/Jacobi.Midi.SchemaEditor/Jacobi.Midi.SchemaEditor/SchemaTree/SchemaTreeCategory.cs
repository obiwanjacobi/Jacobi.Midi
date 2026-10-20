using System.Collections;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// A fixed header in the document view of the schema tree that lists one of the
/// collections of an <see cref="Models.EditDocument"/> (includes, node defs, ...).
/// It only exists in the tree; it is never reported as a selection to the host.
/// </summary>
/// <param name="title">The header text.</param>
/// <param name="items">The (observable) document collection shown under the header.</param>
internal sealed class SchemaTreeCategory(string title, IList items)
{
    public string Title { get; } = title;
    public IList Items { get; } = items;
}
