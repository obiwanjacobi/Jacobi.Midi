using System.Runtime.CompilerServices;

namespace Jacobi.Midi.Messages;

/// <summary>
/// Knowledge about the MIDI byte stream (wire) format shared by the reader and the writer.
/// </summary>
internal static class MidiWire
{
    /// <summary>
    /// The total message length (status included) for a non-sysex status byte,
    /// or -1 when the status is not a (supported) message.
    /// </summary>
    public static int LengthOf(byte status) => status switch
    {
        < 0x80 => -1,
        < 0xC0 => 3,    // note off/on, poly pressure, control change
        < 0xE0 => 2,    // program change, channel pressure
        < 0xF0 => 3,    // pitch bend
        0xF1 or 0xF3 => 2,
        0xF2 => 3,
        0xF6 or 0xF8 or 0xFA or 0xFB or 0xFC or 0xFE or 0xFF => 1,
        _ => -1,        // 0xF0 (sysex), 0xF7 (sysex end) and undefined statuses
    };

    public static bool IsStatus(byte b) => b >= 0x80;

    public static bool IsChannelStatus(byte status) => status is >= 0x80 and < 0xF0;

    public static bool IsRealTime(byte status) => status >= 0xF8;

    /// <summary>The packed uint representation of a <see cref="MidiMessage"/> (status, data1, data2).</summary>
    // MidiMessage is a single-uint struct; this avoids adding a public accessor to the message type.
    public static uint Pack(MidiMessage message) => Unsafe.BitCast<MidiMessage, uint>(message);
}
