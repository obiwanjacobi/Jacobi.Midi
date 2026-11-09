using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Rigs;
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

    /// <summary>
    /// The path of the rig file the dashboard was made with, as written in the dashboard file: relative to the
    /// dashboard file, or absolute. When the open rig does not have the dashboard's devices (see
    /// <see cref="DeviceProblems"/>), the console opens this rig. Null when the dashboard does not name one.
    /// </summary>
    public string? RigPath { get; set; }

    /// <summary>The device aliases the dashboard needs, with the map root each must have.</summary>
    public IReadOnlyList<DashboardDevice> Devices { get; set; } = [];

    /// <summary>Assemblies and schema files for this dashboard: customisations on top of those of the rig and its devices.</summary>
    public SchemaSources Sources { get; init; } = SchemaSources.None;

    /// <summary>The root of the control tree.</summary>
    public required LayoutControl Layout { get; init; }

    /// <summary>
    /// What the rig lacks for this dashboard: a device alias it does not have, or a device with another map root.
    /// Empty when the rig has all the dashboard's devices.
    /// </summary>
    public IReadOnlyList<string> DeviceProblems(Rig rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var problems = new List<string>();
        foreach (var device in Devices)
        {
            if (rig.Find(device.Alias) is not { } instance)
                problems.Add($"The device '{device.Alias}' is not in the rig.");
            else if (!new SchemaName(instance.MapRoot).Equals(new SchemaName(device.MapRoot)))
                problems.Add($"The device '{device.Alias}' of the rig is a '{instance.MapRoot}', the dashboard needs a '{device.MapRoot}'.");
        }
        return problems;
    }

    /// <summary>The device with the alias (case-insensitive), or null.</summary>
    public DashboardDevice? FindDevice(string alias)
        => Devices.FirstOrDefault(device => String.Equals(device.Alias, alias, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A device a dashboard needs: the alias its bindings use and the map root the rig's device must have.
/// </summary>
public sealed record DashboardDevice(string Alias, string MapRoot)
{
    /// <summary>Overrides the rig's colors of the device on this dashboard.</summary>
    public AreaColors Colors { get; init; } = AreaColors.None;

    /// <summary>
    /// The name of the exchange the dashboard receives the device's values with (e.g. <c>requestData</c>): an exchange of
    /// the exchange file of the device's map root. Null when the dashboard does not receive values from the device.
    /// </summary>
    public string? Receive { get; init; }

    /// <summary>
    /// The name of the exchange the dashboard sends the changes of the device's values with (e.g. <c>sendData</c>): an
    /// exchange of the exchange file of the device's map root. Null when the dashboard does not send to the device.
    /// </summary>
    public string? Send { get; init; }
}
