using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Rigs;

/// <summary>
/// The devices of the user: one entry per device instance. Saved in its own file, so the same rig serves all
/// dashboards; dashboards refer to its devices by alias.
/// </summary>
public sealed class Rig
{
    /// <summary>Assemblies and schema files beyond the built-in ones, for all devices of the rig.</summary>
    public SchemaSources Sources { get; init; } = SchemaSources.None;

    public IReadOnlyList<DeviceInstance> Devices { get; init; } = [];

    /// <summary>The device instance with the alias (case-insensitive), or null.</summary>
    public DeviceInstance? Find(string alias)
        => Devices.FirstOrDefault(device => String.Equals(device.Alias, alias, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One device of the rig: what it is (the map root of its schema and the device values the map root does not fix),
/// how to reach it and its colour.
/// </summary>
public sealed class DeviceInstance
{
    /// <summary>The short name dashboards use to refer to the device (e.g. <c>synth</c>).</summary>
    public required string Alias { get; init; }

    /// <summary>The name shown to the user (e.g. the product name).</summary>
    public string? Name { get; init; }

    /// <summary>The map root of the device's schema (e.g. <c>Gx700.AddressMap</c>).</summary>
    public required string MapRoot { get; init; }

    /// <summary>The device values the map root does not fix (e.g. <c>deviceId</c>), by name (case-insensitive).</summary>
    public IReadOnlyDictionary<string, long> DeviceValues { get; init; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    /// <summary>How to reach the device; null when it is not connected.</summary>
    public EndpointReference? Endpoint { get; init; }

    /// <summary>
    /// The colours of the device's controls (accent, plate, panel); the look's where it has none. The user changes
    /// them in the console.
    /// </summary>
    public AreaColors Colors { get; set; } = AreaColors.None;

    /// <summary>Assemblies and schema files for this device only: its schema or customisations of it.</summary>
    public SchemaSources Sources { get; init; } = SchemaSources.None;
}

/// <summary>
/// The endpoint of a device, matched by name and UMP group: endpoint ids are not portable and do not identify an
/// instrument behind a DIN port.
/// </summary>
public sealed record EndpointReference(string Name, int Group = 0);
