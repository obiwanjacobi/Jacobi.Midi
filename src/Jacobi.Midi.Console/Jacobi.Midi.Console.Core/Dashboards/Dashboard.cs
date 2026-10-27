namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// A page of controls bound to device parameters: a tree of controls under one layout control. It refers to devices
/// by alias only, so it is portable between rigs.
/// </summary>
public sealed class Dashboard
{
    public required string Name { get; init; }

    /// <summary>The size of a cell in device independent pixels; the unit of all control sizes.</summary>
    public int CellSize { get; init; } = 48;

    /// <summary>The device aliases the dashboard needs, with the map root each must have.</summary>
    public IReadOnlyList<DashboardDevice> Devices { get; init; } = [];

    /// <summary>The root of the control tree.</summary>
    public required LayoutControl Layout { get; init; }

    /// <summary>The device with the alias (case-insensitive), or null.</summary>
    public DashboardDevice? FindDevice(string alias)
        => Devices.FirstOrDefault(device => String.Equals(device.Alias, alias, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A device a dashboard needs: the alias its bindings use and the map root the rig's device must have.
/// </summary>
/// <param name="Color">Overrides the rig's colour of the device on this dashboard (<c>#RRGGBB</c>).</param>
public sealed record DashboardDevice(string Alias, string MapRoot, string? Color = null);
