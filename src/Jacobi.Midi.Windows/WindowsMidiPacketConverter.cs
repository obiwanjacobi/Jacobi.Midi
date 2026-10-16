using Jacobi.Midi.Core;
using Native = Windows.Devices.Midi2;

namespace Jacobi.Midi.Windows;

internal static class WindowsMidiPacketConverter
{
    internal static Native.MidiMessageStruct ToNative(UniversalPacket packet)
    {
        var count = (int)packet.PacketKind;
        if (count is < 1 or > 4)
            throw new ArgumentException("A MIDI packet must contain one to four words.", nameof(packet));
        var word0 = packet[0];
        if (WordCount(word0) != count)
            throw new ArgumentException("The UMP message type does not match the packet width.", nameof(packet));
        return new Native.MidiMessageStruct(word0, count > 1 ? packet[1] : 0,
            count > 2 ? packet[2] : 0, count > 3 ? packet[3] : 0);
    }

    internal static UniversalPacket FromNative(byte count, Native.MidiMessageStruct message)
    {
        if (count is < 1 or > 4 || WordCount(message.Word0) != count)
            throw new MidiException("Windows MIDI Services delivered an invalid UMP packet width.");
        uint[] words = count switch
        {
            1 => [message.Word0],
            2 => [message.Word0, message.Word1],
            3 => [message.Word0, message.Word1, message.Word2],
            4 => [message.Word0, message.Word1, message.Word2, message.Word3],
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };
        return new UniversalPacket(words);
    }

    internal static byte WordCount(uint word0) => (word0 >> 28) switch
    {
        0x0 or 0x1 or 0x2 or 0x6 or 0x7 => 1,
        0x3 or 0x4 or 0x8 or 0x9 or 0xA => 2,
        0xB or 0xC => 3,
        _ => 4
    };
}
