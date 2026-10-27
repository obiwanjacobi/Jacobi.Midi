using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// An open dashboard on an open rig: its bindings resolved against the rig and its schemas, and a control value per
/// bound slot: a <see cref="ParameterValue"/> of the rig's device states, or a <see cref="VariableValue"/> for a slot
/// bound to an index variable. The session holds the current index of each variable; when one changes, the bindings
/// are resolved again and the parameter values that use it are pointed to the new repeat item. Everything that does
/// not fit is collected in <see cref="Problems"/>; the control of a slot without value shows it as not bound.
/// </summary>
public sealed class DashboardSession : IDisposable
{
    private readonly Dictionary<(ParameterControl Control, string Slot), IControlValue> _values = [];
    private readonly Dictionary<VariableReference, VariableValue> _variables = [];
    private readonly Dictionary<VariableReference, int> _indices = [];
    private readonly Dictionary<VariableReference, int> _startIndices = [];

    private DashboardSession(RigSession rig, Dashboard dashboard)
    {
        Rig = rig;
        Dashboard = dashboard;
    }

    public RigSession Rig { get; }

    public Dashboard Dashboard { get; }

    public BindingResolution Resolution { get; private set; } = new([], new Dictionary<VariableReference, int>(), []);

    /// <summary>The problems of the dashboard (its bindings and variables); those of the rig are in <see cref="RigSession.Problems"/>.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>The bindings were resolved again (an index variable changed); <see cref="Problems"/> may have changed.</summary>
    public event EventHandler? Resolved;

    public static DashboardSession Open(RigSession rig, Dashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(dashboard);

        var session = new DashboardSession(rig, dashboard);
        session.CollectStartIndices(dashboard.Layout);
        session.Resolve();
        return session;
    }

    /// <summary>The value of a slot of a control; null when the slot is not bound or its binding has a problem.</summary>
    public IControlValue? ValueOf(ParameterControl control, string slot)
        => _values.GetValueOrDefault((control, slot));

    /// <summary>The device state of a device of the dashboard; null when the rig does not have it.</summary>
    public DeviceState? DeviceOf(string alias) => Rig.Devices.GetValueOrDefault(alias);

    /// <summary>The colour of a device: the dashboard's, else the rig's; null when neither gives one.</summary>
    public string? ColorOf(string alias)
        => Dashboard.FindDevice(alias)?.Color ?? Rig.ColorOf(alias);

    /// <summary>The current (zero based) index of a variable.</summary>
    public int IndexOf(VariableReference variable)
        => _indices.TryGetValue(variable, out var index) ? index : _startIndices.GetValueOrDefault(variable);

    /// <summary>
    /// Selects another repeat item with a variable, kept within its items: the controls that use it show that item.
    /// </summary>
    public void SetIndex(VariableReference variable, int index)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (Resolution.ItemCounts.TryGetValue(variable, out var count))
            index = Math.Clamp(index, 0, count - 1);
        else
            index = Math.Max(0, index);
        if (index == IndexOf(variable))
            return;

        _indices[variable] = index;
        Resolve();
        if (_variables.TryGetValue(variable, out var value))
            value.RaiseChanged();
        Resolved?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        foreach (var value in _values.Values.OfType<ParameterValue>())
            value.Dispose();
        _values.Clear();
    }

    private void CollectStartIndices(LayoutControl layout)
    {
        foreach (var variable in layout.Variables)
            _startIndices[new VariableReference(layout, variable.Name)] = variable.Value;
        foreach (var child in layout.Children.OfType<LayoutControl>())
            CollectStartIndices(child);
    }

    // resolves the bindings with the current indices; existing values are kept and pointed to their new parameter
    private void Resolve()
    {
        var resolution = BindingResolver.Resolve(Dashboard, new BindingContext
        {
            Rig = Rig.Rig,
            Library = Rig.Library.IsCompiled ? Rig.Library : null,
            Values = _indices,
        });
        Resolution = resolution;
        var problems = resolution.Problems.Select(Describe).ToList();

        foreach (var binding in resolution.Bindings)
        {
            var key = (binding.Control, binding.Slot);
            if (binding.Variable is { } variable)
            {
                if (!_variables.TryGetValue(variable, out var variableValue))
                {
                    variableValue = new VariableValue(this, variable, resolution.ItemCounts.TryGetValue(variable, out var count) ? count : null);
                    _variables[variable] = variableValue;
                }
                _values[key] = variableValue;
                continue;
            }

            var parameter = binding.Parameter!;
            if (_values.GetValueOrDefault(key) is ParameterValue existing)
            {
                existing.Retarget(parameter);
                continue;
            }
            if (DeviceOf(parameter.Device) is not { } state)
                continue;   // the device is not in the rig: reported by the resolver
            if (state.Plan.Find(parameter.Path) is not FieldInfo field)
            {
                problems.Add($"{Describe(binding.Control)}.{binding.Slot}: '{parameter.Path}' is not a field of '{state.MapRoot}'.");
                continue;
            }
            _values[key] = new ParameterValue(state, parameter, ParameterInfo.From(field));
        }

        Problems = problems;
    }

    private static string Describe(BindingProblem problem)
        => problem.Control is null
            ? problem.Message
            : $"{Describe(problem.Control)}{(problem.Slot is null ? "" : $".{problem.Slot}")}: {problem.Message}";

    private static string Describe(DashboardControl control)
        => control.Label ?? control switch
        {
            ParameterControl parameter => parameter.Kind.ToString(),
            LayoutControl layout => layout.Kind.ToString(),
            _ => control.GetType().Name,
        };
}
