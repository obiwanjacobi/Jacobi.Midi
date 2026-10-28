using Jacobi.Midi.Console.Core.Dashboards;

namespace Jacobi.Midi.Console.Core.Editing;

/// <summary>
/// Plans what moving a control into another layout control changes so that its bindings, and those of the controls
/// inside it, stay bound to the same parameters: the device, base and variables around it change
/// (<see cref="BindingScope"/>). A moved layout control keeps the device and base it inherited (it gets them as its
/// own); a binding that would point elsewhere is written again for its new place (<see cref="BindingScope.BindingOf"/>).
/// An index variable that is not declared around the new place (or is another variable there) cannot be kept: it is
/// reported. Nothing is changed yet.
/// </summary>
public static class ControlMoves
{
    /// <exception cref="ArgumentException">The control is the root, is not in the dashboard, or the target is (in) the control.</exception>
    public static MovePlan Move(LayoutControl root, DashboardControl control, LayoutControl target)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(target);
        if (GridLayout.ParentOf(root, control) is not { } parent)
            throw new ArgumentException("The control is the root or not in the dashboard.", nameof(control));
        if (Contains(control, target))
            throw new ArgumentException("A control cannot be moved into itself.", nameof(target));
        var oldOuter = BindingScope.Of(root, parent)!;
        var newOuter = BindingScope.Of(root, target)
            ?? throw new ArgumentException("The target is not in the dashboard.", nameof(target));

        string? device = null, basePath = null;
        if (control is LayoutControl)
        {
            if (control.Device is null && oldOuter.Device is not null && !String.Equals(oldOuter.Device, newOuter.Device, StringComparison.OrdinalIgnoreCase))
                device = oldOuter.Device;
            if (control.Base is null && oldOuter.Base is not null && oldOuter.Base != newOuter.Base)
                basePath = oldOuter.Base;
        }

        var slots = new List<SlotsChange>();
        var problems = new List<string>();
        var newInside = newOuter.Inside(control);
        newInside = newInside with { Device = device ?? newInside.Device, Base = basePath ?? newInside.Base };
        Visit(control, oldOuter.Inside(control), newInside);
        return new MovePlan { Device = device, Base = basePath, Slots = slots, Problems = problems };

        void Visit(DashboardControl current, BindingScope before, BindingScope after)
        {
            if (current.Base is { } own && SchemaPaths.TryParse(own, _ => 0, out var parsedBase, out _))
                CheckVariables(current, parsedBase.Variables.Select(use => use.Variable), before, after);

            switch (current)
            {
                case ParameterControl parameter:
                    var changed = false;
                    var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
                    foreach (var (slot, binding) in parameter.Slots)
                    {
                        bindings[slot] = Rebind(parameter, binding, before, after);
                        changed |= bindings[slot] != binding;
                    }
                    if (changed)
                        slots.Add(new SlotsChange(parameter, bindings));
                    break;

                case LayoutControl layout:
                    foreach (var child in layout.Children)
                        Visit(child, before.Inside(child), after.Inside(child));
                    break;
            }
        }

        Binding Rebind(ParameterControl parameter, Binding binding, BindingScope before, BindingScope after)
        {
            if (binding.Variable is { } variable)
            {
                CheckVariables(parameter, [variable], before, after);
                return binding;
            }
            if (before.FullPathOf(binding) is not { } full)
                return binding;   // not bound before either: reported by the resolver
            if (SchemaPaths.TryParse(full.Path, _ => 0, out var parsed, out _))
                CheckVariables(parameter, parsed.Variables.Select(use => use.Variable), before, after);
            return after.FullPathOf(binding) == full ? binding : after.BindingOf(full.Device, full.Path);
        }

        void CheckVariables(DashboardControl current, IEnumerable<string> names, BindingScope before, BindingScope after)
        {
            foreach (var name in names.Distinct())
            {
                if (before.Variables.TryGetValue(name, out var was) && after.Variables.GetValueOrDefault(name) != was)
                {
                    var problem = $"'{ControlCatalog.NameOf(current)}' uses '{SchemaPaths.VariablePrefix}{name}' of '{ControlCatalog.NameOf(was.Owner)}', which is not around its new place.";
                    if (!problems.Contains(problem))
                        problems.Add(problem);
                }
            }
        }
    }

    /// <summary>True when the control is <paramref name="other"/> or contains it.</summary>
    public static bool Contains(DashboardControl control, DashboardControl other)
        => control == other || (control is LayoutControl layout && layout.Children.Any(child => Contains(child, other)));
}

/// <summary>
/// What a move changes besides the place of the control: the device and base the moved layout control gets as its own
/// (null: unchanged), the slots of the parameter controls that are bound again, and the variables that are lost.
/// </summary>
public sealed record MovePlan
{
    public string? Device { get; init; }

    public string? Base { get; init; }

    public IReadOnlyList<SlotsChange> Slots { get; init; } = [];

    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>The new slots of a parameter control.</summary>
public sealed record SlotsChange(ParameterControl Control, IReadOnlyDictionary<string, Binding> Slots);
