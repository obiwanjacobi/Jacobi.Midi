namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// A contiguous range of linear addresses (one address is one encoded data byte).
/// </summary>
public readonly record struct AddressRange(long Start, long Size)
{
    /// <summary>The first address after the range.</summary>
    public long End => Start + Size;

    public bool IsEmpty => Size <= 0;

    public bool Contains(long address)
        => address >= Start && address < End;

    public bool Contains(AddressRange other)
        => other.Start >= Start && other.End <= End;

    public bool Overlaps(AddressRange other)
        => other.Start < End && Start < other.End;

    /// <summary>The addresses both ranges have in common; empty when they do not overlap.</summary>
    public AddressRange Intersect(AddressRange other)
    {
        var start = Math.Max(Start, other.Start);
        var end = Math.Min(End, other.End);
        return end > start ? new AddressRange(start, end - start) : new AddressRange(start, 0);
    }

    /// <summary>The smallest range that contains both ranges.</summary>
    public AddressRange Union(AddressRange other)
    {
        var start = Math.Min(Start, other.Start);
        return new AddressRange(start, Math.Max(End, other.End) - start);
    }

    public override string ToString() => $"[0x{Start:X}, +0x{Size:X})";
}
