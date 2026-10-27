using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// Resolves the bindings of all parameter controls of a dashboard into parameter references, applying the
/// inheritance of <see cref="DashboardControl.Device"/>, <see cref="DashboardControl.Base"/> and the index variables
/// of the layout controls:
/// <list type="bullet">
/// <item>the device is the binding's own, else the nearest one of the control and the layout controls around it;</item>
/// <item>a binding with its own device has a full path; otherwise its path is appended to the nearest base, if any;</item>
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

        CheckDevices();
        var maps = MapsOf(context.Library);
        Visit(dashboard.Layout, new Scope(null, null, new Dictionary<string, VariableReference>(StringComparer.Ordinal)));
        CheckValues();
        return new BindingResolution(bindings, itemCounts, problems);

        void CheckDevices()
        {
            if (context.Rig is not { } rig)
                return;
            foreach (var device in dashboard.Devices)
            {
                if (rig.Find(device.Alias) is not { } instance)
                    problems.Add(new(null, null, $"The device '{device.Alias}' is not in the rig."));
                else if (!new SchemaName(instance.MapRoot).Equals(new SchemaName(device.MapRoot)))
                    problems.Add(new(null, null, $"The device '{device.Alias}' of the rig is a '{instance.MapRoot}', the dashboard needs a '{device.MapRoot}'."));
            }
        }

        AddressMapAdapter? MapsOf(SchemaLibrary? library)
        {
            if (library is null)
                return null;
            if (library.Maps is null)
            {
                problems.Add(new(null, null, "The schemas do not compile; the bindings are not checked against them."));
                return null;
            }
            foreach (var device in dashboard.Devices)
            {
                if (!library.Maps.IsMap(new SchemaName(device.MapRoot)))
                    problems.Add(new(null, null, $"'{device.MapRoot}' (device '{device.Alias}') is not an address map in the schemas."));
            }
            return library.Maps;
        }

        void Visit(DashboardControl control, Scope outer)
        {
            var scope = new Scope(control.Device ?? outer.Device, control.Base ?? outer.Base, outer.Variables);
            switch (control)
            {
                case LayoutControl layout:
                    if (layout.Variables.Count > 0)
                    {
                        var variables = new Dictionary<string, VariableReference>(outer.Variables, StringComparer.Ordinal);
                        foreach (var variable in layout.Variables)
                        {
                            var reference = new VariableReference(layout, variable.Name);
                            declarations[reference] = variable;
                            variables[variable.Name] = reference;
                        }
                        scope = scope with { Variables = variables };
                    }
                    foreach (var child in layout.Children)
                        Visit(child, scope);
                    break;

                case ParameterControl parameter:
                    foreach (var (slot, binding) in parameter.Slots)
                        ResolveSlot(parameter, slot, binding, scope);
                    break;
            }
        }

        void ResolveSlot(ParameterControl control, string slot, Binding binding, Scope scope)
        {
            if (binding.Variable is { } name)
            {
                if (scope.Variables.TryGetValue(name, out var variable))
                    bindings.Add(new(control, slot, null, variable, [variable]));
                else
                    problems.Add(new(control, slot, $"The index variable '{SchemaPaths.VariablePrefix}{name}' is not declared on a layout control around the control."));
                return;
            }

            var device = binding.Device ?? scope.Device;
            if (device is null)
            {
                problems.Add(new(control, slot, $"'{binding.Path}' has no device: set the device on the binding, the control or a layout control around it."));
                return;
            }
            if (dashboard.FindDevice(device) is not { } dashboardDevice)
            {
                problems.Add(new(control, slot, $"'{binding.Path}' refers to device '{device}', which the dashboard does not list."));
                return;
            }

            var text = binding.Device is null && scope.Base is { } basePath
                ? $"{basePath}.{binding.Path}"
                : binding.Path;
            int? Lookup(string name) => scope.Variables.TryGetValue(name, out var reference) ? ValueOf(reference) : null;
            if (!SchemaPaths.TryParse(text, Lookup, out var parsed, out var error))
            {
                problems.Add(new(control, slot, error));
                return;
            }

            var variables = parsed.Variables.Select(use => scope.Variables[use.Variable]).Distinct().ToList();
            if (maps is not null && !Check(control, slot, dashboardDevice, parsed, scope))
                return;
            bindings.Add(new(control, slot, new ParameterReference(device, parsed.Path, parsed.Instance), null, variables));
        }

        // the path must be a field of the device's map; each variable must index a repeat of the map
        bool Check(ParameterControl control, string slot, DashboardDevice device, ParsedPath parsed, Scope scope)
        {
            var mapRoot = new SchemaName(device.MapRoot);
            var root = parsed.Path.Split(SchemaPath.Separator)[0];
            if (!String.Equals(root, mapRoot.Name, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new(control, slot, $"'{parsed.Path}' must start at the map root '{mapRoot.Name}' of device '{device.Alias}'."));
                return false;
            }
            if (!context.Library!.Schemas!.TryFindByPath(new SchemaPath($"{mapRoot.Namespace}.{parsed.Path}"), out var node) || node is not SchemaField)
            {
                problems.Add(new(control, slot, $"'{parsed.Path}' is not a field of '{device.MapRoot}'."));
                return false;
            }

            foreach (var use in parsed.Variables)
            {
                var reference = scope.Variables[use.Variable];
                int? count;
                try
                {
                    count = maps!.ItemCount(mapRoot, new SchemaPath(use.Repeat));
                }
                catch (AddressMapException exception)
                {
                    problems.Add(new(control, slot, $"The address map '{device.MapRoot}' is not valid: {exception.Message}"));
                    return false;
                }
                if (count is null)
                {
                    problems.Add(new(control, slot, $"'{SchemaPaths.VariablePrefix}{use.Variable}' indexes '{use.Repeat}', which is not a repeat of '{device.MapRoot}'."));
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

            // a control that changes a variable needs its range: the item count of the repeat it indexes in a path
            if (maps is null)
                return;
            foreach (var binding in bindings.Where(binding => binding.Variable is { } variable && !itemCounts.ContainsKey(variable)))
                problems.Add(new(binding.Control, binding.Slot, $"'{SchemaPaths.VariablePrefix}{binding.Variable!.Name}' indexes no repeat in a binding path: its number of items is not known."));
        }

        int ValueOf(VariableReference reference)
            => context.Values is { } values && values.TryGetValue(reference, out var value)
                ? value
                : declarations[reference].Value;
    }

    private readonly record struct Scope(string? Device, string? Base, IReadOnlyDictionary<string, VariableReference> Variables);
}

/// <summary>
/// What a dashboard is resolved with, besides the dashboard itself: all optional.
/// </summary>
public sealed class BindingContext
{
    public static BindingContext None { get; } = new();

    /// <summary>The current values of index variables, as set in the UI; a variable without one has its starting value.</summary>
    public IReadOnlyDictionary<VariableReference, int>? Values { get; init; }

    /// <summary>The rig the dashboard is opened with: its devices are matched by alias and map root.</summary>
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
    IReadOnlyList<BindingProblem> Problems);
