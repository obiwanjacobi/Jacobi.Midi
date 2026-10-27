using System.Globalization;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// One parameter of a device state, as a control sees it: the value (null when unknown), its info from the schema,
/// writing a new value (origin <see cref="ValueOrigin.User"/>) and a notification when the value changes, whoever
/// changed it. When an index variable of its binding changes, the dashboard session points it to the parameter of the
/// other repeat item (<see cref="Retarget"/>); the control then shows that parameter.
/// </summary>
public sealed class ParameterValue : IControlValue, IDisposable
{
    private readonly DeviceState _state;

    internal ParameterValue(DeviceState state, ParameterReference parameter, ParameterInfo info)
    {
        _state = state;
        Parameter = parameter;
        Info = info;
        _state.ValueChanged += OnValueChanged;
    }

    public ParameterReference Parameter { get; private set; }

    public ParameterInfo Info { get; }

    public object? Value => _state.TryGetValue(Parameter, out var known) ? known.Value : null;

    public decimal? Number => Value is { } value and not bool and not string
        ? Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        : null;

    public event EventHandler? Changed;

    public void Set(object value)
    {
        var converted = Info.ToValue(value);
        _state.SetValue(Parameter.Path, Parameter.Instance, new LogicalField(Info.Name, Info.ValueType, converted), ValueOrigin.User);
    }

    public void Dispose() => _state.ValueChanged -= OnValueChanged;

    /// <summary>Points the value to another parameter (another repeat item); <see cref="Changed"/> is raised.</summary>
    internal void Retarget(ParameterReference parameter)
    {
        if (parameter == Parameter)
            return;
        Parameter = parameter;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnValueChanged(object? sender, ValueChangedEventArgs change)
    {
        if (change.Path == Parameter.Path && change.Instance == Parameter.Instance)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
