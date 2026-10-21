namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// One entry of the processor drop-down list of the node editor: a processor key ('namespace.id'),
/// a processor def reference ('ref:name') or a section header that cannot be selected.
/// </summary>
/// <param name="Text">The processor key or reference, or the title of the section.</param>
/// <param name="IsHeader">True for a section header.</param>
internal sealed record ProcessorOption(string Text, bool IsHeader = false)
{
    public bool IsSelectable => !IsHeader;

    // the editable drop-down list uses the text of the selected item
    public override string ToString() => Text;
}
