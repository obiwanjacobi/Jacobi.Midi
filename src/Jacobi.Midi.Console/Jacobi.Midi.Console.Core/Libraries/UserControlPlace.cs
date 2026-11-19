using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Libraries;

/// <summary>
/// Places a user control on a dashboard: a copy of its section. When the full base it gets there (the base around the
/// drop, followed by the node the section keeps as its base, if any; e.g. <c>Compressor</c>) is a node with the user
/// control's nodeDef, the section gets the device of the layout control it is dropped in and that full base as its own.
/// Otherwise the section is placed as in the user control (without a device, with the node it keeps as a relative base):
/// its base and bindings resolve against the base around it once that is set, and while they do not, the
/// <see cref="BindingResolver"/> reports them (checked again on each change). Only a node that exists but has another
/// nodeDef is a problem of the placement: the paths are valid, but the contract is not met. Nothing is changed yet.
/// </summary>
public static class UserControlPlace
{
    /// <exception cref="ArgumentException">The layout control is not in the dashboard.</exception>
    public static UserControlPlacePlan Plan(Dashboard dashboard, LayoutControl container, UserControlDefinition control, SchemaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(library);
        var scope = BindingScope.Of(dashboard.Layout, container)
            ?? throw new ArgumentException("The layout control is not in the dashboard.", nameof(container));

        var section = (LayoutControl)DashboardFile.Copy(control.Section);
        var problems = new List<string>();
        var fullBase = scope.Base is null ? null : section.Base is { } own ? scope.FullBaseOf(own, null) : scope.Base;
        switch (NodeDefAt(dashboard, scope.Device, fullBase, library))
        {
            case (true, var nodeDef) when nodeDef == control.NodeDef:
                (section.Device, section.Base) = (scope.Device, fullBase);
                break;
            case (true, var other):
                problems.Add($"the base '{fullBase}' of '{control.Name}' is {(other is { } found ? $"a '{found}'" : "not a node with a nodeDef")}, not a '{control.NodeDef}'; set the base of the section to a node with the nodeDef '{control.NodeDef}'.");
                break;
        }
        return new UserControlPlacePlan(section, problems);
    }

    // whether the full base is a node of the device's schema, and its nodeDef; (false, null) when there is no device
    // and base, or the base is not a node (the binding resolution reports that)
    private static (bool Exists, SchemaName? NodeDef) NodeDefAt(Dashboard dashboard, string? device, string? basePath, SchemaLibrary library)
    {
        if (device is null || basePath is null
            || dashboard.FindDevice(device) is not { } dashboardDevice
            || !SchemaPaths.TryParse(basePath, _ => 0, out var parsed, out _)
            || library.FindNode(dashboardDevice.Schema, parsed.Path) is null)
            return (false, null);
        return (true, library.NodeDefOf(dashboardDevice.Schema, parsed.Path));
    }
}

/// <summary>What placing a user control gives: the section to add, and the problems with its contract.</summary>
public sealed record UserControlPlacePlan(LayoutControl Section, IReadOnlyList<string> Problems);
