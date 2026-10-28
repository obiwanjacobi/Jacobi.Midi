namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// What the bindings inside a control are resolved with: the nearest <see cref="DashboardControl.Device"/> and
/// <see cref="DashboardControl.Base"/> of the control and the layout controls around it, and the index variables
/// declared on them (a nearer declaration hides one with the same name further out). The one place of these rules:
/// the <see cref="BindingResolver"/> resolves with it, the edits of the console (a drop, a move) bind with it.
/// </summary>
/// <param name="Device">The device alias of the bindings without their own; null when none is set.</param>
/// <param name="Base">The full path (with repeat indices) the bindings without their own device are relative to.</param>
/// <param name="Variables">The index variables by name.</param>
public sealed record BindingScope(string? Device, string? Base, IReadOnlyDictionary<string, VariableReference> Variables)
{
    /// <summary>The scope around the root of a dashboard: no device, no base, no variables.</summary>
    public static BindingScope None { get; } = new(null, null, new Dictionary<string, VariableReference>(StringComparer.Ordinal));

    /// <summary>The scope inside a control in this scope: its own device, base and variables over these.</summary>
    public BindingScope Inside(DashboardControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var scope = new BindingScope(control.Device ?? Device, control.Base ?? Base, Variables);
        if (control is LayoutControl { Variables.Count: > 0 } layout)
        {
            var variables = new Dictionary<string, VariableReference>(Variables, StringComparer.Ordinal);
            foreach (var variable in layout.Variables)
                variables[variable.Name] = new VariableReference(layout, variable.Name);
            scope = scope with { Variables = variables };
        }
        return scope;
    }

    /// <summary>
    /// The scope inside a control of the dashboard whose root is <paramref name="root"/> (with the control's own
    /// device, base and variables); null when the control is not in it.
    /// </summary>
    public static BindingScope? Of(LayoutControl root, DashboardControl control)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(control);
        return Find(root, None);

        BindingScope? Find(DashboardControl current, BindingScope outer)
        {
            var scope = outer.Inside(current);
            if (current == control)
                return scope;
            return current is LayoutControl layout
                ? layout.Children.Select(child => Find(child, scope)).FirstOrDefault(found => found is not null)
                : null;
        }
    }

    /// <summary>
    /// The binding text of a full path (with repeat indices) of a device in this scope: relative to the base when the
    /// device is the scope's and the path is below the base, the path alone when the device is the scope's and there is
    /// no base, otherwise the path with its own device.
    /// </summary>
    public Binding BindingOf(string device, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(device);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!String.Equals(device, Device, StringComparison.OrdinalIgnoreCase))
            return new Binding(path) { Device = device };
        if (Base is null)
            return new Binding(path);
        var prefix = Base + ".";
        return path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length
            ? new Binding(path[prefix.Length..])
            : new Binding(path) { Device = device };
    }

    /// <summary>
    /// The device and full path (with repeat indices) a binding to a device parameter has in this scope; null for a
    /// binding to an index variable or without device.
    /// </summary>
    public (string Device, string Path)? FullPathOf(Binding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Variable is not null)
            return null;
        if (binding.Device is { } own)
            return (own, binding.Path);
        if (Device is null)
            return null;
        return (Device, Base is null ? binding.Path : $"{Base}.{binding.Path}");
    }
}
