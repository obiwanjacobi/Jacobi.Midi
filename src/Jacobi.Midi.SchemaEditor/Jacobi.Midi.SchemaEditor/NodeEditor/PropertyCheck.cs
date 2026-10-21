using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// One entry of the property name drop-down list of the node editor: a property published by a processor.
/// </summary>
/// <param name="Name">The property name as it is written: short under a processor, prefixed with the processor key on an item.</param>
/// <param name="Descriptor">The published property.</param>
internal sealed record PropertyOption(string Name, PropertyDescriptor Descriptor)
{
    /// <summary>
    /// The data type of the property and whether it is required or read-only.
    /// </summary>
    public string Summary
        => Descriptor.DataType.FullName
            + (Descriptor.IsRequired ? ", required" : String.Empty)
            + (Descriptor.IsReadOnly ? ", read-only" : String.Empty);

    public string Description => Descriptor.Description;

    // the editable drop-down list uses the text of the selected item
    public override string ToString() => Name;
}

/// <summary>
/// How the property rows of one owner (a processor, or an item with processors) are checked against the properties
/// the processors publish: the names to choose from, the descriptor of a name and the warning for a name and value.
/// </summary>
/// <param name="NameOptions">The published property names to choose from.</param>
/// <param name="Describe">Returns the published property for a name; null when it is not published (or not checked).</param>
/// <param name="Validate">Returns the warning for a name and value; null when valid (or not checked).</param>
internal sealed record PropertyCheck(
    IReadOnlyList<PropertyOption> NameOptions,
    Func<string, PropertyDescriptor?> Describe,
    Func<string, string?, string?> Validate);
