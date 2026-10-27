namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// Resolves the bindings of all parameter controls of a dashboard into parameter references, applying the
/// inheritance of <see cref="DashboardControl.Device"/> and <see cref="DashboardControl.Base"/>:
/// <list type="bullet">
/// <item>the device is the binding's own, else the nearest one of the control and the layout controls around it;</item>
/// <item>a binding with its own device has a full path; otherwise its path is appended to the nearest base, if any.</item>
/// </list>
/// Bindings that cannot be resolved are reported as problems, never skipped silently.
/// </summary>
public static class BindingResolver
{
    public static BindingResolution Resolve(Dashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        var bindings = new List<SlotBinding>();
        var problems = new List<BindingProblem>();
        Visit(dashboard.Layout, new Scope(null, null));
        return new BindingResolution(bindings, problems);

        void Visit(DashboardControl control, Scope outer)
        {
            var scope = new Scope(control.Device ?? outer.Device, control.Base ?? outer.Base);
            switch (control)
            {
                case LayoutControl layout:
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
            var device = binding.Device ?? scope.Device;
            if (device is null)
            {
                problems.Add(new(control, slot, $"'{binding.Path}' has no device: set the device on the binding, the control or a layout control around it."));
                return;
            }
            if (dashboard.FindDevice(device) is null)
            {
                problems.Add(new(control, slot, $"'{binding.Path}' refers to device '{device}', which the dashboard does not list."));
                return;
            }

            var path = binding.Device is null && scope.Base is { } basePath
                ? $"{basePath}.{binding.Path}"
                : binding.Path;
            bindings.Add(new(control, slot, new ParameterReference(device, path, binding.Instance)));
        }
    }

    private readonly record struct Scope(string? Device, string? Base);
}

/// <summary>A resolved slot: the control, the slot name and the parameter it is bound to.</summary>
public sealed record SlotBinding(ParameterControl Control, string Slot, ParameterReference Parameter);

/// <summary>A slot whose binding could not be resolved, with the reason.</summary>
public sealed record BindingProblem(ParameterControl Control, string Slot, string Message);

/// <summary>The outcome of <see cref="BindingResolver.Resolve"/>: the resolved slots and the problems.</summary>
public sealed record BindingResolution(IReadOnlyList<SlotBinding> Bindings, IReadOnlyList<BindingProblem> Problems);
