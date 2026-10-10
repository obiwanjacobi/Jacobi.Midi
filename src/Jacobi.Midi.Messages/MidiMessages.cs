using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Jacobi.Midi.Messages;

/// <summary>
/// Identifies the kind of a <see cref="MidiMessage"/>.
/// Channel messages use the status high nibble; system messages use the full status byte.
/// </summary>
public enum MidiMessageKind : byte
{
    Unknown = 0,
    NoteOff = 0x80,
    NoteOn = 0x90,
    PolyphonicKeyPressure = 0xA0,
    ControlChange = 0xB0,
    ProgramChange = 0xC0,
    ChannelPressure = 0xD0,
    PitchBendChange = 0xE0,
    SystemExclusive = 0xF0,
    MtcQuarterFrame = 0xF1,
    SongPositionPointer = 0xF2,
    SongSelect = 0xF3,
    TuneRequest = 0xF6,
    TimingClock = 0xF8,
    Start = 0xFA,
    Continue = 0xFB,
    Stop = 0xFC,
    ActiveSensing = 0xFE,
    SystemReset = 0xFF,
}

/// <summary>
/// A union over the supported MIDI channel messages, backed by the raw message bytes (no copy).
/// </summary>
[Union]
public readonly record struct MidiMessage : IUnion
{
    private readonly uint _data;

    public MidiMessage(uint data)
    {
        _data = data;
    }

    public static MidiMessage Create(byte status, byte data1 = 0, byte data2 = 0)
        => new(status | ((uint)data1 << 8) | ((uint)data2 << 16));

    // IUnion implementation
    public object? Value => HasValue ? _data : null;

    public bool HasValue => _data.Byte(0) >= 0x80 && _data.Byte(0) != 0xF0 && _data.Byte(0) != 0xF7;

    /// <summary>
    /// The kind of message, determined from the status byte only (length is not checked).
    /// </summary>
    public MidiMessageKind Kind
    {
        get
        {
            if (!HasValue) return MidiMessageKind.Unknown;
            var status = _data.Byte(0);
            if (status < 0x80) return MidiMessageKind.Unknown;
            if (status < 0xF0) return (MidiMessageKind)(status & 0xF0);
            var kind = (MidiMessageKind)status;
            return Enum.IsDefined(kind) ? kind : MidiMessageKind.Unknown;
        }
    }

    // channel messages carry the channel in the low nibble; system messages use the full status byte
    private static byte StatusOf(uint data)
    {
        var status = data.Byte(0);
        return status < 0xF0 ? (byte)(status & 0xF0) : status;
    }

    private bool Is(MidiMessageKind kind)
        => HasValue && StatusOf(_data) == (byte)kind;

    internal static uint Validate(uint data, MidiMessageKind kind, string messageName)
    {
        var actual = StatusOf(data);
        if (actual != (byte)kind)
            throw new ArgumentException($"{messageName} requires status 0x{(byte)kind:X2}, but got 0x{actual:X2}.", nameof(data));

        return data;
    }

    // non-boxing union accessors (typed views over the same memory, no copy)
    public bool TryGetValue([NotNullWhen(true)] out NoteOffMessage? value)
    {
        if (Is(MidiMessageKind.NoteOff))
        {
            value = new NoteOffMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out NoteOnMessage? value)
    {
        if (Is(MidiMessageKind.NoteOn))
        {
            value = new NoteOnMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PolyphonicKeyPressureMessage? value)
    {
        if (Is(MidiMessageKind.PolyphonicKeyPressure))
        {
            value = new PolyphonicKeyPressureMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ControlChangeMessage? value)
    {
        if (Is(MidiMessageKind.ControlChange))
        {
            value = new ControlChangeMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ControllerMessage? value)
    {
        if (Is(MidiMessageKind.ControlChange)
            && _data.Byte(1) <= ControllerMessage.LastController)
        {
            value = new ControllerMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ChannelModeMessage? value)
    {
        if (Is(MidiMessageKind.ControlChange)
            && _data.Byte(1) >= ChannelModeMessage.FirstController)
        {
            value = new ChannelModeMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ProgramChangeMessage? value)
    {
        if (Is(MidiMessageKind.ProgramChange))
        {
            value = new ProgramChangeMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ChannelPressureMessage? value)
    {
        if (Is(MidiMessageKind.ChannelPressure))
        {
            value = new ChannelPressureMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PitchBendChangeMessage? value)
    {
        if (Is(MidiMessageKind.PitchBendChange))
        {
            value = new PitchBendChangeMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MtcQuarterFrameMessage? value)
    {
        if (Is(MidiMessageKind.MtcQuarterFrame)) { value = new MtcQuarterFrameMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SongPositionPointerMessage? value)
    {
        if (Is(MidiMessageKind.SongPositionPointer)) { value = new SongPositionPointerMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SongSelectMessage? value)
    {
        if (Is(MidiMessageKind.SongSelect)) { value = new SongSelectMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TuneRequestMessage? value)
    {
        if (Is(MidiMessageKind.TuneRequest)) { value = new TuneRequestMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TimingClockMessage? value)
    {
        if (Is(MidiMessageKind.TimingClock)) { value = new TimingClockMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out StartMessage? value)
    {
        if (Is(MidiMessageKind.Start)) { value = new StartMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ContinueMessage? value)
    {
        if (Is(MidiMessageKind.Continue)) { value = new ContinueMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out StopMessage? value)
    {
        if (Is(MidiMessageKind.Stop)) { value = new StopMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ActiveSensingMessage? value)
    {
        if (Is(MidiMessageKind.ActiveSensing)) { value = new ActiveSensingMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SystemResetMessage? value)
    {
        if (Is(MidiMessageKind.SystemReset)) { value = new SystemResetMessage(_data); return true; }
        value = null;
        return false;
    }
}

/// <summary>Note Off (0x8n): releases a note on a channel.</summary>
public readonly record struct NoteOffMessage
{
    internal const int Length = 3;
    private readonly uint _data;

    /// <summary>Creates a NoteOffMessage from its parameters.</summary>
    public NoteOffMessage(byte channel, byte note, byte velocity)
        : this(PackedMessage.Pack(MidiMessageKind.NoteOff, channel, note, velocity))
    { }

    public NoteOffMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.NoteOff, nameof(NoteOffMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Note => _data.Byte(1);
    public byte Velocity => _data.Byte(2);
}

/// <summary>Note On (0x9n): starts a note on a channel with a velocity.</summary>
public readonly record struct NoteOnMessage
{
    internal const int Length = 3;
    private readonly uint _data;

    /// <summary>Creates a NoteOnMessage from its parameters.</summary>
    public NoteOnMessage(byte channel, byte note, byte velocity)
        : this(PackedMessage.Pack(MidiMessageKind.NoteOn, channel, note, velocity))
    { }

    public NoteOnMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.NoteOn, nameof(NoteOnMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Note => _data.Byte(1);
    public byte Velocity => _data.Byte(2);
}

/// <summary>Polyphonic Key Pressure (0xAn): aftertouch for an individual note.</summary>
public readonly record struct PolyphonicKeyPressureMessage
{
    internal const int Length = 3;
    private readonly uint _data;

    /// <summary>Creates a PolyphonicKeyPressureMessage from its parameters.</summary>
    public PolyphonicKeyPressureMessage(byte channel, byte note, byte pressure)
        : this(PackedMessage.Pack(MidiMessageKind.PolyphonicKeyPressure, channel, note, pressure))
    { }

    public PolyphonicKeyPressureMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.PolyphonicKeyPressure, nameof(PolyphonicKeyPressureMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Note => _data.Byte(1);
    public byte Pressure => _data.Byte(2);
}

/// <summary>Control Change (0xBn): sets a controller to a value. Controllers 120-127 are channel mode messages.</summary>
public readonly record struct ControlChangeMessage
{
    internal const int Length = 3;
    private readonly uint _data;

    /// <summary>Creates a ControlChangeMessage from its parameters.</summary>
    public ControlChangeMessage(byte channel, ControllerNumber controller, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)controller, value))
    { }

    public ControlChangeMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.ControlChange, nameof(ControlChangeMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Controller => _data.Byte(1);
    public byte Value => _data.Byte(2);

    internal uint Data => _data;

    // controller numbers 120-127 are channel mode messages
    public bool IsChannelMode => Controller >= ChannelModeMessage.FirstController;

    /// <summary>Identifies which controller this message is for.</summary>
    public ControllerNumber ControllerNumber => (ControllerNumber)Controller;

    public bool TryGetController([NotNullWhen(true)] out ControllerMessage? value)
    {
        if (!IsChannelMode)
        {
            value = new ControllerMessage(_data);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetChannelMode([NotNullWhen(true)] out ChannelModeMessage? value)
    {
        if (IsChannelMode)
        {
            value = new ChannelModeMessage(_data);
            return true;
        }
        value = null;
        return false;
    }
}

/// <summary>Program Change (0xCn): selects a program (patch) on a channel.</summary>
public readonly record struct ProgramChangeMessage
{
    internal const int Length = 2;
    private readonly uint _data;

    /// <summary>Creates a ProgramChangeMessage from its parameters.</summary>
    public ProgramChangeMessage(byte channel, byte program)
        : this(PackedMessage.Pack(MidiMessageKind.ProgramChange, channel, program))
    { }

    public ProgramChangeMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.ProgramChange, nameof(ProgramChangeMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Program => _data.Byte(1);
}

/// <summary>Channel Pressure (0xDn): aftertouch applied to the whole channel.</summary>
public readonly record struct ChannelPressureMessage
{
    internal const int Length = 2;
    private readonly uint _data;

    /// <summary>Creates a ChannelPressureMessage from its parameters.</summary>
    public ChannelPressureMessage(byte channel, byte pressure)
        : this(PackedMessage.Pack(MidiMessageKind.ChannelPressure, channel, pressure))
    { }

    public ChannelPressureMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.ChannelPressure, nameof(ChannelPressureMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Pressure => _data.Byte(1);
}

/// <summary>Pitch Bend Change (0xEn): 14-bit pitch wheel value on a channel.</summary>
public readonly record struct PitchBendChangeMessage
{
    internal const int Length = 3;
    private readonly uint _data;

    /// <summary>Creates a PitchBendChangeMessage from its parameters.</summary>
    /// <param name="value">14-bit value, 0-16383, with 8192 as center (no pitch bend). TODO: not very user friendly!</param>
    public PitchBendChangeMessage(byte channel, int value)
        : this(PackedMessage.Pack(MidiMessageKind.PitchBendChange, channel, (byte)(PackedMessage.CheckRange(value, 0, 0x3FFF) & 0x7F), (byte)(value >> 7)))
    { }

    public PitchBendChangeMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.PitchBendChange, nameof(PitchBendChangeMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    // 14-bit value: LSB in byte 1, MSB in byte 2
    public int Value => (_data.Byte(2) << 7) | _data.Byte(1);
}
