namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// Identifies one of the document-level collections of an <see cref="EditDocument"/>
/// (shown as a category in the Document view of the schema tree).
/// </summary>
internal enum DocumentCollection
{
    Includes,
    NodeDefs,
    DataTypeDefs,
    ProcessorDefs,
    Processors,
    Properties,
}
