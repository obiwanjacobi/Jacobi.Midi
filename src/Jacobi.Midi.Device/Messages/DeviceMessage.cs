using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// One message group of the schemas with the nodes that have a role (<c>midi.role</c>): the facts the schema gives
/// about a message. A message has no kind: an exchange definition gives it its part in a pattern, and the map of the
/// transferred root says what it means for that root (e.g. the unit and maximum data size of an address map).
/// </summary>
public sealed record DeviceMessage
{
    /// <summary>The path of the message group in its plan, e.g. 'Message.DT1'.</summary>
    public required string Name { get; init; }
    /// <summary>The range over the plan of the message group that writes or reads this message.</summary>
    public required PlanRange Range { get; init; }
    /// <summary>The nodes by role (<c>address</c>, <c>size</c>, <c>device.modelId</c>, <c>data</c>, ...).</summary>
    public required IReadOnlyDictionary<string, NodeInfo> Roles { get; init; }

    /// <summary>The schema name of the message group, as an exchange definition names it, e.g. 'Roland.Message32.DT1'.</summary>
    public string FullName => Range.ExecutionPlan.Name is { } envelope ? $"{envelope.Namespace}.{Name}" : Name;

    /// <summary>True when the message carries data (it has the role <c>data</c>).</summary>
    public bool IsData => Roles.ContainsKey(MidiProperties.RoleData);

    /// <summary>The names of the device values the message needs (the <c>device.&lt;name&gt;</c> roles).</summary>
    public IEnumerable<string> DeviceValueNames
        => Roles.Keys
            .Where(role => role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(role => role[MidiProperties.RoleDevicePrefix.Length..]);

    public override string ToString() => Name;
}
