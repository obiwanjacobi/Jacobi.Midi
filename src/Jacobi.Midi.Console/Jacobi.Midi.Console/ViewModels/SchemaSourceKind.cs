namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The kinds of sources the user loads into the rig: a schema file, an exchange file, or an assembly with embedded
/// schemas, exchange files and processors.
/// </summary>
internal enum SchemaSourceKind
{
    Schema,
    Exchange,
    Assembly,
}
