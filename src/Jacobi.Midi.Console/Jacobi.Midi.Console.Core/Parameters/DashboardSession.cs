using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Palettes;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// An open dashboard on an open rig: its bindings resolved against the rig and its schemas, and a control value per
/// bound slot: a <see cref="ParameterValue"/> of the rig's device states, or a <see cref="VariableValue"/> for a slot
/// bound to an index variable. The session holds the current index of each variable; when one changes, the bindings
/// are resolved again and the parameter values that use it are pointed to the new repeat item. Everything that does
/// not fit is collected in <see cref="Problems"/>, as is a parameter its control cannot show
/// (<see cref="ControlCatalog.Accepts"/>); the control of a slot without value shows it as not bound.
/// After an edit of the dashboard (<see cref="Refresh"/>) the bindings are resolved again and the values follow them.
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

    /// <summary>The bindings were resolved again (an index variable changed, an edit); <see cref="Problems"/> may have changed.</summary>
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

    /// <summary>The colours of a device: per area the dashboard's, else the rig's; none where neither gives one.</summary>
    public AreaColors ColorsOf(string alias)
        => (Dashboard.FindDevice(alias)?.Colors ?? AreaColors.None).Over(Rig.ColorsOf(alias));

    /// <summary>
    /// The device of the control's first bound slot: the device whose colour the control has when no colour is given
    /// for it or a layout control around it. Null when no slot is bound to a device parameter.
    /// </summary>
    public string? SlotDeviceOf(ParameterControl control)
        => Resolution.Bindings.FirstOrDefault(binding => binding.Control == control && binding.Parameter is not null)?.Parameter!.Device;

    /// <summary>
    /// The parameters of a device the controls are bound to: their schema paths from the map root without repeat
    /// indices (as <see cref="ParameterNode.Path"/>), so a parameter counts for every repeat item.
    /// </summary>
    public IReadOnlySet<string> UsedPaths(string alias)
        => Resolution.Bindings
            .Select(binding => binding.Parameter)
            .Where(parameter => parameter is not null && String.Equals(parameter.Device, alias, StringComparison.OrdinalIgnoreCase))
            .Select(parameter => parameter!.Path)
            .ToHashSet(StringComparer.Ordinal);

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

    /// <summary>
    /// Resolves the dashboard again after it was edited: the slots get the values of their (new) bindings, a slot that
    /// is no longer bound loses its value; the variables keep their current index while they are declared, a new one
    /// starts at its starting value.
    /// </summary>
    public void Refresh()
    {
        _startIndices.Clear();
        CollectStartIndices(Dashboard.Layout);
        foreach (var variable in _indices.Keys.Where(variable => !_startIndices.ContainsKey(variable)).ToList())
            _indices.Remove(variable);
        Resolve();
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

    // resolves the bindings with the current indices; existing values are kept and pointed to their new parameter (of
    // the same field of the same device; otherwise the slot gets a new value), values of slots no longer bound are dropped
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

        var bound = resolution.Bindings.Select(binding => (binding.Control, binding.Slot)).ToHashSet();
        foreach (var key in _values.Keys.Where(key => !bound.Contains(key)).ToList())
            Drop(key);
        foreach (var variable in _variables.Keys.Where(variable => !_startIndices.ContainsKey(variable)).ToList())
            _variables.Remove(variable);

        foreach (var binding in resolution.Bindings)
        {
            var key = (binding.Control, binding.Slot);
            if (binding.Variable is { } variable)
            {
                int? count = resolution.ItemCounts.TryGetValue(variable, out var items) ? items : null;
                if (!_variables.TryGetValue(variable, out var variableValue) || variableValue.Info.Range?.Maximum != count)
                {
                    variableValue = new VariableValue(this, variable, count);
                    _variables[variable] = variableValue;
                }
                if (ControlCatalog.RefusalOf(binding.Control.Kind, variableValue.Info) is { } refusal)
                {
                    Drop(key);
                    problems.Add($"{Describe(binding.Control)}.{binding.Slot}: {refusal}");
                    continue;
                }
                if (_values.GetValueOrDefault(key) != variableValue)
                {
                    Drop(key);
                    _values[key] = variableValue;
                }
                continue;
            }

            var parameter = binding.Parameter!;
            if (_values.GetValueOrDefault(key) is ParameterValue existing && existing.CanRetarget(parameter))
            {
                existing.Retarget(parameter);
                continue;
            }
            Drop(key);
            if (DeviceOf(parameter.Device) is not { } state)
                continue;   // the device is not in the rig: reported by the resolver
            if (state.Plan.Find(parameter.Path) is not FieldInfo field)
            {
                problems.Add($"{Describe(binding.Control)}.{binding.Slot}: '{parameter.Path}' is not a field of '{state.MapRoot}'.");
                continue;
            }
            var info = ParameterInfo.From(field);
            if (ControlCatalog.RefusalOf(binding.Control.Kind, info) is { } refused)
            {
                problems.Add($"{Describe(binding.Control)}.{binding.Slot}: {refused}");
                continue;
            }
            _values[key] = new ParameterValue(state, parameter, info);
        }

        Problems = problems;
    }

    private void Drop((ParameterControl Control, string Slot) key)
    {
        if (_values.Remove(key, out var value) && value is ParameterValue parameterValue)
            parameterValue.Dispose();
    }

    private static string Describe(BindingProblem problem)
        => problem.Control is null
            ? problem.Message
            : $"{Describe(problem.Control)}{(problem.Slot is null ? "" : $".{problem.Slot}")}: {problem.Message}";

    private static string Describe(DashboardControl control) => ControlCatalog.NameOf(control);
}
