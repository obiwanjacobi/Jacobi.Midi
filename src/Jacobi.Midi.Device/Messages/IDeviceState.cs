using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The state of a device for one root, as a device session keeps it: the values received from the device, sent to it
/// and sent by the device on its own. As a sink it takes received data, as a source it gives the data to send. It
/// decides what it holds, what a send needs and what is forgotten; how depends on the map that created it
/// (<see cref="IMessageMap.CreateState"/>). Thread-safe.
/// </summary>
internal interface IDeviceState : IValueSink, IValueSource
{
    /// <summary>The range of the whole root.</summary>
    PlanRange Whole { get; }

    /// <summary>True when the state holds every value of the block that the device can send (a receive can be answered from it).</summary>
    bool Contains(PlanRange block);

    /// <summary>Gives the values of the range to the sink.</summary>
    void CopyTo(PlanRange range, IValueSink sink);

    /// <summary>The client's values of the range, exactly as they would be sent.</summary>
    FieldValueStore ClientValues(PlanRange range, IValueSource source);

    /// <summary>
    /// True when a send of the range has every value it needs from the client's values or (unless
    /// <paramref name="fresh"/>) from the state; otherwise the missing values are requested from the device first.
    /// </summary>
    bool Completes(PlanRange range, FieldValueStore client, bool fresh);

    /// <summary>Takes the client's values of the range into the state, for a send (values the device does not accept are left out).</summary>
    void Merge(PlanRange range, FieldValueStore client);

    /// <summary>Forgets the values of the range.</summary>
    void Clear(PlanRange range);

    /// <summary>Forgets all values.</summary>
    void Clear();
}
