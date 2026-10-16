using System.Diagnostics.CodeAnalysis;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Messages.V2;

/// <summary>
/// Identifies the role of a UMP in a SysEx7 or SysEx8 message.
/// </summary>
public enum SysExPacketStatus : byte
{
    Complete = 0,
    Start = 1,
    Continue = 2,
    End = 3,
}

/// <summary>
/// A 64-bit SysEx7 UMP carrying up to six 7-bit payload bytes, without F0/F7 framing.
/// Word zero occupies the low 32 bits of Data, matching UniversalPacket64.
/// </summary>
public readonly record struct SysEx7Message : IUniversalPacket
{
    private readonly ulong _data;

    public SysEx7Message(byte group, SysExPacketStatus status, ReadOnlySpan<byte> payload)
        : this((ulong)SysExPacket.Pack(group, status, payload, false, 0)) { }

    public SysEx7Message(ulong data)
    {
        if (!IsValid(data))
            throw new ArgumentException("Expected a SysEx7 UMP with status 0-3 and 0-6 seven-bit payload bytes.", nameof(data));
        _data = data;
    }

    public SysEx7Message(uint word0, uint word1) : this(word0 | ((ulong)word1 << 32)) { }

    public SysEx7Message(UniversalPacket64 packet) : this(packet[0], packet[1]) { }

    public ulong Data => _data;
    public UniversalPacket64 Packet => new(_data);
    public bool HasValue => IsValid(_data);
    public byte Group => (byte)((this[0] >> 24) & 0xF);
    public SysExPacketStatus Status => (SysExPacketStatus)((this[0] >> 20) & 0xF);
    public int PayloadLength => (int)((this[0] >> 16) & 0xF);
    public PacketKind PacketKind => PacketKind.Packet64;
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);

    public uint this[int index] => index switch
    {
        0 => (uint)_data,
        1 => (uint)(_data >> 32),
        _ => throw new IndexOutOfRangeException($"Expected 0-1 and got {index}."),
    };

    /// <summary>
    /// Reads a valid payload byte in transmission order, excluding the UMP header.
    /// </summary>
    public byte GetPayloadByte(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PayloadLength);
        return SysExPacket.Byte(_data, index + 2);
    }

    public void CopyTo(Span<uint> destination)
    {
        if (destination.Length < 2)
            throw new ArgumentException("A SysEx7 UMP requires two words.", nameof(destination));
        destination[0] = this[0];
        destination[1] = this[1];
    }

    /// <summary>
    /// Copies only valid payload bytes, excluding the header and padding.
    /// </summary>
    public void CopyPayloadTo(Span<byte> destination)
        => SysExPacket.CopyPayloadTo(_data, 2, PayloadLength, destination);

    /// <summary>
    /// Restores MIDI 1.0 framing for a nonempty Complete packet, allowing reuse of existing SysEx views.
    /// Fragment packets require reassembly before they can be interpreted as complete messages.
    /// </summary>
    public bool TryGetSystemExclusive([NotNullWhen(true)] out SystemExclusiveMessage? value)
    {
        if (!HasValue || Status != SysExPacketStatus.Complete || PayloadLength == 0)
        {
            value = null;
            return false;
        }

        var bytes = new byte[PayloadLength + 2];
        bytes[0] = 0xF0;
        CopyPayloadTo(bytes.AsSpan(1, PayloadLength));
        bytes[^1] = 0xF7;
        value = new SystemExclusiveMessage(bytes);
        return true;
    }

    private static bool IsValid(ulong data)
    {
        var word0 = (uint)data;
        int count = (int)((word0 >> 16) & 0xF);
        if (word0 >> 28 != 3 || ((word0 >> 20) & 0xF) > 3 || count > 6)
            return false;
        for (int index = 0; index < count; index++)
        {
            if (SysExPacket.Byte(data, index + 2) > 127)
                return false;
        }
        return true;
    }
}

/// <summary>
/// A 128-bit SysEx8 UMP carrying a Stream ID and up to thirteen 8-bit payload bytes.
/// Word zero occupies the low 32 bits of Data, matching UniversalPacket128.
/// </summary>
public readonly record struct SysEx8Message : IUniversalPacket
{
    private readonly UInt128 _data;

    public SysEx8Message(byte group, SysExPacketStatus status, byte streamId, ReadOnlySpan<byte> payload)
        : this(SysExPacket.Pack(group, status, payload, true, streamId)) { }

    public SysEx8Message(UInt128 data)
    {
        if (!IsValid(data))
            throw new ArgumentException("Expected a SysEx8 UMP with status 0-3 and byte count 1-14, or an End abort with byte count 15.", nameof(data));
        _data = data;
    }

    public SysEx8Message(uint word0, uint word1, uint word2, uint word3)
        : this((UInt128)word0 | ((UInt128)word1 << 32) | ((UInt128)word2 << 64) | ((UInt128)word3 << 96)) { }

    public SysEx8Message(UniversalPacket128 packet) : this(packet[0], packet[1], packet[2], packet[3]) { }

    public UInt128 Data => _data;
    public UniversalPacket128 Packet => new(_data);
    public bool HasValue => IsValid(_data);
    public byte Group => (byte)((this[0] >> 24) & 0xF);
    public SysExPacketStatus Status => (SysExPacketStatus)((this[0] >> 20) & 0xF);
    public byte StreamId => (byte)(this[0] >> 8);

    /// <summary>
    /// The wire byte count, including the Stream ID; 15 denotes an abort, not a payload length.
    /// </summary>
    public byte ByteCount => (byte)((this[0] >> 16) & 0xF);

    public bool IsAborted => Status == SysExPacketStatus.End && ByteCount == 15;
    public int PayloadLength => IsAborted || ByteCount == 0 ? 0 : ByteCount - 1;
    public PacketKind PacketKind => PacketKind.Packet128;
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);

    public uint this[int index] => index switch
    {
        >= 0 and < 4 => (uint)(_data >> (index * 32)),
        _ => throw new IndexOutOfRangeException($"Expected 0-3 and got {index}."),
    };

    /// <summary>
    /// Creates an End UMP that discards an incomplete or untrusted message on this Group and Stream ID.
    /// </summary>
    public static SysEx8Message CreateAbort(byte group, byte streamId)
    {
        MidiMessage.Check(group, 15, nameof(group));
        return new SysEx8Message(0x503F0000U | ((uint)group << 24) | ((uint)streamId << 8), 0, 0, 0);
    }

    /// <summary>
    /// Reads a valid payload byte in transmission order, excluding the header and Stream ID.
    /// </summary>
    public byte GetPayloadByte(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PayloadLength);
        return SysExPacket.Byte(_data, index + 3);
    }

    public void CopyTo(Span<uint> destination)
    {
        if (destination.Length < 4)
            throw new ArgumentException("A SysEx8 UMP requires four words.", nameof(destination));
        for (int index = 0; index < 4; index++)
            destination[index] = this[index];
    }

    /// <summary>
    /// Copies only valid payload bytes, excluding the header, Stream ID, and padding. Aborts have no payload.
    /// </summary>
    public void CopyPayloadTo(Span<byte> destination)
        => SysExPacket.CopyPayloadTo(_data, 3, PayloadLength, destination);

    private static bool IsValid(UInt128 data)
    {
        var word0 = (uint)data;
        uint status = (word0 >> 20) & 0xF;
        uint count = (word0 >> 16) & 0xF;
        return word0 >> 28 == 5 && status <= 3
            && (count is >= 1 and <= 14 || (status == 3 && count == 15));
    }
}

internal static class SysExPacket
{
    internal static byte Byte(UInt128 data, int index)
        => (byte)(data >> ((index / 4 * 32) + ((3 - index % 4) * 8)));

    internal static UInt128 Pack(byte group, SysExPacketStatus status, ReadOnlySpan<byte> payload, bool eightBit, byte streamId)
    {
        MidiMessage.Check(group, 15, nameof(group));
        MidiMessage.Check((byte)status, 3, nameof(status));
        int capacity = eightBit ? 13 : 6;
        if (payload.Length > capacity)
            throw new ArgumentException($"Expected at most {capacity} payload bytes.", nameof(payload));

        uint count = (uint)(payload.Length + (eightBit ? 1 : 0));
        UInt128 data = (eightBit ? 0x50000000U : 0x30000000U) | ((uint)group << 24)
            | ((uint)status << 20) | (count << 16) | (eightBit ? (uint)streamId << 8 : 0U);
        int offset = eightBit ? 3 : 2;
        for (int index = 0; index < payload.Length; index++)
        {
            if (!eightBit && payload[index] > 127)
                throw new ArgumentOutOfRangeException(nameof(payload), "SysEx7 payload bytes must be in the range 0-127.");
            int position = index + offset;
            data |= (UInt128)payload[index] << ((position / 4 * 32) + ((3 - position % 4) * 8));
        }
        return data;
    }

    internal static void CopyPayloadTo(UInt128 data, int offset, int length, Span<byte> destination)
    {
        if (destination.Length < length)
            throw new ArgumentException($"Expected space for at least {length} payload bytes.", nameof(destination));
        for (int index = 0; index < length; index++)
            destination[index] = Byte(data, index + offset);
    }
}