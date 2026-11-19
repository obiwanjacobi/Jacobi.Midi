using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// Resolves the bindings of all parameter controls of a dashboard into parameter references, applying the
/// inheritance of <see cref="DashboardControl.Device"/>, <see cref="DashboardControl.Base"/> and the index variables
/// of the layout controls:
/// <list type="bullet">
/// <item>the device is the binding's own, else the nearest one of the control and the layout controls around it
/// (<see cref="BindingScope"/>);</item>
/// <item>a binding with its own device has a full path; otherwise its path is appended to the nearest base, if any;
/// so is a base (<see cref="BindingScope.FullBaseOf"/>);</item>
/// <item>the repeat indices in the base and the path give the instance; an index variable (<c>[$patch]</c>) is the
/// nearest declaration, with its current value (<see cref="BindingContext.Values"/>) or its starting value.</item>
/// </list>
/// With a <see cref="BindingContext.Rig"/> the dashboard's devices are matched with the rig; with a
/// <see cref="BindingContext.Library"/> the bindings are checked against the schemas and the number of items each
/// index variable can select is taken from the address maps. Resolve again after a variable changed: the bindings
/// that use it (<see cref="SlotBinding.Variables"/>) get another instance.
/// Everything that cannot be resolved is reported as a problem, never skipped silently.
/// </summary>
public static class BindingResolver
{
    public static BindingResolution Resolve(Dashboard dashboard, BindingContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        context ??= BindingContext.None;

        var bindings = new List<SlotBinding>();
        var problems = new List<BindingProblem>();
        var declarations = new Dictionary<VariableReference, IndexVariable>();
        var itemCounts = new Dictionary<VariableReference, int>();
        var repeats = new Dictionary<VariableReference, string>();

        CheckDevices();
        var library = LibraryOf(context.Library);
        Visit(dashboard.Layout, BindingScope.None);
        CheckValues();
        return new BindingResolution(bindings, itemCounts, problems) { Repeats = repeats };

        void CheckDevices()
        {
            if (context.Rig is not { } rig)
                return;
            foreach (var problem in dashboard.DeviceProblems(rig))
                problems.Add(new(null, null, problem));
        }

        // the library the bindings are checked against: compiled, with the roots of the devices
        SchemaLibrary? LibraryOf(SchemaLibrary? candidate)
        {
            if (candidate is null)
                return null;
            if (!candidate.IsCompiled)
            {
                problems.Add(new(null, null, "The schemas do not compile; the bindings are not checked against them."));
                return null;
            }
            foreach (var device in dashboard.Devices)
            {
                if (!candidate.HasSchema(device.Schema))
                    problems.Add(new(null, null, $"'{device.Schema}' (device '{device.Alias}') is not a schema with root groups."));
            }
            return candidate;
        }

        void Visit(DashboardControl control, BindingScope outer)
        {
            var scope = outer.Inside(control);
            if (control.Base is not null)
                ResolveBase(control, scope);
            switch (control)
            {
                case LayoutControl layout:
                    foreach (var variable in layout.Variables)
                        declarations[new VariableReference(layout, variable.Name)] = variable;
                    break;

                case ParameterControl parameter:
                    foreach (var (slot, binding) in parameter.Slots)
                        ResolveSlot(parameter, slot, binding, scope);
                    break;
            }
            foreach (var child in control.Children)
                Visit(child, scope);
        }

        void ResolveSlot(ParameterControl control, string slot, Binding binding, BindingScope scope)
        {
            if (binding.Variable is { } name)
            {
                if (scope.Variables.TryGetValue(name, out var variable))
                    bindings.Add(new(control, slot, null, variable, [variable]));
                else
                    problems.Add(new(control, slot, $"The index variable '{SchemaPaths.VariablePrefix}{name}' is not declared on a layout control around the control."));
                return;
            }

            var full = scope.FullPathOf(binding);
            if (Locate(control, slot, binding.Path, full, scope) is not var (dashboardDevice, parsed))
                return;
            var device = full!.Value.Device;

            var variables = parsed.Variables.Select(use => scope.Variables[use.Variable]).Distinct().ToList();
            AddRepeats(parsed, scope);
            if (library is not null && !Check(control, slot, dashboardDevice, parsed, scope))
                return;
            bindings.Add(new(control, slot, new ParameterReference(device, parsed.Path, parsed.Instance), null, variables));
        }

        // the base of a control must be a node of the device's schema (checked again on each resolve: e.g. a placed user
        // control whose base becomes valid when the base around it is set); the variables in it index repeats
        // (a base without device or with an undeclared variable is not reported here: each binding below it reports that)
        void ResolveBase(DashboardControl control, BindingScope scope)
        {
            if (!SchemaPaths.TryParse(scope.Base, _ => 0, out var parsed, out _))
                return;
            AddRepeats(parsed, scope);
            if (library is not null && scope.Device is { } device && dashboard.FindDevice(device) is { } dashboardDevice
                && RootOf(control, null, dashboardDevice, parsed) is not null
                && library.FindNode(dashboardDevice.Schema, parsed.Path) is null)
                problems.Add(new(control, null, $"The base '{parsed.Path}' is not a node of '{dashboardDevice.Schema}'."));
        }

        // the device and the parsed full path of a binding or base (as written: text); null when it has no device, the
        // device is not listed or the path is not valid (reported)
        (DashboardDevice Device, ParsedPath Path)? Locate(DashboardControl control, string? slot, string text, (string Device, string Path)? full, BindingScope scope)
        {
            if (full is not var (device, path))
            {
                problems.Add(new(control, slot, $"'{text}' has no device: set the device on the binding, the control or a layout control around it."));
                return null;
            }
            if (dashboard.FindDevice(device) is not { } dashboardDevice)
            {
                problems.Add(new(control, slot, $"'{text}' refers to device '{device}', which the dashboard does not list."));
                return null;
            }

            int? Lookup(string name) => scope.Variables.TryGetValue(name, out var reference) ? ValueOf(reference) : null;
            if (!SchemaPaths.TryParse(path, Lookup, out var parsed, out var error))
            {
                problems.Add(new(control, slot, error));
                return null;
            }
            return (dashboardDevice, parsed);
        }

        // the root of the device's schema the path starts at; null when there is none (reported)
        SchemaName? RootOf(DashboardControl control, string? slot, DashboardDevice device, ParsedPath parsed)
        {
            var root = library!.RootOf(device.Schema, parsed.Path);
            if (root is null)
                problems.Add(new(control, slot, $"'{parsed.Path}' must start at a root of the schema '{device.Schema}' of device '{device.Alias}'."));
            return root;
        }

        // the path must be a field of one of the device's roots (the roots of its schema); each variable must index a
        // repeat of that root
        bool Check(ParameterControl control, string slot, DashboardDevice device, ParsedPath parsed, BindingScope scope)
        {
            if (RootOf(control, slot, device, parsed) is not { } root)
                return false;
            if (library!.FindNode(device.Schema, parsed.Path) is not SchemaField)
            {
                problems.Add(new(control, slot, $"'{parsed.Path}' is not a field of '{device.Schema}'."));
                return false;
            }

            foreach (var use in parsed.Variables)
            {
                var reference = scope.Variables[use.Variable];
                int? count;
                try
                {
                    count = library.ItemCount(root, use.Repeat);
                }
                catch (MessageMapException exception)
                {
                    problems.Add(new(control, slot, $"The address map '{root}' is not valid: {exception.Message}"));
                    return false;
                }
                if (count is null)
                {
                    // a repeat whose count only the data gives (e.g. a list resource): the variable has no range yet
                    if (library.Serializer?.GetPlan(root).Find(new SchemaPath(use.Repeat)) is RepeatInfo)
                        continue;
                    problems.Add(new(control, slot, $"'{SchemaPaths.VariablePrefix}{use.Variable}' indexes '{use.Repeat}', which is not a repeat of '{root}'."));
                    return false;
                }
                if (itemCounts.TryGetValue(reference, out var known) && known != count)
                {
                    problems.Add(new(control, slot, $"'{SchemaPaths.VariablePrefix}{use.Variable}' indexes repeats with {known} and {count} items."));
                    count = Math.Min(known, count.Value);
                }
                itemCounts[reference] = count.Value;
            }
            return true;
        }

        void CheckValues()
        {
            foreach (var (reference, count) in itemCounts)
            {
                var value = ValueOf(reference);
                if (value >= count)
                    problems.Add(new(reference.Owner, null, $"'{SchemaPaths.VariablePrefix}{reference.Name}' is {value}, but there are only {count} items (0-{count - 1})."));
            }

            // a control that changes a variable needs a repeat it indexes in a path; its range is the item count of the
            // repeat, or none yet when only the data gives the count
            if (library is null)
                return;
            foreach (var binding in bindings.Where(binding => binding.Variable is { } variable && !itemCounts.ContainsKey(variable) && !repeats.ContainsKey(variable)))
                problems.Add(new(binding.Control, binding.Slot, $"'{SchemaPaths.VariablePrefix}{binding.Variable!.Name}' indexes no repeat in a binding path."));
        }

        // the first repeat a declared variable is seen to index
        void AddRepeats(ParsedPath parsed, BindingScope scope)
        {
            foreach (var use in parsed.Variables)
            {
                if (scope.Variables.TryGetValue(use.Variable, out var reference))
                    repeats.TryAdd(reference, use.Repeat);
            }
        }

        int ValueOf(VariableReference reference)
            => context.Values is { } values && values.TryGetValue(reference, out var value)
                ? value
                : declarations[reference].Value;
    }

}

/// <summary>
/// What a dashboard is resolved with, besides the dashboard itself: all optional.
/// </summary>
public sealed class BindingContext
{
    public static BindingContext None { get; } = new();

    /// <summary>The current values of index variables, as set in the UI; a variable without one has its starting value.</summary>
    public IReadOnlyDictionary<VariableReference, int>? Values { get; init; }

    /// <summary>The rig the dashboard is opened with: its devices are matched by alias and schema.</summary>
    public Rig? Rig { get; init; }

    /// <summary>The compiled schemas: the bindings are checked against them and the item counts taken from the address maps.</summary>
    public SchemaLibrary? Library { get; init; }
}

/// <summary>An index variable: the layout control that declares it and its name.</summary>
public sealed record VariableReference(LayoutControl Owner, string Name);

/// <summary>
/// A resolved slot: the control and the slot name, what it is bound to (a device <see cref="Parameter"/> or an index
/// <see cref="Variable"/>: exactly one of them) and the index variables it depends on (a variable binding depends on
/// its variable).
/// </summary>
public sealed record SlotBinding(ParameterControl Control, string Slot, ParameterReference? Parameter, VariableReference? Variable,
    IReadOnlyList<VariableReference> Variables);

/// <summary>Something that could not be resolved: the control and slot (if any) and the reason.</summary>
public sealed record BindingProblem(DashboardControl? Control, string? Slot, string Message);

/// <summary>
/// The outcome of <see cref="BindingResolver.Resolve"/>: the resolved slots, the number of items each index variable
/// can select (only with a schema library) and the problems.
/// </summary>
public sealed record BindingResolution(
    IReadOnlyList<SlotBinding> Bindings,
    IReadOnlyDictionary<VariableReference, int> ItemCounts,
    IReadOnlyList<BindingProblem> Problems)
{
    /// <summary>
    /// The repeat each index variable indexes (the schema path of the repeat without indices, e.g.
    /// <c>AddressMap.Memory</c>), as the bindings and bases use it; a variable that indexes nothing is left out.
    /// </summary>
    public IReadOnlyDictionary<VariableReference, string> Repeats { get; init; } = new Dictionary<VariableReference, string>();
}
