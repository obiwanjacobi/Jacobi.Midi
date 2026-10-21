using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// The data types to choose from in the data type drop-down lists of the node editor: the types registered in the
/// <see cref="DataTypeRegistry"/> followed by the data type defs of the document ('document.name').
/// </summary>
internal static class DataTypeOptions
{
    private static readonly string[] RegisteredDataTypes = DataTypeRegistry.CreateDefault().Types
        .Select(descriptor => descriptor.Name.FullName)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// The data type names for <paramref name="document"/>; without the data type def <paramref name="exclude"/>
    /// (a data type def cannot be based on itself).
    /// </summary>
    public static IReadOnlyList<string> Get(EditDocument? document, EditDataTypeDef? exclude = null)
    {
        if (document is null)
        {
            return RegisteredDataTypes;
        }
        return [.. RegisteredDataTypes, .. document.DataTypeDefs
            .Where(def => !ReferenceEquals(def, exclude))
            .Select(def => $"{document.Name}{SchemaName.Separator}{def.Name}")
            .Where(name => !RegisteredDataTypes.Contains(name, StringComparer.OrdinalIgnoreCase))];
    }
}
