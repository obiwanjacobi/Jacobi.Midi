using Jacobi.Midi.Console.Core.Dashboards;

namespace Jacobi.Midi.Console.Core.Editing;

/// <summary>
/// Plans what moving a control into another layout control changes so that its bindings, and those of the controls
/// inside it, stay bound to the same parameters: the device, base and variables around it change
/// (<see cref="BindingScope"/>). A moved layout control keeps the device and base it inherited, and a control its own
/// base (relative to the base around it): they are written for the new place (<see cref="BindingScope.BaseOf"/>); a
/// binding that would point elsewhere is written again for its new place (<see cref="BindingScope.BindingOf"/>).
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

        // a layout control keeps the device it inherited; a control with a base of its own keeps its (full) base, a
        // layout control also the base it inherited: written for the new place (BindingScope.BaseOf)
        string? device = null, basePath = null;
        var before = oldOuter.Inside(control);
        var newInside = newOuter.Inside(control);
        var needsDevice = control is LayoutControl && control.Device is null && before.Device is not null
            && !String.Equals(before.Device, newOuter.Device, StringComparison.OrdinalIgnoreCase);
        var keepsBase = before.Base is not null && (control.Base is not null || control is LayoutControl);
        if (keepsBase && control.Device is not null)
        {
            if (newInside.Base != before.Base)
                basePath = before.Base;
        }
        else if (keepsBase && before.Device is { } kept && (needsDevice || newInside.Base != before.Base))
        {
            (device, basePath) = newOuter.BaseOf(kept, before.Base!);
        }
        else if (needsDevice)
        {
            device = before.Device;
        }

        var slots = new List<SlotsChange>();
        var problems = new List<string>();
        newInside = newInside with { Device = device ?? newInside.Device, Base = basePath is null ? newInside.Base : before.Base };
        Visit(control, before, newInside);
        return new MovePlan { Device = device, Base = basePath, Slots = slots, Problems = problems };

        void Visit(DashboardControl current, BindingScope before, BindingScope after)
        {
            // the variables in the (full) base of a control with a base of its own, or that gets one
            var hasBase = current.Base is not null || (current == control && basePath is not null);
            if (hasBase && SchemaPaths.TryParse(before.Base, _ => 0, out var parsedBase, out _))
                CheckVariables(current, parsedBase.Variables.Select(use => use.Variable), before, after);

            if (current is ParameterControl parameter)
            {
                var changed = false;
                var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
                foreach (var (slot, binding) in parameter.Slots)
                {
                    bindings[slot] = Rebind(parameter, binding, before, after);
                    changed |= bindings[slot] != binding;
                }
                if (changed)
                    slots.Add(new SlotsChange(parameter, bindings));
            }
            foreach (var child in current.Children)
                Visit(child, before.Inside(child), after.Inside(child));
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
        => control == other || control.Children.Any(child => Contains(child, other));
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
