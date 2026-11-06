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

    /// <summary>The device values the root fixes (<c>midi.device.*</c>).</summary>
    DeviceValues Device { get; }

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
    /// Recognises a message of the device as <paramref name="message"/> for the device values: for a data message,
    /// <paramref name="data"/> is the data it carries; for any other message it is null.
    /// </summary>
    bool TryRead(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device, out ReceivedData? data);

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
