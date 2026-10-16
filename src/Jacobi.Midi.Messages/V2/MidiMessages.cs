using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Messages.V2;

/// <summary>
/// MIDI 2.0 Channel Voice status nibbles (UMP Message Type 0x4).
/// </summary>
public enum MidiMessageKind : byte
{
    RegisteredPerNoteController = 0x0,
    AssignablePerNoteController = 0x1,
    RegisteredController = 0x2,
    AssignableController = 0x3,
    RelativeRegisteredController = 0x4,
    RelativeAssignableController = 0x5,
    PerNotePitchBend = 0x6,
    NoteOff = 0x8,
    NoteOn = 0x9,
    PolyphonicKeyPressure = 0xA,
    ControlChange = 0xB,
    ProgramChange = 0xC,
    ChannelPressure = 0xD,
    PitchBendChange = 0xE,
    PerNoteManagement = 0xF,
    Unknown = 0xFF,
}

/// <summary>
/// Interpretation of the 16-bit attribute data in a Note On or Note Off message.
/// </summary>
public enum NoteAttributeType : byte
{
    None = 0,
    ManufacturerSpecific = 1,
    ProfileSpecific = 2,
    Pitch = 3,
}

/// <summary>
/// Actions applied to the per-note controllers of a note number.
/// </summary>
[Flags]
public enum PerNoteManagementOptions : byte
{
    None = 0,
    Reset = 1,
    Detach = 2,
}

/// <summary>
/// A packed union of all MIDI 2.0 Channel Voice messages from M2-104-UM v1.1.2, section 7.4.
/// The first UMP word occupies the low 32 bits of <see cref="Data"/>; words retain UMP bit order.
/// </summary>
[Union]
public readonly record struct MidiMessage : IUnion, IUniversalPacket
{
    private readonly ulong _data;

    public MidiMessage(ulong data) => _data = data;

    public MidiMessage(uint word0, uint word1) : this(word0 | ((ulong)word1 << 32)) { }

    public MidiMessage(UniversalPacket64 packet) : this(packet[0], packet[1]) { }

    public ulong Data => _data;
    public UniversalPacket64 Packet => new(_data);
    public byte Group => (byte)((this[0] >> 24) & 0xF);
    public byte Channel => (byte)((this[0] >> 16) & 0xF);
    public PacketKind PacketKind => PacketKind.Packet64;
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);
    public bool HasValue => MessageKind == MessageKind.Midi2ChannelVoice64 && Status != 7;
    public object? Value => HasValue ? _data : null;
    public MidiMessageKind Kind => HasValue ? (MidiMessageKind)Status : MidiMessageKind.Unknown;
    private byte Status => (byte)((this[0] >> 20) & 0xF);
    internal byte Byte3 => (byte)(this[0] >> 8);
    internal byte Byte4 => (byte)this[0];

    public uint this[int index] => index switch
    {
        0 => (uint)_data,
        1 => (uint)(_data >> 32),
        _ => throw new IndexOutOfRangeException($"Expected 0-1 and got {index}."),
    };

    public void CopyTo(Span<uint> destination)
    {
        if (destination.Length < 2)
            throw new ArgumentException("A MIDI 2.0 Channel Voice message requires two words.", nameof(destination));
        destination[0] = this[0];
        destination[1] = this[1];
    }

    internal static MidiMessage Validate(ulong data, MidiMessageKind kind)
    {
        var message = new MidiMessage(data);
        if (message.Kind != kind)
            throw new ArgumentException($"Expected MIDI 2.0 Channel Voice status 0x{(byte)kind:X}, but got {message.Kind}.", nameof(data));
        return message;
    }

    internal static ulong Pack(MidiMessageKind kind, byte group, byte channel, byte byte3, byte byte4, uint word1)
    {
        Check(group, 15, nameof(group));
        Check(channel, 15, nameof(channel));
        return new MidiMessage(0x40000000U | ((uint)group << 24) | ((uint)kind << 20)
            | ((uint)channel << 16) | ((uint)byte3 << 8) | byte4, word1).Data;
    }

    internal static byte Check(byte value, byte maximum, string parameterName)
    {
        if (value > maximum)
            throw new ArgumentOutOfRangeException(parameterName, value, $"Expected a value from 0 to {maximum}.");
        return value;
    }

    internal static ulong PackNote(MidiMessageKind kind, byte group, byte channel, byte note,
        ushort velocity, NoteAttributeType attributeType, ushort attributeData)
    {
        Check((byte)attributeType, 3, nameof(attributeType));
        if (attributeType == NoteAttributeType.None && attributeData != 0)
            throw new ArgumentException("Attribute data must be zero when no attribute is selected.", nameof(attributeData));
        return Pack(kind, group, channel, Check(note, 127, nameof(note)), (byte)attributeType,
            ((uint)velocity << 16) | attributeData);
    }

    internal static ulong PackController(MidiMessageKind kind, byte group, byte channel, byte bank, byte index, uint value)
        => Pack(kind, group, channel, Check(bank, 127, nameof(bank)), Check(index, 127, nameof(index)), value);

    public bool TryGetValue([NotNullWhen(true)] out RegisteredPerNoteControllerMessage? value)
    {
        if (Kind == MidiMessageKind.RegisteredPerNoteController) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out AssignablePerNoteControllerMessage? value)
    {
        if (Kind == MidiMessageKind.AssignablePerNoteController) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out RegisteredControllerMessage? value)
    {
        if (Kind == MidiMessageKind.RegisteredController) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out AssignableControllerMessage? value)
    {
        if (Kind == MidiMessageKind.AssignableController) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out RelativeRegisteredControllerMessage? value)
    {
        if (Kind == MidiMessageKind.RelativeRegisteredController) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out RelativeAssignableControllerMessage? value)
    {
        if (Kind == MidiMessageKind.RelativeAssignableController) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PerNotePitchBendMessage? value)
    {
        if (Kind == MidiMessageKind.PerNotePitchBend) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out NoteOffMessage? value)
    {
        if (Kind == MidiMessageKind.NoteOff) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out NoteOnMessage? value)
    {
        if (Kind == MidiMessageKind.NoteOn) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PolyphonicKeyPressureMessage? value)
    {
        if (Kind == MidiMessageKind.PolyphonicKeyPressure) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ControlChangeMessage? value)
    {
        if (Kind == MidiMessageKind.ControlChange) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ProgramChangeMessage? value)
    {
        if (Kind == MidiMessageKind.ProgramChange) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ChannelPressureMessage? value)
    {
        if (Kind == MidiMessageKind.ChannelPressure) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PitchBendChangeMessage? value)
    {
        if (Kind == MidiMessageKind.PitchBendChange) { value = new(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PerNoteManagementMessage? value)
    {
        if (Kind == MidiMessageKind.PerNoteManagement) { value = new(_data); return true; }
        value = null;
        return false;
    }
}

/// <summary>
/// Sets a registered controller for one note; the controller index is 8 bits.
/// </summary>
public readonly record struct RegisteredPerNoteControllerMessage
{
    private readonly MidiMessage _message;
    public RegisteredPerNoteControllerMessage(byte group, byte channel, byte note, byte controller, uint value)
        : this(MidiMessage.Pack(MidiMessageKind.RegisteredPerNoteController, group, channel,
            MidiMessage.Check(note, 127, nameof(note)), controller, value)) { }
    public RegisteredPerNoteControllerMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.RegisteredPerNoteController);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Note => (byte)(_message.Byte3 & 0x7F);
    public byte Controller => _message.Byte4;
    public uint Value => _message[1];
}

/// <summary>
/// Sets an application-defined controller for one note; the controller index is 8 bits.
/// </summary>
public readonly record struct AssignablePerNoteControllerMessage
{
    private readonly MidiMessage _message;
    public AssignablePerNoteControllerMessage(byte group, byte channel, byte note, byte controller, uint value)
        : this(MidiMessage.Pack(MidiMessageKind.AssignablePerNoteController, group, channel,
            MidiMessage.Check(note, 127, nameof(note)), controller, value)) { }
    public AssignablePerNoteControllerMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.AssignablePerNoteController);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Note => (byte)(_message.Byte3 & 0x7F);
    public byte Controller => _message.Byte4;
    public uint Value => _message[1];
}

/// <summary>
/// Sets a registered channel controller, addressed by a 7-bit bank and 7-bit index (RPN).
/// </summary>
public readonly record struct RegisteredControllerMessage
{
    private readonly MidiMessage _message;
    public RegisteredControllerMessage(byte group, byte channel, byte bank, byte index, uint value)
        : this(MidiMessage.PackController(MidiMessageKind.RegisteredController, group, channel, bank, index, value)) { }
    public RegisteredControllerMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.RegisteredController);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Bank => (byte)(_message.Byte3 & 0x7F);
    public byte Index => (byte)(_message.Byte4 & 0x7F);
    public uint Value => _message[1];
}

/// <summary>
/// Sets an assignable channel controller, addressed by a 7-bit bank and 7-bit index (NRPN).
/// </summary>
public readonly record struct AssignableControllerMessage
{
    private readonly MidiMessage _message;
    public AssignableControllerMessage(byte group, byte channel, byte bank, byte index, uint value)
        : this(MidiMessage.PackController(MidiMessageKind.AssignableController, group, channel, bank, index, value)) { }
    public AssignableControllerMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.AssignableController);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Bank => (byte)(_message.Byte3 & 0x7F);
    public byte Index => (byte)(_message.Byte4 & 0x7F);
    public uint Value => _message[1];
}

/// <summary>
/// Adjusts a registered channel controller by a signed 32-bit two's-complement amount.
/// </summary>
public readonly record struct RelativeRegisteredControllerMessage
{
    private readonly MidiMessage _message;
    public RelativeRegisteredControllerMessage(byte group, byte channel, byte bank, byte index, int value)
        : this(MidiMessage.PackController(MidiMessageKind.RelativeRegisteredController, group, channel, bank, index, unchecked((uint)value))) { }
    public RelativeRegisteredControllerMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.RelativeRegisteredController);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Bank => (byte)(_message.Byte3 & 0x7F);
    public byte Index => (byte)(_message.Byte4 & 0x7F);
    public int Value => unchecked((int)_message[1]);
}

/// <summary>
/// Adjusts an assignable channel controller by a signed 32-bit two's-complement amount.
/// </summary>
public readonly record struct RelativeAssignableControllerMessage
{
    private readonly MidiMessage _message;
    public RelativeAssignableControllerMessage(byte group, byte channel, byte bank, byte index, int value)
        : this(MidiMessage.PackController(MidiMessageKind.RelativeAssignableController, group, channel, bank, index, unchecked((uint)value))) { }
    public RelativeAssignableControllerMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.RelativeAssignableController);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Bank => (byte)(_message.Byte3 & 0x7F);
    public byte Index => (byte)(_message.Byte4 & 0x7F);
    public int Value => unchecked((int)_message[1]);
}

/// <summary>
/// Bends one note's pitch with an unsigned 32-bit value, centered at 0x80000000.
/// </summary>
public readonly record struct PerNotePitchBendMessage
{
    private readonly MidiMessage _message;
    public PerNotePitchBendMessage(byte group, byte channel, byte note, uint value)
        : this(MidiMessage.Pack(MidiMessageKind.PerNotePitchBend, group, channel,
            MidiMessage.Check(note, 127, nameof(note)), 0, value)) { }
    public PerNotePitchBendMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.PerNotePitchBend);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Note => (byte)(_message.Byte3 & 0x7F);
    public uint Value => _message[1];
}

/// <summary>
/// Releases a note with 16-bit velocity and optional 16-bit attribute data.
/// </summary>
public readonly record struct NoteOffMessage
{
    private readonly MidiMessage _message;
    public NoteOffMessage(byte group, byte channel, byte note, ushort velocity,
        NoteAttributeType attributeType = NoteAttributeType.None, ushort attributeData = 0)
        : this(MidiMessage.PackNote(MidiMessageKind.NoteOff, group, channel, note, velocity, attributeType, attributeData)) { }
    public NoteOffMessage(ulong data) => _message = MidiMessage.Validate(data, MidiMessageKind.NoteOff);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Note => (byte)(_message.Byte3 & 0x7F);
    public ushort Velocity => (ushort)(_message[1] >> 16);
    public NoteAttributeType AttributeType => (NoteAttributeType)_message.Byte4;
    public ushort AttributeData => (ushort)_message[1];
}

/// <summary>
/// Starts a note with 16-bit velocity and optional 16-bit attribute data. Zero velocity is still Note On.
/// </summary>
public readonly record struct NoteOnMessage
{
    private readonly MidiMessage _message;
    public NoteOnMessage(byte group, byte channel, byte note, ushort velocity,
        NoteAttributeType attributeType = NoteAttributeType.None, ushort attributeData = 0)
        : this(MidiMessage.PackNote(MidiMessageKind.NoteOn, group, channel, note, velocity, attributeType, attributeData)) { }
    public NoteOnMessage(ulong data) => _message = MidiMessage.Validate(data, MidiMessageKind.NoteOn);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Note => (byte)(_message.Byte3 & 0x7F);
    public ushort Velocity => (ushort)(_message[1] >> 16);
    public NoteAttributeType AttributeType => (NoteAttributeType)_message.Byte4;
    public ushort AttributeData => (ushort)_message[1];
}

/// <summary>
/// Sets 32-bit polyphonic key pressure for one note.
/// </summary>
public readonly record struct PolyphonicKeyPressureMessage
{
    private readonly MidiMessage _message;
    public PolyphonicKeyPressureMessage(byte group, byte channel, byte note, uint pressure)
        : this(MidiMessage.Pack(MidiMessageKind.PolyphonicKeyPressure, group, channel,
            MidiMessage.Check(note, 127, nameof(note)), 0, pressure)) { }
    public PolyphonicKeyPressureMessage(ulong data)
        => _message = MidiMessage.Validate(data, MidiMessageKind.PolyphonicKeyPressure);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Note => (byte)(_message.Byte3 & 0x7F);
    public uint Pressure => _message[1];
}

/// <summary>
/// Sets a 7-bit controller number to a 32-bit value. Numbers 120-127 select channel modes.
/// </summary>
public readonly record struct ControlChangeMessage
{
    private readonly MidiMessage _message;
    public ControlChangeMessage(byte group, byte channel, ControllerNumber controller, uint value)
        : this(MidiMessage.Pack(MidiMessageKind.ControlChange, group, channel,
            MidiMessage.Check((byte)controller, 127, nameof(controller)), 0, value)) { }
    public ControlChangeMessage(ulong data) => _message = MidiMessage.Validate(data, MidiMessageKind.ControlChange);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Controller => (byte)(_message.Byte3 & 0x7F);
    public ControllerNumber ControllerNumber => (ControllerNumber)Controller;
    public bool IsChannelMode => Controller >= 120;
    public uint Value => _message[1];
}

/// <summary>
/// Selects a program and optionally a bank. A null bank leaves the current bank unchanged.
/// </summary>
public readonly record struct ProgramChangeMessage
{
    private readonly MidiMessage _message;
    public ProgramChangeMessage(byte group, byte channel, byte program, ushort? bank = null)
        : this(Pack(group, channel, program, bank)) { }
    public ProgramChangeMessage(ulong data) => _message = MidiMessage.Validate(data, MidiMessageKind.ProgramChange);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Program => (byte)((_message[1] >> 24) & 0x7F);
    public bool BankValid => (_message.Byte4 & 1) != 0;
    public byte BankMsb => (byte)((_message[1] >> 8) & 0x7F);
    public byte BankLsb => (byte)(_message[1] & 0x7F);
    public ushort? Bank => BankValid ? (ushort)((BankMsb << 7) | BankLsb) : null;

    private static ulong Pack(byte group, byte channel, byte program, ushort? bank)
    {
        MidiMessage.Check(program, 127, nameof(program));
        if (bank > 0x3FFF)
            throw new ArgumentOutOfRangeException(nameof(bank), bank, "Expected a bank from 0 to 16383.");
        uint bankData = bank.HasValue ? ((uint)(bank.Value >> 7) << 8) | (uint)(bank.Value & 0x7F) : 0;
        return MidiMessage.Pack(MidiMessageKind.ProgramChange, group, channel, 0,
            (byte)(bank.HasValue ? 1 : 0), ((uint)program << 24) | bankData);
    }
}

/// <summary>
/// Sets 32-bit pressure for a channel.
/// </summary>
public readonly record struct ChannelPressureMessage
{
    private readonly MidiMessage _message;
    public ChannelPressureMessage(byte group, byte channel, uint pressure)
        : this(MidiMessage.Pack(MidiMessageKind.ChannelPressure, group, channel, 0, 0, pressure)) { }
    public ChannelPressureMessage(ulong data) => _message = MidiMessage.Validate(data, MidiMessageKind.ChannelPressure);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public uint Pressure => _message[1];
}

/// <summary>
/// Bends a channel's pitch with an unsigned 32-bit value, centered at 0x80000000.
/// </summary>
public readonly record struct PitchBendChangeMessage
{
    private readonly MidiMessage _message;
    public PitchBendChangeMessage(byte group, byte channel, uint value)
        : this(MidiMessage.Pack(MidiMessageKind.PitchBendChange, group, channel, 0, 0, value)) { }
    public PitchBendChangeMessage(ulong data) => _message = MidiMessage.Validate(data, MidiMessageKind.PitchBendChange);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public uint Value => _message[1];
}

/// <summary>
/// Resets and/or detaches per-note controllers for a note number.
/// </summary>
public readonly record struct PerNoteManagementMessage
{
    private readonly MidiMessage _message;
    public PerNoteManagementMessage(byte group, byte channel, byte note, PerNoteManagementOptions options)
        : this(MidiMessage.Pack(MidiMessageKind.PerNoteManagement, group, channel,
            MidiMessage.Check(note, 127, nameof(note)), MidiMessage.Check((byte)options, 3, nameof(options)), 0)) { }
    public PerNoteManagementMessage(ulong data) => _message = MidiMessage.Validate(data, MidiMessageKind.PerNoteManagement);
    public ulong Data => _message.Data;
    public byte Group => _message.Group;
    public byte Channel => _message.Channel;
    public byte Note => (byte)(_message.Byte3 & 0x7F);
    public PerNoteManagementOptions Options => (PerNoteManagementOptions)(_message.Byte4 & 3);
    public bool Reset => (Options & PerNoteManagementOptions.Reset) != 0;
    public bool Detach => (Options & PerNoteManagementOptions.Detach) != 0;
}