using System.Globalization;
using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// Checks the constant Value of a field against its data type: the data type is resolved from the
/// <see cref="DataTypeRegistry"/> and the (local) data type defs of the document, the Value must parse with the
/// descriptor and respect its Min, Max and Options facets.
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
        => !String.IsNullOrWhiteSpace(dataType) && Resolve(documents, ownerName, dataType) is null;

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
    public static string? Validate(IReadOnlyList<EditDocument> documents, string ownerName, string dataType, string value)
    {
        value = value?.Trim() ?? String.Empty;
        if (value.Length == 0 ||
            value.StartsWith(SchemaNodeRef.Prefix, StringComparison.Ordinal) ||
            value.StartsWith(SchemaPubRef.Prefix, StringComparison.Ordinal) ||
            Resolve(documents, ownerName, dataType) is not { } type)
        {
            return null;
        }

        var label = type.Options?.Values.Any(option => String.Equals(option, value, StringComparison.OrdinalIgnoreCase)) == true;
        if (label)
        {
            return null;
        }
        if (!type.Descriptor.Parse(value, out var parsed))
        {
            return $"'{value}' is not a valid value for {dataType.Trim()}.";
        }

        if (type.Options is { } options)
        {
            return TryGetNumber(parsed, out var key) && key == Math.Truncate(key) && options.ContainsKey((long)key)
                ? null
                : $"'{value}' is not one of the options of {dataType.Trim()}.";
        }
        if (TryGetNumber(parsed, out var number))
        {
            if (type.Min is { } min && number < min)
            {
                return $"{value} is less than the minimum {min.ToString(CultureInfo.InvariantCulture)} of {dataType.Trim()}.";
            }
            if (type.Max is { } max && number > max)
            {
                return $"{value} is greater than the maximum {max.ToString(CultureInfo.InvariantCulture)} of {dataType.Trim()}.";
            }
        }
        return null;
    }

    private sealed record ResolvedType(DataTypeDescriptor Descriptor, decimal? Min, decimal? Max, IReadOnlyDictionary<long, string>? Options);

    /// <summary>
    /// Follows the data type defs of the documents down to a registered descriptor, the facets of the most derived def win.
    /// The base of a def without namespace is looked up in the document of that def.
    /// Returns null when the chain does not end in a registered type.
    /// </summary>
    private static ResolvedType? Resolve(IReadOnlyList<EditDocument> documents, string ownerName, string dataType)
    {
        if (String.IsNullOrWhiteSpace(dataType))
        {
            return null;
        }

        decimal? min = null, max = null;
        IReadOnlyDictionary<long, string>? options = null;
        var visited = new HashSet<EditDataTypeDef>();
        var name = new SchemaName(dataType.Trim());
        while (true)
        {
            if (Registry.TryGet(name, out var descriptor))
            {
                var (clrMin, clrMax) = GetClrRange(descriptor.ClrType);
                return new(descriptor, min ?? descriptor.Min ?? clrMin, max ?? descriptor.Max ?? clrMax, options ?? descriptor.Options);
            }

            var owner = String.IsNullOrEmpty(name.Namespace) ? ownerName : name.Namespace;
            var def = documents
                .Where(d => String.Equals(d.Name, owner, StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => d.DataTypeDefs)
                .FirstOrDefault(d => String.Equals(d.Name, name.Name, StringComparison.OrdinalIgnoreCase));
            if (def is null || !visited.Add(def))
            {
                return null;
            }
            ownerName = owner;
            min ??= def.Min;
            max ??= def.Max;
            options ??= def.Options;
            name = def.BasedOn;
        }
    }

    /// <summary>
    /// The range of an integral or decimal CLR type: the fallback when neither the descriptor nor a data type def has a Min/Max.
    /// Null bounds for other types (floating point types do not fit a decimal range).
    /// </summary>
    private static (decimal? Min, decimal? Max) GetClrRange(Type type) => Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) switch
    {
        TypeCode.SByte => (SByte.MinValue, SByte.MaxValue),
        TypeCode.Byte => (Byte.MinValue, Byte.MaxValue),
        TypeCode.Int16 => (Int16.MinValue, Int16.MaxValue),
        TypeCode.UInt16 => (UInt16.MinValue, UInt16.MaxValue),
        TypeCode.Int32 => (Int32.MinValue, Int32.MaxValue),
        TypeCode.UInt32 => (UInt32.MinValue, UInt32.MaxValue),
        TypeCode.Int64 => (Int64.MinValue, Int64.MaxValue),
        TypeCode.UInt64 => (UInt64.MinValue, UInt64.MaxValue),
        TypeCode.Decimal => (Decimal.MinValue, Decimal.MaxValue),
        _ => (null, null)
    };

    private static bool TryGetNumber(object? value, out decimal number)
    {
        number = 0;
        if (value is null or string or bool or char or Enum or not IConvertible)
        {
            return false;
        }
        try
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }
}
