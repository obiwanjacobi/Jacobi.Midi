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
        => !String.IsNullOrWhiteSpace(dataType) && Resolve(document, dataType) is null;

    /// <summary>
    /// Returns a warning when <paramref name="value"/> is not valid for <paramref name="dataType"/>; otherwise null.
    /// Empty values, references ('ref:', 'pub:') and custom data types are not validated.
    /// </summary>
    public static string? Validate(EditDocument? document, string dataType, string value)
    {
        value = value?.Trim() ?? String.Empty;
        if (value.Length == 0 ||
            value.StartsWith(SchemaNodeRef.Prefix, StringComparison.Ordinal) ||
            value.StartsWith(SchemaPubRef.Prefix, StringComparison.Ordinal) ||
            Resolve(document, dataType) is not { } type)
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
    /// Follows the data type defs of the document down to a registered descriptor, the facets of the most derived def win.
    /// Returns null when the chain does not end in a registered type.
    /// </summary>
    private static ResolvedType? Resolve(EditDocument? document, string dataType)
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
                return new(descriptor, min ?? descriptor.Min, max ?? descriptor.Max, options ?? descriptor.Options);
            }

            var isLocal = document is not null &&
                (String.IsNullOrEmpty(name.Namespace) || String.Equals(name.Namespace, document.Name, StringComparison.OrdinalIgnoreCase));
            var def = isLocal
                ? document!.DataTypeDefs.FirstOrDefault(d => String.Equals(d.Name, name.Name, StringComparison.OrdinalIgnoreCase))
                : null;
            if (def is null || !visited.Add(def))
            {
                return null;
            }
            min ??= def.Min;
            max ??= def.Max;
            options ??= def.Options;
            name = def.BasedOn;
        }
    }

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
