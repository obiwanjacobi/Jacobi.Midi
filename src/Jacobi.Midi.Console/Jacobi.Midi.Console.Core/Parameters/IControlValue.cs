namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// What a parameter control shows and changes: a device parameter (<see cref="ParameterValue"/>) or an index variable
/// of the dashboard (<see cref="VariableValue"/>). A control does not know which one it has.
/// </summary>
public interface IControlValue
{
    /// <summary>The kind of value, its range, step, options and default.</summary>
    ParameterInfo Info { get; }

    /// <summary>The logical value; null when it is unknown.</summary>
    object? Value { get; }

    /// <summary>The value of a number as decimal; null when unknown or not a number.</summary>
    decimal? Number { get; }

    /// <summary>Sets the value, converted to the value type of <see cref="Info"/>.</summary>
    void Set(object value);

    /// <summary>The value changed, or the value now stands for another parameter; raised on the thread of the change.</summary>
    event EventHandler? Changed;
}
