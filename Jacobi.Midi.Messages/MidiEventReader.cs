using System.Buffers;

namespace Jacobi.Midi.Messages;

/// <summary>
/// Reads MIDI events from a buffer that is passed to each read call.
/// The reader instance only holds configuration (<see cref="ReadMetaEvents"/>) and the running status.
/// </summary>
/// <remarks>
/// Short messages are packed into the event (1-3 bytes); system exclusive and meta messages are copied into
/// the event, so an event never refers to the buffer it was read from.
/// When a message is incomplete the buffer is left at the start of that message and false is returned,
/// so the call can be repeated once more data has arrived.
/// Not yet handled: real-time messages interleaved inside another message.
/// </remarks>
public sealed class MidiEventReader
{
    private byte _runningStatus;

    /// <summary>
    /// When true, 0xFF starts a meta event (0xFF, type, variable-length size, data) as in a MIDI file,
    /// instead of being the System Reset message (live MIDI byte stream).
    /// </summary>
    public bool ReadMetaEvents { get; set; }

    /// <summary>Forgets the running status, for example when starting on a new stream.</summary>
    public void Reset() => _runningStatus = 0;

    /// <summary>Reads the next event and advances <paramref name="buffer"/> past it.</summary>
    public bool TryRead(ref ReadOnlyMemory<byte> buffer, out MidiEvent midiEvent)
    {
        var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(buffer));
        var result = TryRead(ref reader, out midiEvent);
        buffer = buffer.Slice((int)reader.Consumed);
        return result;
    }

    /// <summary>Reads the next event and advances <paramref name="buffer"/> past it.</summary>
    public bool TryRead(ref ReadOnlySequence<byte> buffer, out MidiEvent midiEvent)
    {
        var reader = new SequenceReader<byte>(buffer);
        var result = TryRead(ref reader, out midiEvent);
        buffer = buffer.Slice(reader.Position);
        return result;
    }

    /// <summary>Reads the next event and advances <paramref name="reader"/> past it.</summary>
    public bool TryRead(ref SequenceReader<byte> reader, out MidiEvent midiEvent)
    {
        midiEvent = default;

        while (reader.TryPeek(out var first))
        {
            if (first == SysEx.Start)
            {
                if (TryReadSysEx(ref reader, out midiEvent, out var skipped))
                    return true;
                if (skipped)
                    continue;
                return false;
            }

            if (first == MetaMessageFormat.Status && ReadMetaEvents)
                return TryReadMeta(ref reader, out midiEvent);

            if (!MidiWire.IsStatus(first))
            {
                // data byte: running status (channel messages only)
                if (_runningStatus == 0)
                {
                    reader.Advance(1); // stray data byte, skip
                    continue;
                }
                return TryReadShort(ref reader, _runningStatus, statusInBuffer: false, out midiEvent);
            }

            if (MidiWire.LengthOf(first) < 0)
            {
                reader.Advance(1); // undefined status or stray 0xF7, skip
                continue;
            }

            return TryReadShort(ref reader, first, statusInBuffer: true, out midiEvent);
        }

        return false;
    }

    private bool TryReadShort(ref SequenceReader<byte> reader, byte status, bool statusInBuffer, out MidiEvent midiEvent)
    {
        midiEvent = default;
        var start = reader.Consumed;
        if (statusInBuffer)
            reader.Advance(1);

        var dataLength = MidiWire.LengthOf(status) - 1;
        uint packed = status;
        for (var i = 0; i < dataLength; i++)
        {
            if (!reader.TryPeek(out var b))
            {
                reader.Rewind(reader.Consumed - start);
                return false; // incomplete
            }

            if (MidiWire.IsStatus(b))
            {
                // TODO: real-time byte inside a message; for now treat the message as corrupt and drop it.
                return TryRead(ref reader, out midiEvent);
            }

            reader.Advance(1);
            packed |= (uint)b << ((i + 1) * 8);
        }

        // running status: channel messages keep it; system common cancels it; real-time leaves it alone
        if (MidiWire.IsChannelStatus(status))
            _runningStatus = status;
        else if (!MidiWire.IsRealTime(status))
            _runningStatus = 0;

        midiEvent = new MidiEvent(new MidiMessage(packed));
        return true;
    }

    // skipped: an empty sysex (0xF0 0xF7) was consumed and reading can continue
    private bool TryReadSysEx(ref SequenceReader<byte> reader, out MidiEvent midiEvent, out bool skipped)
    {
        midiEvent = default;
        skipped = false;
        var start = reader.Consumed;
        reader.Advance(1); // 0xF0

        // scan for the terminating 0xF7, or for the next status byte (which implicitly ends the sysex)
        var terminated = false;
        var found = false;
        while (reader.TryPeek(out var b))
        {
            if (b == SysEx.End)
            {
                terminated = true;
                found = true;
                break;
            }

            if (MidiWire.IsStatus(b) && !MidiWire.IsRealTime(b))
            {
                found = true;
                break;
            }

            reader.Advance(1);
        }

        if (!found)
        {
            reader.Rewind(reader.Consumed - start);
            return false; // incomplete
        }

        var bodyLength = reader.Consumed - start - 1;
        var total = (int)(bodyLength + 1 + (terminated ? 1 : 0));
        reader.Rewind(reader.Consumed - start);
        _runningStatus = 0;

        if (bodyLength == 0)
        {
            reader.Advance(total);
            skipped = true;
            return false;
        }

        var bytes = new byte[total];
        reader.TryCopyTo(bytes);
        reader.Advance(total);
        midiEvent = MidiEvent.FromOwnedBytes(bytes);
        return true;
    }

    private bool TryReadMeta(ref SequenceReader<byte> reader, out MidiEvent midiEvent)
    {
        midiEvent = default;
        var start = reader.Consumed;
        reader.Advance(1); // 0xFF

        // type, then a variable-length size of up to 4 bytes
        var length = 0;
        var complete = false;
        if (reader.TryRead(out _))
        {
            for (var i = 0; i < 4 && reader.TryRead(out var b); i++)
            {
                length = (length << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    complete = true;
                    break;
                }
            }
        }

        if (!complete)
        {
            var headerLength = reader.Consumed - start;
            reader.Rewind(headerLength);
            if (headerLength >= 6)
            {
                reader.Advance(1); // size longer than 4 bytes: corrupt, resync
                return TryRead(ref reader, out midiEvent);
            }
            return false; // incomplete
        }

        if (reader.Remaining < length)
        {
            reader.Rewind(reader.Consumed - start);
            return false; // incomplete
        }

        var total = (int)(reader.Consumed - start) + length;
        reader.Rewind(reader.Consumed - start);
        var bytes = new byte[total];
        reader.TryCopyTo(bytes);
        reader.Advance(total);
        _runningStatus = 0;

        midiEvent = MidiEvent.FromOwnedBytes(bytes);
        return true;
    }
}
