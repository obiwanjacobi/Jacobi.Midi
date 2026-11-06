using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// A data message that was recognised for a root: the device values in it and the block of the root it carries (a
/// range of the root's plan). Call <see cref="Decode"/> to read its values into a value model; the map that recognised
/// it decides how.
/// </summary>
public sealed class ReceivedData
{
    private readonly Func<ReceivedData, IValueSink, long> _decode;

    internal ReceivedData(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device, PlanRange block, Func<ReceivedData, IValueSink, long> decode)
    {
        Message = message;
        SysEx = sysEx;
        Device = device;
        Block = block;
        _decode = decode;
    }

    public DeviceMessage Message { get; }
    public ReadOnlyMemory<byte> SysEx { get; }
    /// <summary>The device values read from the message.</summary>
    public DeviceValues Device { get; }
    /// <summary>The nodes of the root the message carries.</summary>
    public PlanRange Block { get; }

    /// <summary>Reads the values of the message into the value model (paths and instances of the root's plan).</summary>
    public long Decode(IValueSink sink) => _decode(this, sink);
}
