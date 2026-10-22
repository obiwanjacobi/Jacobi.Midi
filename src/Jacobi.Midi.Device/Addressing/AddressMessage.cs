using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// What a message is for (<c>midi.message</c>): it asks for data, carries data, or is the device's reply to a message
/// it received. The exchange protocols reason in these kinds, not in message bytes.
/// </summary>
public enum AddressMessageKind
{
    Request,
    Data,
    /// <summary>The device accepted the message (e.g. Korg DATA LOAD COMPLETED, Roland ACK).</summary>
    Ack,
    /// <summary>The device could not process the message (e.g. Korg DATA LOAD ERROR, Roland RJC).</summary>
    Nak,
    /// <summary>The device received a message it could not read (e.g. Korg FORMAT ERROR, Roland ERR).</summary>
    Error,
}

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
/// One tagged message group of an envelope (<c>midi.message</c>) with its settings and the nodes that have a role
/// (<c>midi.role</c>). The settings may be overridden per device by the map root.
/// </summary>
public sealed record AddressMessage
{
    /// <summary>The path of the message group in the envelope plan, e.g. 'Message.DT1'.</summary>
    public required string Name { get; init; }
    public required AddressMessageKind Kind { get; init; }
    public AddressUnit Unit { get; init; }
    /// <summary>The largest number of data bytes in one message; 0 means no limit.</summary>
    public int MaxDataSize { get; init; }
    /// <summary>The time to wait after sending this message.</summary>
    public TimeSpan Interval { get; init; }
    /// <summary>The range over the envelope plan that writes or reads this message.</summary>
    public required PlanRange Range { get; init; }
    /// <summary>The envelope nodes by role (<c>addressStart</c>, <c>addressSize</c>, <c>device.modelId</c>, <c>data</c>, ...).</summary>
    public required IReadOnlyDictionary<string, NodeInfo> Roles { get; init; }

    /// <summary>The names of the device values the message needs (the <c>device.&lt;name&gt;</c> roles).</summary>
    public IEnumerable<string> DeviceValueNames
        => Roles.Keys
            .Where(role => role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(role => role[MidiProperties.RoleDevicePrefix.Length..]);

    public override string ToString() => $"{Name} ({Kind}, {Unit})";
}
