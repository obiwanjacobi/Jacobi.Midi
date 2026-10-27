namespace Jacobi.Midi.Console.Core.Rigs;

/// <summary>
/// The devices of the user: one entry per device instance. Saved in its own file, so the same rig serves all
/// dashboards; dashboards refer to its devices by alias.
/// </summary>
public sealed class Rig
{
    /// <summary>Schema files beyond the schemas embedded in the device assemblies.</summary>
    public IReadOnlyList<string> Schemas { get; init; } = [];

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

    /// <summary>The device values the map root does not fix (e.g. <c>deviceId</c>), by name.</summary>
    public IReadOnlyDictionary<string, long> DeviceValues { get; init; } = new Dictionary<string, long>();

    /// <summary>How to reach the device; null when it is not connected.</summary>
    public EndpointReference? Endpoint { get; init; }

    /// <summary>The colour the device's palette is derived from (<c>#RRGGBB</c>); null for the look's neutral palette.</summary>
    public string? Color { get; init; }
}

/// <summary>
/// The endpoint of a device, matched by name and UMP group: endpoint ids are not portable and do not identify an
/// instrument behind a DIN port.
/// </summary>
public sealed record EndpointReference(string Name, int Group = 0);
