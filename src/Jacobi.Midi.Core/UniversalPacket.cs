using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Jacobi.Midi.Core;

public interface IUniversalPacket
{
    PacketKind PacketKind { get; }
    MessageKind MessageKind { get; }
    UInt32 this[int index] { get; }
    void CopyTo(Span<UInt32> destination);
}

public readonly record struct StampedPacket(UniversalPacket Packet, ulong Timestamp);

[Union]
public readonly record struct UniversalPacket : IUnion, IUniversalPacket
{
    private readonly UInt32[]? _data;

    public UniversalPacket(UInt32[] data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        if (data.Length > 4)
            throw new ArgumentException($"Expected a maximum length of 4, but got {data.Length}.", nameof(data));

        _data = data;
    }

    public object? Value => _data;

    public bool HasValue => _data?.Length > 0;

    public bool TryGetValue([NotNullWhen(true)] out UniversalPacket32? value)
    {
        if (_data?.Length == 1)
        {
            value = new UniversalPacket32(PacketPacker.ToUInt32(_data));
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out UniversalPacket64? value)
    {
        if (_data?.Length == 2)
        {
            value = new UniversalPacket64(_data);
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out UniversalPacket96? value)
    {
        if (_data?.Length == 3)
        {
            value = new UniversalPacket96(_data);
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out UniversalPacket128? value)
    {
        if (_data?.Length == 4)
        {
            value = new UniversalPacket128(_data);
            return true;
        }

        value = null;
        return false;
    }

    public PacketKind PacketKind => (PacketKind)(_data?.Length ?? 0);
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);

    public uint this[int index] => _data?[index]
        ?? throw new InvalidOperationException("This UniversalPacket instance is unitialized.");

    public void CopyTo(Span<uint> destination)
    {
        for (int i = 0; i < (int)PacketKind; i++)
        {
            destination[i] = this[i];
        }
    }
}

public readonly record struct UniversalPacket32 : IUniversalPacket
{
    private readonly UInt32 _data;

    public UniversalPacket32(UInt32 data)
        => _data = data;

    public PacketKind PacketKind => PacketKind.Packet32;
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);

    public uint this[int index] => PacketPacker.FromIndex32(_data, index);

    public void CopyTo(Span<uint> destination)
    {
        for (int i = 0; i < (int)PacketKind; i++)
        {
            destination[i] = this[i];
        }
    }
}

public readonly record struct UniversalPacket64 : IUniversalPacket
{
    private readonly UInt64 _data;

    public UniversalPacket64(UInt64 data)
        => _data = data;
    internal UniversalPacket64(UInt32[] data)
        => _data = PacketPacker.ToUInt64(data);

    public PacketKind PacketKind => PacketKind.Packet64;
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);

    public uint this[int index] => PacketPacker.FromIndex64(_data, index);

    public void CopyTo(Span<uint> destination)
    {
        for (int i = 0; i < (int)PacketKind; i++)
        {
            destination[i] = this[i];
        }
    }
}

public readonly record struct UniversalPacket96 : IUniversalPacket
{
    private readonly UInt128 _data;

    public UniversalPacket96(UInt128 data)
        => _data = data & Mask96;

    // lower 96 bits (3 words)
    private static readonly UInt128 Mask96 = new(0x0000_0000_FFFF_FFFF, 0xFFFF_FFFF_FFFF_FFFF);
    internal UniversalPacket96(UInt32[] data)
        => _data = PacketPacker.ToUInt96(data);

    public PacketKind PacketKind => PacketKind.Packet96;
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);

    public uint this[int index] => PacketPacker.FromIndex96(_data, index);

    public void CopyTo(Span<uint> destination)
    {
        for (int i = 0; i < (int)PacketKind; i++)
        {
            destination[i] = this[i];
        }
    }
}

public readonly record struct UniversalPacket128 : IUniversalPacket
{
    private readonly UInt128 _data;

    public UniversalPacket128(UInt128 data)
        => _data = data;
    internal UniversalPacket128(UInt32[] data)
        => _data = PacketPacker.ToUInt128(data);

    public PacketKind PacketKind => PacketKind.Packet128;
    public MessageKind MessageKind => (MessageKind)(this[0] >> 28);

    public uint this[int index] => PacketPacker.FromIndex128(_data, index);

    public void CopyTo(Span<uint> destination)
    {
        for (int i = 0; i < (int)PacketKind; i++)
        {
            destination[i] = this[i];
        }
    }
}

internal static class PacketPacker
{
    public static UInt32 ToUInt32(UInt32[] data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        if (data.Length != 1)
            throw new ArgumentException($"Expected a length of exactly 1 but got {data.Length}", nameof(data));

        return data[0];
    }

    public static UInt64 ToUInt64(UInt32[] data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        if (data.Length != 2)
            throw new ArgumentException($"Expected a length of exactly 2 but got {data.Length}", nameof(data));

        return (UInt64)data[0] | (UInt64)data[1] << 32;
    }

    public static UInt128 ToUInt96(UInt32[] data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        if (data.Length != 3)
            throw new ArgumentException($"Expected a length of exactly 3 but got {data.Length}", nameof(data));

        return (UInt128)data[0] | (UInt128)data[1] << 32 | (UInt128)data[2] << 64;
    }

    public static UInt128 ToUInt128(UInt32[] data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        if (data.Length != 4)
            throw new ArgumentException($"Expected a length of exactly 4 but got {data.Length}", nameof(data));

        return (UInt128)data[0] | (UInt128)data[1] << 32 | (UInt128)data[2] << 64 | (UInt128)data[3] << 96;
    }

    public static UInt32 FromIndex32(UInt32 value, int index)
    {
        return index switch
        {
            0 => value,
            _ => throw new IndexOutOfRangeException($"Expected 0 and got {index}.")
        };
    }

    public static UInt32 FromIndex64(UInt64 value, int index)
    {
        return index switch
        {
            0 => (UInt32)value,
            1 => (UInt32)(value >> 32),
            _ => throw new IndexOutOfRangeException($"Expected 0-1 and got {index}.")
        };
    }

    public static UInt32 FromIndex96(UInt128 value, int index)
    {
        return index switch
        {
            0 => (UInt32)value,
            1 => (UInt32)(value >> 32),
            2 => (UInt32)(value >> 64),
            _ => throw new IndexOutOfRangeException($"Expected 0-2 and got {index}.")
        };
    }

    public static UInt32 FromIndex128(UInt128 value, int index)
    {
        return index switch
        {
            0 => (UInt32)value,
            1 => (UInt32)(value >> 32),
            2 => (UInt32)(value >> 64),
            3 => (UInt32)(value >> 96),
            _ => throw new IndexOutOfRangeException($"Expected 0-3 and got {index}.")
        };
    }
}