using System.Globalization;
using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// A data type to choose from in a data type drop-down list: its name and a short description of what it is.
/// The editable drop-down takes the <see cref="Name"/> as its text (<see cref="ToString"/>).
/// </summary>
/// <param name="Name">The full name of the data type ('namespace.name').</param>
/// <param name="Description">What the data type is: the size and CLR type of a registered type, the base and facets of a data type def.</param>
internal sealed record DataTypeOption(string Name, string Description)
{
    public override string ToString() => Name;
}

/// <summary>
/// The data types to choose from in the data type drop-down lists of the node editor: the types registered in the
/// <see cref="DataTypeRegistry"/>, followed by the data type defs of the document and of the schemas it includes itself
/// ('schema.name'; see <see cref="SchemaScope.DirectIncludes"/>). Each with a description (<see cref="DataTypeOption"/>).
/// </summary>
internal static class DataTypeOptions
{
    private static readonly DataTypeOption[] RegisteredDataTypes = DataTypeRegistry.CreateDefault().Types
        .Select(descriptor => new DataTypeOption(descriptor.Name.FullName, Describe(descriptor)))
        .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// The data types for the document of <paramref name="scope"/>; without the data type def <paramref name="exclude"/>
    /// (a data type def cannot be based on itself).
    /// </summary>
    public static IReadOnlyList<DataTypeOption> Get(SchemaScope? scope, EditDataTypeDef? exclude = null)
    {
        if (scope is null)
        {
            return RegisteredDataTypes;
        }
        return [.. RegisteredDataTypes, .. ((IEnumerable<EditDocument>)[scope.Document, .. scope.DirectIncludes])
            .SelectMany(document => document.DataTypeDefs
                .Where(def => !ReferenceEquals(def, exclude))
                .Select(def => new DataTypeOption($"{document.Name}{SchemaName.Separator}{def.Name}", Describe(def))))
            .Where(option => !RegisteredDataTypes.Any(registered => String.Equals(registered.Name, option.Name, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>The size and CLR type of a registered data type, e.g. "1 byte · Byte" or "variable · String".</summary>
    internal static string Describe(DataTypeDescriptor descriptor)
    {
        var size = descriptor.FixedSize switch
        {
            null => "variable",
            1 => "1 byte",
            { } bytes => $"{bytes} bytes"
        };
        return $"{size} · {descriptor.ClrType.Name}";
    }

    /// <summary>
    /// The base type and the facets that are set of a data type def, followed by its processors, e.g. "sys.uint8 · 0..127",
    /// "sys.uint8 · 2 options" or "sys.uint8 · scale 1, shift -64 · midi.sysexbyte".
    /// </summary>
    internal static string Describe(EditDataTypeDef definition)
    {
        var parts = new List<string> { definition.BasedOn.ToString() };
        if (definition.Min is not null || definition.Max is not null)
        {
            parts.Add($"{definition.Min}..{definition.Max}");
        }
        if (definition.Options is { Count: > 0 } options)
        {
            parts.Add(options.Count == 1 ? "1 option" : $"{options.Count} options");
        }
        var conversion = new List<string>();
        if (definition.Scale is { } scale)
        {
            conversion.Add($"scale {scale.ToString(CultureInfo.InvariantCulture)}");
        }
        if (definition.Shift is { } shift)
        {
            conversion.Add($"shift {shift.ToString(CultureInfo.InvariantCulture)}");
        }
        if (conversion.Count > 0)
        {
            parts.Add(String.Join(", ", conversion));
        }
        if (definition.ValueType is { } valueType)
        {
            parts.Add($"as {valueType}");
        }
        if (definition.Processors.Count > 0)
        {
            parts.Add(String.Join(", ", definition.Processors.Select(processor => processor.Processor.ToString())));
        }
        return String.Join(" · ", parts);
    }
}
