namespace Jacobi.Midi.Messages;

internal static class PackedMessage
{
    // byte 0 = status, byte 1 = data1, byte 2 = data2 (little-endian packing)
    internal static byte Byte(this uint data, int index) => (byte)(data >> (index * 8));

    /// <summary>Packs a channel message; the channel (0-15) is merged into the status byte.</summary>
    internal static uint Pack(MidiMessageKind kind, byte channel, byte data1 = 0, byte data2 = 0)
    {
        if (channel > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "The channel must be in the range 0-15.");

        return PackSystem(kind, data1, data2) | channel;
    }

    /// <summary>Packs a message without a channel (system messages).</summary>
    internal static uint PackSystem(MidiMessageKind kind, byte data1 = 0, byte data2 = 0)
    {
        if (data1 > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(data1), data1, "Data bytes must be in the range 0-127.");
        if (data2 > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(data2), data2, "Data bytes must be in the range 0-127.");

        return (byte)kind | ((uint)data1 << 8) | ((uint)data2 << 16);
    }

    /// <summary>Validates that a value lies within an inclusive range.</summary>
    internal static int CheckRange(int value, int min, int max)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"The value ({value}) must be in the range {min}-{max}.");
        return value;
    }

    /// <summary>Packs the MIDI Time Code quarter frame data byte.</summary>
    internal static byte MtcData(byte messageType, byte value)
    {
        if (messageType > 7)
            throw new ArgumentOutOfRangeException(nameof(messageType), messageType, "The message type must be in the range 0-7.");
        if (value > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be in the range 0-15.");
        return (byte)((messageType << 4) | value);
    }
}
