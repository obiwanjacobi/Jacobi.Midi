namespace Jacobi.Midi.Core;

public readonly record struct MidiChannel(byte Value);

public readonly record struct MidiGroup(byte Value);

public readonly record struct MidiTimestamp(ulong Value);

public enum PacketKind
{
    None,
    Packet32,
    Packet64,
    Packet96,
    Packet128,
}

public enum MessageKind
{
    /// <summary>32-bit utility message</summary>
    UtilityMessage32 = 0x00,
    /// <summary>32-bit system common or system real-time message</summary>
    SystemCommon32 = 0x01,
    /// <summary>32-bit MIDI 1.0 channel voice message</summary>
    Midi1ChannelVoice32 = 0x02,
    /// <summary>64-bit data message, which includes MIDI 1.0 System Exclusive</summary>
    DataMessage64 = 0x03,
    /// <summary>64-bit MIDI 2.0 channel voice message</summary>
    Midi2ChannelVoice64 = 0x04,
    /// <summary>128-bit data message, such as System Exclusive 8</summary>
    DataMessage128 = 0x05,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReserved632 = 0x06,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReserved732 = 0x07,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReserved864 = 0x08,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReserved964 = 0x09,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReservedA64 = 0x0A,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReservedB96 = 0x0B,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReservedC96 = 0x0C,
    /// <summary>128-bit Flex Data message, such as tempo, time signature, or lyrics</summary>
    FlexData128 = 0x0D,
    /// <summary>Set aside for future use by the MIDI standards groups</summary>
    FutureReservedE128 = 0x0E,
    /// <summary>128-bit stream message, such as endpoint discovery and function block messages</summary>
    Stream128 = 0x0F,
}

// exception base class for all midi specific errors
[Serializable]
public class MidiException : Exception
{
    public MidiException() { }
    public MidiException(string message) : base(message) { }
    public MidiException(string message, Exception inner) : base(message, inner) { }
}