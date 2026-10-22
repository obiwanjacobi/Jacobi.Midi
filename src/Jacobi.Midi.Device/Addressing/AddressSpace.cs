using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// How an address is encoded on the wire: <see cref="ByteCount"/> bytes of <see cref="BitsPerByte"/> bits each
/// (GP-8: 2 x 7, most Roland devices: 4 x 7, Yamaha: 3 x 7). Converts between the wire form and linear addresses.
/// Taken from the envelope's <c>addressStart</c> field.
/// </summary>
public readonly record struct AddressSpace(int ByteCount, int BitsPerByte = 7)
{
    /// <summary>The number of addresses in the space.</summary>
    public long Capacity => 1L << (ByteCount * BitsPerByte);

    /// <summary>Converts the address bytes (most significant first) to a linear address.</summary>
    public long ToLinear(ReadOnlySpan<byte> wire)
    {
        long address = 0;
        foreach (var b in wire)
        {
            if (b >= 1 << BitsPerByte)
            {
                throw new ArgumentException($"The address byte 0x{b:X2} does not fit in {BitsPerByte} bits.", nameof(wire));
            }
            address = (address << BitsPerByte) | b;
        }
        return address;
    }

    /// <summary>Converts a linear address to <see cref="ByteCount"/> address bytes (most significant first).</summary>
    public void ToWire(long address, Span<byte> wire)
    {
        if (address < 0 || address >= Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(address), $"The address 0x{address:X} is outside the address space.");
        }
        if (wire.Length < ByteCount)
        {
            throw new ArgumentException($"The address needs {ByteCount} bytes.", nameof(wire));
        }

        var mask = (1L << BitsPerByte) - 1;
        for (var i = ByteCount - 1; i >= 0; i--)
        {
            wire[i] = (byte)(address & mask);
            address >>= BitsPerByte;
        }
    }

    /// <summary>
    /// Parses the schema notation into a linear address: removes '-' and '_', reads hex ("0x" prefix or "H" postfix)
    /// or decimal, and converts the wire form (one byte per address byte) to linear ("0x40-00" → 0x2000).
    /// </summary>
    public long Parse(string text)
    {
        if (!TryParse(text, BitsPerByte, out var address, out var error))
        {
            throw new FormatException(error);
        }
        if (address >= Capacity)
        {
            throw new FormatException($"The address '{text}' is outside the address space of {ByteCount} x {BitsPerByte} bits.");
        }
        return address;
    }

    /// <summary>Formats a linear address in wire form, as in the manual ("0x40-00").</summary>
    public string Format(long address)
    {
        Span<byte> wire = stackalloc byte[ByteCount];
        ToWire(address, wire);
        return "0x" + String.Join('-', wire.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Parses the schema notation (see <see cref="Parse"/>) with the given number of bits per address byte.
    /// </summary>
    public static bool TryParse(string? text, int bitsPerByte, out long address, [NotNullWhen(false)] out string? error)
    {
        address = 0;
        if (!TryParseNumber(text, out var wire))
        {
            error = $"'{text}' is not a number (hex with a '0x' prefix or 'H' postfix, otherwise decimal).";
            return false;
        }

        var limit = 1UL << bitsPerByte;
        var shift = 0;
        while (wire != 0)
        {
            var b = wire & 0xFF;
            if (b >= limit)
            {
                error = $"'{text}': the address byte 0x{b:X2} does not fit in {bitsPerByte} bits.";
                return false;
            }
            address |= (long)b << shift;
            shift += bitsPerByte;
            wire >>= 8;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Parses a number in the schema notation without the wire conversion: '-' and '_' are removed,
    /// a '0x' prefix or 'H' postfix means hex, otherwise decimal.
    /// </summary>
    public static bool TryParseNumber(string? text, out ulong value)
    {
        value = 0;
        if (String.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = text.Trim().Replace("-", String.Empty).Replace("_", String.Empty);
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return UInt64.TryParse(digits.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        if (digits.EndsWith('H') || digits.EndsWith('h'))
        {
            return UInt64.TryParse(digits.AsSpan(0, digits.Length - 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        return UInt64.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
