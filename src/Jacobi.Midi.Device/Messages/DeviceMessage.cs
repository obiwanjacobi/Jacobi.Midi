using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// What one message covers (<c>midi.unit</c>): any contiguous addresses, exactly one field, or exactly one atomic group.
/// </summary>
public enum AddressUnit
{
    Range,
    Field,
    Atomic,
}

/// <summary>
/// One message group of an envelope with its settings and the nodes that have a role (<c>midi.role</c>). A message
/// has no kind: an exchange definition gives it its part in a pattern. The roles say what it holds: a message with a
/// <c>data</c> role carries data of an address map, one with an <c>address</c> role (and no data) asks for data.
/// The settings may be overridden per device by the map root.
/// </summary>
public sealed record AddressMessage
{
    /// <summary>The path of the message group in the envelope plan, e.g. 'Message.DT1'.</summary>
    public required string Name { get; init; }
    public AddressUnit Unit { get; init; }
    /// <summary>The largest number of data bytes in one message; 0 means no limit.</summary>
    public int MaxDataSize { get; init; }
    /// <summary>The range over the envelope plan that writes or reads this message.</summary>
    public required PlanRange Range { get; init; }
    /// <summary>The envelope nodes by role (<c>address</c>, <c>size</c>, <c>device.modelId</c>, <c>data</c>, ...).</summary>
    public required IReadOnlyDictionary<string, NodeInfo> Roles { get; init; }

    /// <summary>The schema name of the message group, as an exchange definition names it, e.g. 'Roland.Message32.DT1'.</summary>
    public string FullName => Range.ExecutionPlan.Name is { } envelope ? $"{envelope.Namespace}.{Name}" : Name;

    /// <summary>True when the message carries data of an address map (it has the role <c>data</c>).</summary>
    public bool IsData => Roles.ContainsKey(MidiProperties.RoleData);

    /// <summary>True when the message asks for data: it has the role <c>address</c> and carries no data.</summary>
    public bool IsRequest => !IsData && Roles.ContainsKey(MidiProperties.RoleAddress);

    /// <summary>The names of the device values the message needs (the <c>device.&lt;name&gt;</c> roles).</summary>
    public IEnumerable<string> DeviceValueNames
        => Roles.Keys
            .Where(role => role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(role => role[MidiProperties.RoleDevicePrefix.Length..]);

    public override string ToString() => $"{Name} ({Unit})";
}
