using System.Globalization;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// What the schema says about a parameter, for the controls: its name, the type of its logical value and, for a
/// number, its range and step (the data type's <c>min</c>, <c>max</c> and <c>scale</c>), for a choice its option names.
/// An on/off value (a bool) is also a number from 0 (off) to 1 (on), and a choice the number of its option (0 for the
/// first), in steps of 1: the controls of a number in a range (a knob, a slider, an XY pad) can show them too
/// (<see cref="ToNumber"/>, <see cref="ToValue"/>).
/// </summary>
/// <param name="Name">The field name.</param>
/// <param name="ValueType">The CLR type of the logical value (the data type's value type).</param>
/// <param name="Range">The range of a number (0-1 for a bool, the option numbers for a choice); null when the parameter is
/// not a number or has no known range.</param>
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
        if (valueType == typeof(bool))
        {
            range = new ValueRange(0, 1);
        }
        else if (options is { Count: > 0 })
        {
            range = new ValueRange(0, options.Count - 1);
        }
        else if (options is null && IsNumber(valueType))
        {
            var minimum = ToDecimal(dataType.Min) ?? MinimumOf(valueType);
            var maximum = ToDecimal(dataType.Max) ?? MaximumOf(valueType);
            if (minimum is { } min && maximum is { } max && min <= max)
                range = new ValueRange(min, max);
        }

        object? defaultValue = dataType.OptionsDefault
            ?? (valueType == typeof(bool) ? false
                : range is { } known ? known.Clamp(0)
                : null);
        // the scale of a choice is of its physical values, not of the option numbers
        return new ParameterInfo(field.Field.Name, valueType, range, options is null ? dataType.Scale ?? 1 : 1, options) { Default = defaultValue };
    }

    /// <summary>
    /// Converts a value for the parameter (e.g. a decimal of a knob) to its value type: a number is on when it is not 0,
    /// and the option with that number for a choice.
    /// </summary>
    public object ToValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.GetType() == ValueType)
            return value;
        if (Options is { Count: > 0 } options && value is not string)
            return options[Math.Clamp((int)Math.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture)), 0, options.Count - 1)];
        return Convert.ChangeType(value, ValueType, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A logical value of the parameter as a number: a number itself, a bool as 1 (on) or 0 (off), an option of a choice as
    /// its number; null for any other value (a text, an option the choice does not have).
    /// </summary>
    public decimal? ToNumber(object? value)
        => value switch
        {
            null => null,
            string option when Options is { } options && IndexOf(options, option) is >= 0 and var index => index,
            string => null,
            bool on => on ? 1 : 0,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        };

    /// <summary>The name of an option by its number; null for a parameter without options or a number it does not have.</summary>
    public string? OptionAt(decimal? number)
        => Options is { } options && number is { } known && known == Math.Truncate(known) && known >= 0 && known < options.Count
            ? options[(int)known]
            : null;

    private static int IndexOf(IReadOnlyList<string> options, string option)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (options[index] == option)
                return index;
        }
        return -1;
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
