using System.Globalization;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// What the schema says about a parameter, for the controls: its name, the type of its logical value and, for a
/// number, its range and step (the data type's <c>min</c>, <c>max</c> and <c>scale</c>), for a choice its option names.
/// </summary>
/// <param name="Name">The field name.</param>
/// <param name="ValueType">The CLR type of the logical value (the data type's value type).</param>
/// <param name="Range">The range of a number; null when the parameter is not a number or has no known range.</param>
/// <param name="Step">The smallest change of a number: the data type's <c>scale</c>, otherwise 1.</param>
/// <param name="Options">The option names of a choice; null for any other parameter.</param>
public sealed record ParameterInfo(string Name, Type ValueType, ValueRange? Range, decimal Step, IReadOnlyList<string>? Options)
{
    /// <summary>
    /// The value a reset gives: the data type's <c>optionsDefault</c>, otherwise the origin of a number (zero within the
    /// range) and off for a switch; null when there is none (a text).
    /// </summary>
    public object? Default { get; init; }

    public ParameterKind Kind
        => Options is not null ? ParameterKind.Choice
            : ValueType == typeof(bool) ? ParameterKind.Switch
            : ValueType == typeof(string) ? ParameterKind.Text
            : ParameterKind.Number;

    /// <summary>The parameter info of a field of a plan.</summary>
    public static ParameterInfo From(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var dataType = field.DataType;
        var valueType = field.ValueType.ClrType;

        IReadOnlyList<string>? options = valueType == typeof(string) && dataType.Options is { } map
            ? [.. map.Values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty)]
            : null;

        ValueRange? range = null;
        if (options is null && IsNumber(valueType))
        {
            var minimum = ToDecimal(dataType.Min) ?? MinimumOf(valueType);
            var maximum = ToDecimal(dataType.Max) ?? MaximumOf(valueType);
            if (minimum is { } min && maximum is { } max && min <= max)
                range = new ValueRange(min, max);
        }

        object? defaultValue = dataType.OptionsDefault
            ?? (range is { } known ? known.Clamp(0)
                : valueType == typeof(bool) ? false
                : null);
        return new ParameterInfo(field.Field.Name, valueType, range, dataType.Scale ?? 1, options) { Default = defaultValue };
    }

    /// <summary>Converts a value for the parameter (e.g. a decimal of a knob) to its value type.</summary>
    public object ToValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.GetType() == ValueType ? value : Convert.ChangeType(value, ValueType, CultureInfo.InvariantCulture);
    }

    private static bool IsNumber(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
            || type == typeof(decimal) || type == typeof(double) || type == typeof(float);

    private static decimal? ToDecimal(object? value)
        => value is null ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    // the range of the CLR type when the data type has none; null for types without a useful range
    private static decimal? MinimumOf(Type type)
        => type == typeof(byte) ? Byte.MinValue : type == typeof(sbyte) ? SByte.MinValue
            : type == typeof(ushort) ? UInt16.MinValue : type == typeof(short) ? Int16.MinValue
            : null;

    private static decimal? MaximumOf(Type type)
        => type == typeof(byte) ? Byte.MaxValue : type == typeof(sbyte) ? SByte.MaxValue
            : type == typeof(ushort) ? UInt16.MaxValue : type == typeof(short) ? Int16.MaxValue
            : null;
}

/// <summary>The kinds of parameters, by the type of their logical value.</summary>
public enum ParameterKind
{
    Number,
    Switch,
    Choice,
    Text,
}
