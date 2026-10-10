using System.Buffers;

namespace Jacobi.Midi.Messages;

/// <summary>
/// Writes MIDI events to a buffer that is passed to each write call.
/// The writer instance only holds configuration (<see cref="UseRunningStatus"/>) and the running status.
/// </summary>
/// <remarks>
/// The writer does not flush; the caller owns the buffer.
/// </remarks>
public sealed class MidiEventWriter
{
    private byte _runningStatus;

    /// <summary>
    /// When true, the status byte of consecutive channel messages with the same status is omitted.
    /// </summary>
    public bool UseRunningStatus { get; set; }

    /// <summary>Forgets the running status, for example when starting on a new stream.</summary>
    public void Reset() => _runningStatus = 0;

    /// <summary>The number of bytes <see cref="TryWrite"/> would write for the event right now.</summary>
    public int GetSize(MidiEvent midiEvent)
    {
        switch (midiEvent.Kind)
        {
            case MidiEventKind.Message:
                midiEvent.TryGetValue(out MidiMessage? message);
                var status = (byte)MidiWire.Pack(message!.Value);
                var length = MidiWire.LengthOf(status);
                if (length < 0)
                    throw new ArgumentException($"Status 0x{status:X2} is not a supported message.", nameof(midiEvent));
                return SkipsStatus(status) ? length - 1 : length;

            case MidiEventKind.SystemExclusive:
                midiEvent.TryGetValue(out SystemExclusiveMessage? sysEx);
                var sysExBytes = sysEx!.Value.Bytes.Span;
                return sysExBytes.Length + (sysExBytes[^1] != SysEx.End ? 1 : 0);

            case MidiEventKind.Meta:
                midiEvent.TryGetValue(out MidiMetaMessage? meta);
                return meta!.Value.Bytes.Length;

            default:
                throw new ArgumentException("The event has no value.", nameof(midiEvent));
        }
    }

    /// <summary>
    /// Writes the event to <paramref name="destination"/>. Returns false (and writes nothing) when the
    /// destination is too small.
    /// </summary>
    public bool TryWrite(MidiEvent midiEvent, Span<byte> destination, out int bytesWritten)
    {
        var size = GetSize(midiEvent);
        if (destination.Length < size)
        {
            bytesWritten = 0;
            return false;
        }

        switch (midiEvent.Kind)
        {
            case MidiEventKind.Message:
                midiEvent.TryGetValue(out MidiMessage? message);
                WriteShort(message!.Value, destination);
                break;

            case MidiEventKind.SystemExclusive:
                midiEvent.TryGetValue(out SystemExclusiveMessage? sysEx);
                WriteSysEx(sysEx!.Value, destination);
                break;

            case MidiEventKind.Meta:
                midiEvent.TryGetValue(out MidiMetaMessage? meta);
                WriteMeta(meta!.Value, destination);
                break;
        }

        bytesWritten = size;
        return true;
    }

    /// <summary>Writes the event to the buffer writer (for example a PipeWriter or ArrayBufferWriter).</summary>
    public void Write(MidiEvent midiEvent, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var size = GetSize(midiEvent);
        TryWrite(midiEvent, output.GetSpan(size), out _);
        output.Advance(size);
    }

    private bool SkipsStatus(byte status)
        => UseRunningStatus && MidiWire.IsChannelStatus(status) && status == _runningStatus;

    private void WriteShort(MidiMessage message, Span<byte> destination)
    {
        var packed = MidiWire.Pack(message);
        var status = (byte)packed;
        var length = MidiWire.LengthOf(status);

        var index = 0;
        for (var i = SkipsStatus(status) ? 1 : 0; i < length; i++)
            destination[index++] = (byte)(packed >> (i * 8));

        if (MidiWire.IsChannelStatus(status))
            _runningStatus = status;
        else if (!MidiWire.IsRealTime(status))
            _runningStatus = 0;
    }

    // adds the 0xF7 terminator when it is missing
    private void WriteSysEx(SystemExclusiveMessage message, Span<byte> destination)
    {
        var bytes = message.Bytes.Span;
        bytes.CopyTo(destination);
        if (bytes[^1] != SysEx.End)
            destination[bytes.Length] = SysEx.End;

        _runningStatus = 0;
    }

    // meta events are only valid in a MIDI file; the bytes are written as they are
    private void WriteMeta(MidiMetaMessage message, Span<byte> destination)
    {
        message.Bytes.Span.CopyTo(destination);

        _runningStatus = 0;
    }
}
