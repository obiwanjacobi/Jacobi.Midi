using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// A page of controls bound to device parameters: a tree of controls under one layout control. It refers to devices
/// by alias only, so it is portable between rigs.
/// </summary>
public sealed class Dashboard
{
    /// <summary>The cell size at which the controls are shown at their own size (not scaled).</summary>
    public const int DefaultCellSize = 24;

    /// <summary>The smallest cell size the console offers (a quarter of the default).</summary>
    public const int MinimumCellSize = 6;

    /// <summary>The largest cell size the console offers (four times the default).</summary>
    public const int MaximumCellSize = 96;

    public required string Name { get; set; }

    /// <summary>
    /// The size of a cell in device independent pixels: the unit of all sizes, gaps and grid positions. The whole
    /// dashboard scales with it.
    /// </summary>
    public int CellSize { get; set; } = DefaultCellSize;

    /// <summary>
    /// The name of the look the dashboard is shown with when it is selected; null when it has none. The console knows
    /// the looks; the user can show the dashboard with another one without changing it.
    /// </summary>
    public string? Look { get; set; }

    /// <summary>The device aliases the dashboard needs, with the map root each must have.</summary>
    public IReadOnlyList<DashboardDevice> Devices { get; set; } = [];

    /// <summary>Assemblies and schema files for this dashboard: customisations on top of those of the rig and its devices.</summary>
    public SchemaSources Sources { get; init; } = SchemaSources.None;

    /// <summary>The root of the control tree.</summary>
    public required LayoutControl Layout { get; init; }

    /// <summary>The device with the alias (case-insensitive), or null.</summary>
    public DashboardDevice? FindDevice(string alias)
        => Devices.FirstOrDefault(device => String.Equals(device.Alias, alias, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A device a dashboard needs: the alias its bindings use and the map root the rig's device must have.
/// </summary>
public sealed record DashboardDevice(string Alias, string MapRoot)
{
    /// <summary>Overrides the rig's colours of the device on this dashboard.</summary>
    public AreaColors Colors { get; init; } = AreaColors.None;
}
