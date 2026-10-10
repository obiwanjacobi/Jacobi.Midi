using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Jacobi.Midi.Messages;

/// <summary>
/// Parses the meta event layout used in Standard MIDI Files: 0xFF, type, variable-length size, data.
/// </summary>
internal static class MetaMessageFormat
{
    public const byte Status = 0xFF;

    public static bool TryParse(ReadOnlyMemory<byte> data, out byte type, out ReadOnlyMemory<byte> payload)
    {
        type = 0;
        payload = default;

        var span = data.Span;
        if (span.Length < 3 || span[0] != Status)
            return false;

        type = span[1];
        var length = 0;
        var index = 2;
        for (var i = 0; i < 4 && index < span.Length; i++)
        {
            var b = span[index++];
            length = (length << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                if (length > span.Length - index)
                    return false;

                payload = data.Slice(index, length);
                return true;
            }
        }
        return false;
    }

    // payloadLength < 0: any length. Returns the payload (a slice of data, no copy).
    public static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> data, byte type, int payloadLength, string messageName)
    {
        if (!TryParse(data, out var actual, out var payload))
            throw new ArgumentException($"{messageName} requires a well-formed meta event (0xFF, type, length, data).", nameof(data));

        if (actual != type)
            throw new ArgumentException($"{messageName} requires meta type 0x{type:X2}, but got 0x{actual:X2}.", nameof(data));

        if (payloadLength >= 0 && payload.Length != payloadLength)
            throw new ArgumentException($"{messageName} requires {payloadLength} data bytes, but got {payload.Length}.", nameof(data));

        return payload;
    }
}

/// <summary>
/// A union over the Standard MIDI File meta events, backed by the raw event bytes (no copy).
/// </summary>
[Union]
public readonly record struct MidiMetaMessage : IUnion
{
    private readonly ReadOnlyMemory<byte> _data;

    public MidiMetaMessage(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    public static MidiMetaMessage Create(byte[] bytes)
    {
        return new MidiMetaMessage(bytes);
    }

    public static MidiMetaMessage Create(byte[] buffer, int offset, int length)
    {
        return new MidiMetaMessage(buffer.AsMemory(offset, length));
    }

    // IUnion implementation
    public object? Value => HasValue ? _data : null;

    public bool HasValue => MetaMessageFormat.TryParse(_data, out _, out _);

    /// <summary>The raw event bytes: 0xFF, type, length and data.</summary>
    public ReadOnlyMemory<byte> Bytes => _data;

    private bool Is(byte type, int payloadLength)
        => MetaMessageFormat.TryParse(_data, out var actual, out var payload)
            && actual == type
            && (payloadLength < 0 || payload.Length == payloadLength);

    // non-boxing union accessors (typed views over the same memory, no copy)
    public bool TryGetValue([NotNullWhen(true)] out SequenceNumberMessage? value)
    {
        if (Is(SequenceNumberMessage.Type, SequenceNumberMessage.Length)) { value = new SequenceNumberMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TextMessage? value)
    {
        if (Is(TextMessage.Type, -1)) { value = new TextMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out CopyrightMessage? value)
    {
        if (Is(CopyrightMessage.Type, -1)) { value = new CopyrightMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TrackNameMessage? value)
    {
        if (Is(TrackNameMessage.Type, -1)) { value = new TrackNameMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out InstrumentNameMessage? value)
    {
        if (Is(InstrumentNameMessage.Type, -1)) { value = new InstrumentNameMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out LyricMessage? value)
    {
        if (Is(LyricMessage.Type, -1)) { value = new LyricMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MarkerMessage? value)
    {
        if (Is(MarkerMessage.Type, -1)) { value = new MarkerMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out CuePointMessage? value)
    {
        if (Is(CuePointMessage.Type, -1)) { value = new CuePointMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ProgramNameMessage? value)
    {
        if (Is(ProgramNameMessage.Type, -1)) { value = new ProgramNameMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out DeviceNameMessage? value)
    {
        if (Is(DeviceNameMessage.Type, -1)) { value = new DeviceNameMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MidiChannelPrefixMessage? value)
    {
        if (Is(MidiChannelPrefixMessage.Type, MidiChannelPrefixMessage.Length)) { value = new MidiChannelPrefixMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MidiPortMessage? value)
    {
        if (Is(MidiPortMessage.Type, MidiPortMessage.Length)) { value = new MidiPortMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out EndOfTrackMessage? value)
    {
        if (Is(EndOfTrackMessage.Type, EndOfTrackMessage.Length)) { value = new EndOfTrackMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SetTempoMessage? value)
    {
        if (Is(SetTempoMessage.Type, SetTempoMessage.Length)) { value = new SetTempoMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SmpteOffsetMessage? value)
    {
        if (Is(SmpteOffsetMessage.Type, SmpteOffsetMessage.Length)) { value = new SmpteOffsetMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TimeSignatureMessage? value)
    {
        if (Is(TimeSignatureMessage.Type, TimeSignatureMessage.Length)) { value = new TimeSignatureMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out KeySignatureMessage? value)
    {
        if (Is(KeySignatureMessage.Type, KeySignatureMessage.Length)) { value = new KeySignatureMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SequencerSpecificMessage? value)
    {
        if (Is(SequencerSpecificMessage.Type, -1)) { value = new SequencerSpecificMessage(_data); return true; }
        value = null;
        return false;
    }
}

/// <summary>Sequence Number (FF 00): the number of a sequence or pattern.</summary>
public readonly record struct SequenceNumberMessage
{
    public const byte Type = 0x00;
    internal const int Length = 2;
    private readonly ReadOnlyMemory<byte> _payload;

    public SequenceNumberMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, Length, nameof(SequenceNumberMessage));

    public ushort Number => (ushort)((_payload.Span[0] << 8) | _payload.Span[1]);
}

/// <summary>Text Event (FF 01): arbitrary text.</summary>
public readonly record struct TextMessage
{
    public const byte Type = 0x01;
    private readonly ReadOnlyMemory<byte> _payload;

    public TextMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(TextMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Copyright Notice (FF 02): a copyright text.</summary>
public readonly record struct CopyrightMessage
{
    public const byte Type = 0x02;
    private readonly ReadOnlyMemory<byte> _payload;

    public CopyrightMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(CopyrightMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Sequence/Track Name (FF 03): the name of the sequence or track.</summary>
public readonly record struct TrackNameMessage
{
    public const byte Type = 0x03;
    private readonly ReadOnlyMemory<byte> _payload;

    public TrackNameMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(TrackNameMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Instrument Name (FF 04): the name of the instrument used on the track.</summary>
public readonly record struct InstrumentNameMessage
{
    public const byte Type = 0x04;
    private readonly ReadOnlyMemory<byte> _payload;

    public InstrumentNameMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(InstrumentNameMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Lyric (FF 05): a lyric syllable, usually on a note.</summary>
public readonly record struct LyricMessage
{
    public const byte Type = 0x05;
    private readonly ReadOnlyMemory<byte> _payload;

    public LyricMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(LyricMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Marker (FF 06): a named point in the sequence, such as a rehearsal mark.</summary>
public readonly record struct MarkerMessage
{
    public const byte Type = 0x06;
    private readonly ReadOnlyMemory<byte> _payload;

    public MarkerMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(MarkerMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Cue Point (FF 07): a description of something happening at this point, such as on film or stage.</summary>
public readonly record struct CuePointMessage
{
    public const byte Type = 0x07;
    private readonly ReadOnlyMemory<byte> _payload;

    public CuePointMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(CuePointMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Program Name (FF 08): the name of the program (patch) used.</summary>
public readonly record struct ProgramNameMessage
{
    public const byte Type = 0x08;
    private readonly ReadOnlyMemory<byte> _payload;

    public ProgramNameMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(ProgramNameMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>Device Name (FF 09): the name of the device (port) the track is intended for.</summary>
public readonly record struct DeviceNameMessage
{
    public const byte Type = 0x09;
    private readonly ReadOnlyMemory<byte> _payload;

    public DeviceNameMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(DeviceNameMessage));

    public ReadOnlySpan<byte> Bytes => _payload.Span;
    public string Text => Encoding.UTF8.GetString(_payload.Span);
}

/// <summary>MIDI Channel Prefix (FF 20): associates following meta events with a MIDI channel.</summary>
public readonly record struct MidiChannelPrefixMessage
{
    public const byte Type = 0x20;
    internal const int Length = 1;
    private readonly ReadOnlyMemory<byte> _payload;

    public MidiChannelPrefixMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, Length, nameof(MidiChannelPrefixMessage));

    public byte Channel => _payload.Span[0];
}

/// <summary>MIDI Port (FF 21): the MIDI port (bus) that following events are sent to.</summary>
public readonly record struct MidiPortMessage
{
    public const byte Type = 0x21;
    internal const int Length = 1;
    private readonly ReadOnlyMemory<byte> _payload;

    public MidiPortMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, Length, nameof(MidiPortMessage));

    public byte Port => _payload.Span[0];
}

/// <summary>End of Track (FF 2F): marks the end of a track.</summary>
public readonly record struct EndOfTrackMessage
{
    public const byte Type = 0x2F;
    internal const int Length = 0;

    public EndOfTrackMessage(ReadOnlyMemory<byte> data)
        => MetaMessageFormat.Validate(data, Type, Length, nameof(EndOfTrackMessage));
}

/// <summary>Set Tempo (FF 51): the tempo in microseconds per quarter note.</summary>
public readonly record struct SetTempoMessage
{
    public const byte Type = 0x51;
    internal const int Length = 3;
    private readonly ReadOnlyMemory<byte> _payload;

    public SetTempoMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, Length, nameof(SetTempoMessage));

    public int MicrosecondsPerQuarterNote
        => (_payload.Span[0] << 16) | (_payload.Span[1] << 8) | _payload.Span[2];

    public double BeatsPerMinute => 60_000_000.0 / MicrosecondsPerQuarterNote;
}

/// <summary>The SMPTE frame rates used by the SMPTE Offset meta event.</summary>
public enum SmpteFrameRate : byte
{
    Fps24 = 0,
    Fps25 = 1,
    Fps2997Drop = 2,
    Fps30 = 3,
}

/// <summary>SMPTE Offset (FF 54): the SMPTE time at which the track is to start.</summary>
public readonly record struct SmpteOffsetMessage
{
    public const byte Type = 0x54;
    internal const int Length = 5;
    private readonly ReadOnlyMemory<byte> _payload;

    public SmpteOffsetMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, Length, nameof(SmpteOffsetMessage));

    public SmpteFrameRate FrameRate => (SmpteFrameRate)((_payload.Span[0] >> 5) & 0x03);
    public byte Hours => (byte)(_payload.Span[0] & 0x1F);
    public byte Minutes => _payload.Span[1];
    public byte Seconds => _payload.Span[2];
    public byte Frames => _payload.Span[3];
    public byte FractionalFrames => _payload.Span[4];
}

/// <summary>Time Signature (FF 58): the time signature and metronome settings.</summary>
public readonly record struct TimeSignatureMessage
{
    public const byte Type = 0x58;
    internal const int Length = 4;
    private readonly ReadOnlyMemory<byte> _payload;

    public TimeSignatureMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, Length, nameof(TimeSignatureMessage));

    public byte Numerator => _payload.Span[0];

    /// <summary>The denominator as a negative power of two (2 = quarter note).</summary>
    public byte DenominatorPower => _payload.Span[1];

    public int Denominator => 1 << DenominatorPower;
    public byte ClocksPerClick => _payload.Span[2];
    public byte ThirtySecondNotesPerQuarterNote => _payload.Span[3];
}

/// <summary>Key Signature (FF 59): the key as a number of sharps or flats, major or minor.</summary>
public readonly record struct KeySignatureMessage
{
    public const byte Type = 0x59;
    internal const int Length = 2;
    private readonly ReadOnlyMemory<byte> _payload;

    public KeySignatureMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, Length, nameof(KeySignatureMessage));

    /// <summary>-7 (seven flats) to 7 (seven sharps); 0 is C major / A minor.</summary>
    public sbyte Accidentals => (sbyte)_payload.Span[0];

    public bool IsMinor => _payload.Span[1] != 0;
}

/// <summary>Sequencer Specific (FF 7F): proprietary data for a specific sequencer.</summary>
public readonly record struct SequencerSpecificMessage
{
    public const byte Type = 0x7F;
    private readonly ReadOnlyMemory<byte> _payload;

    public SequencerSpecificMessage(ReadOnlyMemory<byte> data)
        => _payload = MetaMessageFormat.Validate(data, Type, -1, nameof(SequencerSpecificMessage));

    public ReadOnlySpan<byte> Data => _payload.Span;
}
