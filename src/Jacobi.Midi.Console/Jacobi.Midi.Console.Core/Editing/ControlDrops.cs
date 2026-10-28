using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;

namespace Jacobi.Midi.Console.Core.Editing;

/// <summary>
/// Plans what a parameter or group of the parameter tree does when it is dropped on a dashboard; nothing is changed yet
/// (the console applies the <see cref="DropPlan"/> as one edit).
/// <list type="bullet">
/// <item>A field becomes the suggested control (<see cref="ControlCatalog.Suggest"/>), bound to it.</item>
/// <item>A group or repeat becomes a section with its <c>device</c> and <c>base</c>, a control per field and a nested
/// section per group in it, recursively, laid out by <see cref="GridLayout.Complete"/>.</item>
/// <item>A field dropped on a slot of a control binds that slot.</item>
/// </list>
/// The binding is written in the scope of its place (<see cref="BindingScope.BindingOf"/>): relative to the base around
/// it when it can be. A repeat in the path gets the index the base around it gives, else an index variable in scope
/// that indexes the same repeat (<see cref="BindingResolution.Repeats"/>), else a new variable (named after the repeat,
/// starting at the first item): declared on the nearest grid or section around the drop for a field, on the new section
/// itself for a group, with a number box to select the item. A device the dashboard does not list yet is added.
/// </summary>
public static class ControlDrops
{
    /// <summary>The control a parameter or group becomes when it is dropped in a layout control.</summary>
    public static DropPlan Drop(Dashboard dashboard, BindingResolution resolution, DropSource source, LayoutControl container)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(container);
        var scope = BindingScope.Of(dashboard.Layout, container)
            ?? throw new ArgumentException("The layout control is not in the dashboard.", nameof(container));

        var declared = new List<DeclaredVariable>();
        var repeats = new Dictionary<VariableReference, string>(resolution.Repeats);
        DashboardControl control;
        if (source.Node.Kind == ParameterNodeKind.Field)
        {
            var indexer = new Indexer(scope, source.Device, repeats, HostAround(dashboard.Layout, container), declared);
            var parameter = ControlCatalog.Create(ControlCatalog.Suggest(source.Node.Info!));
            parameter.Slots = new Dictionary<string, Binding>(StringComparer.Ordinal)
            {
                [FirstSlotOf(parameter)] = scope.BindingOf(source.Device, indexer.PathOf(source.Node)),
            };
            control = parameter;
        }
        else
        {
            control = Section(source.Node, source.Device, scope, repeats);
            GridLayout.Complete((LayoutControl)control);
        }
        return new DropPlan
        {
            Control = control,
            Variables = declared,
            Device = DeviceToAdd(dashboard, source),
        };
    }

    /// <summary>The binding of a slot of a control that a field is dropped on.</summary>
    public static DropPlan Bind(Dashboard dashboard, BindingResolution resolution, DropSource source, ParameterControl control)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(control);
        if (source.Node.Kind != ParameterNodeKind.Field)
            throw new ArgumentException("Only a field is bound to a slot.", nameof(source));
        var scope = BindingScope.Of(dashboard.Layout, control)
            ?? throw new ArgumentException("The control is not in the dashboard.", nameof(control));

        var declared = new List<DeclaredVariable>();
        var host = GridLayout.ParentOf(dashboard.Layout, control) is { } parent ? HostAround(dashboard.Layout, parent) : dashboard.Layout;
        var indexer = new Indexer(scope, source.Device, new Dictionary<VariableReference, string>(resolution.Repeats), host, declared);
        return new DropPlan
        {
            Slot = FirstSlotOf(control),
            Binding = scope.BindingOf(source.Device, indexer.PathOf(source.Node)),
            Variables = declared,
            Device = DeviceToAdd(dashboard, source),
        };
    }

    /// <summary>
    /// The slot a dropped field binds: the first slot the control shows that is not bound yet (an XY pad: <c>x</c>, then
    /// <c>y</c>; an envelope: the time and level of each segment in turn), the first one when all are bound.
    /// </summary>
    public static string FirstSlotOf(ParameterControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var shown = SlotNames.Shown(control);
        return shown.FirstOrDefault(slot => !control.Slots.ContainsKey(slot)) ?? shown[0];
    }

    // a section for a group or repeat: its base indexes the repeats, the variables it needs are its own
    private static LayoutControl Section(ParameterNode node, string device, BindingScope outer, Dictionary<VariableReference, string> repeats)
    {
        var section = new LayoutControl { Kind = LayoutControlKind.Section, Label = node.Name };
        var own = new List<DeclaredVariable>();
        var basePath = new Indexer(outer, device, repeats, section, own).PathOf(node);
        section.Base = basePath;
        if (!String.Equals(outer.Device, device, StringComparison.OrdinalIgnoreCase))
            section.Device = device;
        section.Variables = [.. own.Select(variable => variable.Variable)];

        var scope = outer.Inside(section);
        var children = new List<DashboardControl>();
        // a number box selects the item of each repeat the section declared a variable for
        foreach (var variable in own)
        {
            var numberBox = ControlCatalog.Create(ParameterControlKind.NumberBox);
            numberBox.Label = repeats[new VariableReference(section, variable.Variable.Name)].Split('.')[^1];
            numberBox.Slots = new Dictionary<string, Binding> { [SlotNames.Value] = new Binding($"{SchemaPaths.VariablePrefix}{variable.Variable.Name}") };
            children.Add(numberBox);
        }
        foreach (var child in node.Children)
        {
            if (child.Kind == ParameterNodeKind.Field)
            {
                var parameter = ControlCatalog.Create(ControlCatalog.Suggest(child.Info!));
                parameter.Slots = new Dictionary<string, Binding>(StringComparer.Ordinal)
                {
                    [FirstSlotOf(parameter)] = scope.BindingOf(device, $"{basePath}.{child.Name}"),
                };
                children.Add(parameter);
            }
            else
            {
                children.Add(Section(child, device, scope, repeats));
            }
        }
        section.Children = children;
        return section;
    }

    // where a field's new variables are declared: the nearest grid or section at or around the container
    private static LayoutControl HostAround(LayoutControl root, LayoutControl container)
    {
        for (LayoutControl? layout = container; layout is not null; layout = GridLayout.ParentOf(root, layout))
        {
            if (GridLayout.IsGrid(layout))
                return layout;
        }
        return root;
    }

    private static DashboardDevice? DeviceToAdd(Dashboard dashboard, DropSource source)
        => dashboard.FindDevice(source.Device) is null ? new DashboardDevice(source.Device, source.MapRoot) : null;

    /// <summary>
    /// Writes the full path of a node with an index for each repeat in it (see <see cref="ControlDrops"/>); a new
    /// variable is declared on the host.
    /// </summary>
    private sealed class Indexer(BindingScope scope, string device, Dictionary<VariableReference, string> repeats, LayoutControl host, List<DeclaredVariable> declared)
    {
        // the index the base around gives each repeat it goes through, by the path of the repeat
        private readonly Dictionary<string, string> _baseIndices = BaseIndices(scope, device);

        public string PathOf(ParameterNode node)
            => String.Join('.', node.PathFromRoot.Select(part => part.Kind == ParameterNodeKind.Repeat ? $"{part.Name}[{IndexOf(part)}]" : part.Name));

        private string IndexOf(ParameterNode repeat)
        {
            if (_baseIndices.TryGetValue(repeat.Path, out var index))
                return index;
            foreach (var (name, reference) in scope.Variables)
            {
                if (repeats.TryGetValue(reference, out var indexed) && String.Equals(indexed, repeat.Path, StringComparison.OrdinalIgnoreCase))
                    return $"{SchemaPaths.VariablePrefix}{name}";
            }
            foreach (var variable in declared.Where(variable => variable.Owner == host))
            {
                if (String.Equals(repeats[new VariableReference(host, variable.Variable.Name)], repeat.Path, StringComparison.OrdinalIgnoreCase))
                    return $"{SchemaPaths.VariablePrefix}{variable.Variable.Name}";
            }

            var declaredName = NewName(repeat.Name);
            declared.Add(new DeclaredVariable(host, new IndexVariable(declaredName)));
            repeats[new VariableReference(host, declaredName)] = repeat.Path;
            return $"{SchemaPaths.VariablePrefix}{declaredName}";
        }

        // the name of the repeat in camel case; a name in scope or on the host gets a number
        private string NewName(string repeat)
        {
            var name = SchemaPaths.IsVariableName(repeat) ? Char.ToLowerInvariant(repeat[0]) + repeat[1..] : "item";
            var taken = scope.Variables.Keys
                .Concat(host.Variables.Select(variable => variable.Name))
                .Concat(declared.Where(variable => variable.Owner == host).Select(variable => variable.Variable.Name))
                .ToHashSet(StringComparer.Ordinal);
            var unique = name;
            for (var number = 2; taken.Contains(unique); number++)
                unique = $"{name}{number}";
            return unique;
        }

        private static Dictionary<string, string> BaseIndices(BindingScope scope, string device)
        {
            var indices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (scope.Base is not { } basePath || !String.Equals(scope.Device, device, StringComparison.OrdinalIgnoreCase))
                return indices;
            var names = new List<string>();
            foreach (var segment in basePath.Split('.'))
            {
                var open = segment.IndexOf('[');
                names.Add(open < 0 ? segment : segment[..open]);
                if (open >= 0 && segment.EndsWith(']'))
                    indices[String.Join('.', names)] = segment[(open + 1)..^1];
            }
            return indices;
        }
    }
}

/// <summary>What is dropped: a node of the parameter tree of a device of the rig (its alias and map root).</summary>
public sealed record DropSource(string Device, string MapRoot, ParameterNode Node);

/// <summary>
/// What a drop changes: a new control (<see cref="Control"/>, with its bindings, not placed yet) or the binding of a
/// slot of an existing control (<see cref="Slot"/>, <see cref="Binding"/>); and the index variables to declare on
/// layout controls already in the dashboard and the device the dashboard must list.
/// </summary>
public sealed record DropPlan
{
    public DashboardControl? Control { get; init; }

    public string? Slot { get; init; }

    public Binding? Binding { get; init; }

    /// <summary>The variables to declare on layout controls of the dashboard; those of a new section are in it.</summary>
    public IReadOnlyList<DeclaredVariable> Variables { get; init; } = [];

    /// <summary>The device to add to the dashboard's devices; null when it lists it.</summary>
    public DashboardDevice? Device { get; init; }
}

/// <summary>An index variable to declare on a layout control.</summary>
public sealed record DeclaredVariable(LayoutControl Owner, IndexVariable Variable);
