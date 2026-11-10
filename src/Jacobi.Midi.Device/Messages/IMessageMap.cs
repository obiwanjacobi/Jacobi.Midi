using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// How the data of one schema root is transferred in messages: which messages carry it, how a range of the root is
/// split into blocks (the nodes one message asks for or carries), how a message for a block is written and how a
/// message of the device is recognised. Created per root by an <see cref="IMessageMapProvider"/>; the address map is
/// one kind of map. Stateless: a call never depends on earlier calls (caches for speed aside). Thread-safe.
/// </summary>
internal interface IMessageMap
{
    /// <summary>The root the map is for (the <c>for</c> of an exchange file).</summary>
    SchemaName Root { get; }

    /// <summary>How the data of the root is carried in messages.</summary>
    MessageMapKind Kind { get; }

    /// <summary>The device values the root fixes (<c>midi.device.*</c>).</summary>
    DeviceValues Device { get; }

    /// <summary>
    /// The number of items of a repeat of the root as the map transfers it (e.g. the items of the address space of an
    /// address map, the constant count of a dump); null for a node that is not a repeat, or a repeat whose count the
    /// data gives.
    /// </summary>
    int? ItemCount(SchemaPath repeat);

    /// <summary>
    /// How the value of a node with a role of the map's own (<c>midi.role</c>, e.g. <c>address</c>, <c>size</c>,
    /// <c>data</c>) is supplied when a data message carries the root: a short description; null when the map does not
    /// supply it. The device values the root fixes are described by <see cref="MessageMapInfo.DescribeRole"/>.
    /// </summary>
    string? DescribeRole(string role);

    /// <summary>The messages that can carry the data of the root.</summary>
    IReadOnlyList<DeviceMessage> DataMessages { get; }

    /// <summary>A message by its schema name (as an exchange file names it), checked for use with this map.</summary>
    /// <exception cref="InvalidOperationException">There is no such message, or it cannot be used with this map.</exception>
    DeviceMessage Message(SchemaName name);

    /// <summary>True when the message carries data of the root.</summary>
    bool IsData(DeviceMessage message);

    /// <summary>The problems that prevent splitting the range into blocks for <paramref name="splitBy"/>; empty when it can.</summary>
    IReadOnlyList<string> Validate(PlanRange range, DeviceMessage splitBy);

    /// <summary>The blocks to transfer the range with the message (the range may be widened, e.g. to the groups that are transferred whole).</summary>
    /// <exception cref="InvalidOperationException">The range cannot be split for the message (see <see cref="Validate"/>).</exception>
    IReadOnlyList<PlanRange> Split(PlanRange range, DeviceMessage message);

    /// <summary>The message for a block as a complete SysEx message; <paramref name="source"/> gives the values of a data message.</summary>
    byte[] Write(DeviceMessage message, PlanRange block, IValueSource? source, DeviceValues device);

    /// <summary>
    /// A data message for a block as a complete SysEx message, written from the client's model of the whole message
    /// (envelope and body, scope by scope; e.g. the instance of an editor): the map supplies the roles of the block
    /// and <paramref name="device"/> the device values it holds; the client the other values and the body.
    /// </summary>
    byte[] WriteMessage(DeviceMessage message, PlanRange block, IValueSource client, DeviceValues device);

    /// <summary>
    /// Wraps the client's sink for the body of a data message that carries <paramref name="block"/>, so the read ends
    /// with the block (e.g. the repeats of an address map, which have no count); the sink itself when nothing is needed.
    /// </summary>
    IValueSink WrapBody(IValueSink sink, PlanRange block);

    /// <summary>A short description of a block for a person, e.g. its address and size, or the whole root.</summary>
    string DescribeBlock(PlanRange block);

    /// <summary>The blocks in the order the map transfers them (e.g. by address); by default as given.</summary>
    IReadOnlyList<PlanRange> Order(IReadOnlyList<PlanRange> blocks) => blocks;

    /// <summary>
    /// Matches a message of the device with <paramref name="message"/> for the device values: recognised (for a data
    /// message with the data it carries), not this message, or a near miss with the problem (see <see cref="MessageMatch"/>).
    /// </summary>
    MessageMatch Match(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device);

    /// <summary>
    /// Recognises a message of the device as <paramref name="message"/> for the device values: for a data message,
    /// <paramref name="data"/> is the data it carries; for any other message it is null. A near miss is not recognised
    /// (see <see cref="Match"/> for its problem).
    /// </summary>
    bool TryRead(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device, out ReceivedData? data)
    {
        var match = Match(message, sysEx, device);
        data = match.Data;
        return match.IsRecognised;
    }

    /// <summary>True when the received data covers the block.</summary>
    bool IsComplete(PlanRange block, IReadOnlyList<ReceivedData> received);

    /// <summary>A new, empty state of the device for this root (see <see cref="IDeviceState"/>).</summary>
    IDeviceState CreateState();
}

/// <summary>
/// Creates the <see cref="IMessageMap"/> of a root when the root is of its kind: it claims a root when the schema
/// holds tags it owns, and then reports the problems of those tags itself (it never declines because of them).
/// Stateless.
/// </summary>
internal interface IMessageMapProvider
{
    /// <summary>The map of the plan's root; null when the root is not of this provider's kind.</summary>
    /// <exception cref="Exception">The provider's own exception when the root is of its kind but not valid.</exception>
    IMessageMap? TryCreate(Serializer serializer, ExecutionPlan plan);
}
