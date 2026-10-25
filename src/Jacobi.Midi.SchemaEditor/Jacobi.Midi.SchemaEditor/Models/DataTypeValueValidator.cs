using System.Globalization;
using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// Checks the constant Value of a field against its data type: the data type is resolved from the
/// <see cref="DataTypeRegistry"/> and the (local) data type defs of the document. The Value is a logical value:
/// it must parse with the value type of the descriptor, be one of its Options and respect its Min and Max facets.
/// The data type defs are resolved by the serializer itself (<see cref="DataTypeRegistry.RegisterDataTypeDefs"/>),
/// so facet inheritance and the implied value type are the same as when the schema is compiled.
/// Produces warnings only; data types that cannot be resolved (custom types) are not validated.
/// </summary>
internal static class DataTypeValueValidator
{
    private static readonly DataTypeRegistry Registry = DataTypeRegistry.CreateDefault();

    /// <summary>
    /// True when <paramref name="dataType"/> is not empty but is neither registered nor a data type def of the
    /// <paramref name="document"/> (based on a registered type): it is a custom data type.
    /// </summary>
    public static bool IsCustom(EditDocument? document, string dataType)
        => document is null ? IsCustom([], String.Empty, dataType) : IsCustom([document], document.Name, dataType);

    /// <summary>
    /// True when <paramref name="dataType"/> is not empty but is neither registered nor a data type def of one of the
    /// <paramref name="documents"/> (based on a registered type). Unqualified names are looked up in <paramref name="ownerName"/>.
    /// </summary>
    public static bool IsCustom(IReadOnlyList<EditDocument> documents, string ownerName, string dataType)
        => !String.IsNullOrWhiteSpace(dataType) && Resolve(documents, ownerName, dataType, Registry) is null;

    /// <summary>
    /// Returns a warning when <paramref name="value"/> is not valid for <paramref name="dataType"/>; otherwise null.
    /// Empty values, references ('ref:', 'pub:') and custom data types are not validated.
    /// </summary>
    public static string? Validate(EditDocument? document, string dataType, string value)
        => document is null ? Validate([], String.Empty, dataType, value) : Validate([document], document.Name, dataType, value);

    /// <summary>
    /// Returns a warning when <paramref name="value"/> is not valid for <paramref name="dataType"/>; otherwise null.
    /// The data type defs of all <paramref name="documents"/> are known (so also those of included schemas);
    /// unqualified names are looked up in <paramref name="ownerName"/>.
    /// </summary>
    /// <param name="registry">The registered data types; null for the built-in data types.</param>
    public static string? Validate(IReadOnlyList<EditDocument> documents, string ownerName, string dataType, string value,
        DataTypeRegistry? registry = null)
    {
        value = value?.Trim() ?? String.Empty;
        if (IsDerived(value) || Resolve(documents, ownerName, dataType, registry ?? Registry) is not { } resolved)
        {
            return null;
        }
        if (resolved.Error is { } error)
        {
            return $"The data type {dataType.Trim()} is not valid: {error}";
        }

        var type = resolved.Descriptor!;
        return Validate(type.ValueType, type, dataType.Trim(), value, out _);
    }

    /// <summary>True when <paramref name="value"/> is empty or a reference ('ref:', 'pub:'): not a literal, the engine derives the value.</summary>
    public static bool IsDerived(string value)
        => value.Length == 0 ||
            value.StartsWith(SchemaNodeRef.Prefix, StringComparison.Ordinal) ||
            value.StartsWith(SchemaPubRef.Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Returns a warning when <paramref name="value"/> is not a valid logical value; otherwise null.
    /// The value is one of the options (matched case-insensitive) or a literal of <paramref name="valueType"/>,
    /// and must respect the Options, Min and Max of <paramref name="facets"/>.
    /// </summary>
    /// <param name="valueType">The data type of the logical value: parses the literal.</param>
    /// <param name="facets">The data type with the facets that apply to the value; null when none apply.</param>
    /// <param name="typeName">The name of the data type in the warning.</param>
    /// <param name="value">The literal; it is not trimmed (a text value may start or end with spaces).</param>
    /// <param name="parsed">The logical value; null when there is a warning.</param>
    public static string? Validate(DataTypeDescriptor valueType, DataTypeDescriptor? facets, string typeName, string value, out object? parsed)
    {
        // an option value is matched case-insensitive
        parsed = facets?.Options?.Values.FirstOrDefault(option => String.Equals(Format(option), value, StringComparison.OrdinalIgnoreCase));
        if (parsed is null && (!valueType.Parse(value, out parsed) || parsed is null))
        {
            parsed = null;
            return $"'{value}' is not a valid value for {typeName}.";
        }
        var warning = facets switch
        {
            { Options: { } options } when !options.Values.Contains(parsed) => $"'{value}' is not one of the options of {typeName}.",
            { Min: { } min } when DataTypeDescriptor.Compare(parsed, min) < 0 => $"{value} is less than the minimum {Format(min)} of {typeName}.",
            { Max: { } max } when DataTypeDescriptor.Compare(parsed, max) > 0 => $"{value} is greater than the maximum {Format(max)} of {typeName}.",
            _ => null
        };
        if (warning is not null)
        {
            parsed = null;
        }
        return warning;
    }

    /// <summary>
    /// Returns a warning when <paramref name="value"/> is not a literal of the physical part of <paramref name="dataType"/>
    /// (as the physical value of an option); otherwise null. Facets are not applied. Custom data types are not validated.
    /// </summary>
    public static string? ValidatePhysical(EditDocument? document, string dataType, string value)
    {
        if (Resolve(document is null ? [] : [document], document?.Name ?? String.Empty, dataType, Registry) is not { } resolved)
        {
            return null;
        }
        if (resolved.Error is { } error)
        {
            return $"The data type {dataType.Trim()} is not valid: {error}";
        }
        return resolved.Descriptor!.Parse(value, out var parsed) && parsed is not null
            ? null
            : $"'{value}' is not a valid value for {dataType.Trim()}.";
    }

    /// <summary>The resolved descriptor, or the error why the data type def (or one it depends on) is not valid.</summary>
    private sealed record Resolution(DataTypeDescriptor? Descriptor, string? Error);

    /// <summary>
    /// Resolves a registered data type, or a data type def of the documents together with the defs it depends on (its base and value type)
    /// into a copy of <paramref name="registry"/>. A reference without namespace is looked up in the document of the referring def.
    /// Returns null when the data type is neither registered nor a data type def (a custom type).
    /// </summary>
    private static Resolution? Resolve(IReadOnlyList<EditDocument> documents, string ownerName, string dataType, DataTypeRegistry registry)
    {
        if (String.IsNullOrWhiteSpace(dataType))
        {
            return null;
        }
        var name = new SchemaName(dataType.Trim());
        if (registry.TryGet(name, out var registered))
        {
            return new(registered, null);
        }
        var owner = String.IsNullOrEmpty(name.Namespace) ? ownerName : name.Namespace;
        if (FindDef(documents, owner, name) is null)
        {
            return null;
        }

        // the def and the defs it depends on, with the name of their document
        var defs = new List<(string Owner, EditDataTypeDef Definition)>();
        var pending = new Stack<(string Owner, SchemaName Name)>([(owner, name)]);
        while (pending.TryPop(out var reference))
        {
            var defOwner = String.IsNullOrEmpty(reference.Name.Namespace) ? reference.Owner : reference.Name.Namespace;
            // a def that is not found is reported by the registry
            if (registry.TryGet(reference.Name, out _) ||
                FindDef(documents, defOwner, reference.Name) is not { } def ||
                defs.Any(item => ReferenceEquals(item.Definition, def)))
            {
                continue;
            }
            defs.Add((defOwner, def));
            pending.Push((defOwner, def.BasedOn));
            if (def.ValueType is { } valueType)
            {
                pending.Push((defOwner, valueType));
            }
        }

        try
        {
            var schemas = defs.GroupBy(item => item.Owner, StringComparer.OrdinalIgnoreCase).Select(group =>
            {
                var builder = new SchemaDocumentBuilder(group.Key);
                foreach (var (_, def) in group)
                {
                    EditNodeMapper.WriteDataTypeDef(def, builder);
                }
                return builder.Build();
            }).ToList();

            var resolver = new DataTypeRegistry();
            foreach (var type in registry.Types)
            {
                resolver.Register(type);
            }
            resolver.RegisterDataTypeDefs(schemas);
            return resolver.TryGet(new SchemaName($"{owner}{SchemaName.Separator}{name.Name}"), out var descriptor)
                ? new(descriptor, null)
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            return new(null, ex.Message);
        }
    }

    private static EditDataTypeDef? FindDef(IReadOnlyList<EditDocument> documents, string owner, SchemaName name)
        => documents
            .Where(d => String.Equals(d.Name, owner, StringComparison.OrdinalIgnoreCase))
            .SelectMany(d => d.DataTypeDefs)
            .FirstOrDefault(d => String.Equals(d.Name, name.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Formats a (logical) value as the text its value type parses again (see <see cref="Validate(DataTypeDescriptor, DataTypeDescriptor?, string, string, out object?)"/>):
    /// hex bytes, or the invariant text of the value.
    /// </summary>
    public static string Format(object? value) => value switch
    {
        null => String.Empty,
        byte[] bytes => Convert.ToHexString(bytes),
        ReadOnlyMemory<byte> memory => Convert.ToHexString(memory.Span),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? String.Empty
    };
}
