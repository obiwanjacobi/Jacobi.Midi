using Jacobi.BinarySerializer.Codecs;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Device.Processors;

/// <summary>
/// The 7-in-8 packing of 8-bit data in SysEx (<c>midi.pack7</c>, a field processor of a <c>sys.bytes</c> field): every
/// 7 data bytes are sent as 8 bytes of 7 bits, first a byte with the high bits (bit 7) of the 7 bytes, then those 7
/// bytes without their high bit. The last group may be shorter: n data bytes (1-6) are sent as n + 1 bytes.
/// The field's <c>byteLength</c> is the length of the packed bytes (on the wire); the value is the unpacked data.
/// Property <c>bitorder</c>: where the high bit of the first byte of a group is in the high-bit byte, <c>little</c> (bit 0,
/// default) or <c>big</c> (bit 6). Used by the universal File Dump and by Korg dumps.
/// </summary>
internal sealed class Pack7Processor : IFieldProcessor
{
    private static readonly PropertyDescriptor BitOrderProperty = new("bitorder", "sys.endianness", false,
        description: "Where the high bit of the first byte of a group is: 'little' (bit 0 of the high-bit byte, default) or 'big' (bit 6).");

    public ProcessorKey Key => new("midi.pack7");
    public string Name => "MIDI 7-in-8 Packing Processor";
    public PipelineStage Stage => PipelineStage.Representation;
    public IReadOnlyList<PropertyDescriptor> Properties => [BitOrderProperty];

    public FieldWriteResult<EncodedField> Write(LogicalField field, FieldProcessorContext context)
    {
        var data = field.Value switch
        {
            null => [],
            byte[] bytes => bytes,
            ReadOnlyMemory<byte> memory => memory.ToArray(),
            _ => throw context.Fail($"'{context.Field.Path}': the 7-in-8 packing expects bytes, not {field.Value.GetType()}."),
        };
        var packed = Pack(data, IsBig(context));
        if (context.FieldData.ByteLength is { } length && length != packed.Length)
        {
            throw context.Fail($"'{context.Field.Path}': {data.Length} data bytes pack into {packed.Length} bytes, not the {length} of the field.");
        }
        return FieldWriteResult<EncodedField>.Written(new(field.Name, typeof(byte[]), packed, packed.Length * 8), packed.Length * 8);
    }

    public FieldReadResult<LogicalField> Read(EncodedField field, FieldProcessorContext context)
    {
        if (field.Value is not byte[] bytes)
        {
            throw context.Fail($"'{context.Field.Path}': the 7-in-8 packing expects bytes.");
        }
        if (context.FieldData.ByteLength is not { } length)
        {
            throw context.Fail($"'{context.Field.Path}': the 7-in-8 packing needs the byte length of the field (the packed bytes).");
        }
        if (bytes.Length < length)
        {
            return FieldReadResult<LogicalField>.NeedMoreData();
        }
        var packed = bytes.AsSpan(0, length);
        if (length % 8 == 1)
        {
            throw context.Fail($"'{context.Field.Path}': {length} packed bytes: the last group has a high-bit byte without data.");
        }
        foreach (var b in packed)
        {
            if (b > 0x7F)
            {
                throw context.Fail($"'{context.Field.Path}': a packed byte has bit 7 set ({b:X2}).");
            }
        }
        byte[] data;
        try
        {
            data = Unpack(packed, IsBig(context));
        }
        catch (FormatException exception)
        {
            throw context.Fail($"'{context.Field.Path}': {exception.Message}", inner: exception);
        }
        return FieldReadResult<LogicalField>.Consumed(new(field.Name, typeof(byte[]), data), length * 8);
    }

    /// <summary>Packs <paramref name="data"/>: per group of up to 7 bytes, the high-bit byte and the 7-bit parts.</summary>
    internal static byte[] Pack(ReadOnlySpan<byte> data, bool big)
    {
        var groups = (data.Length + 6) / 7;
        var packed = new byte[data.Length + groups];
        var output = 0;
        for (var start = 0; start < data.Length; start += 7)
        {
            var count = Math.Min(7, data.Length - start);
            var highBits = output++;
            for (var i = 0; i < count; i++)
            {
                var b = data[start + i];
                if ((b & 0x80) != 0)
                {
                    packed[highBits] |= (byte)(1 << (big ? 6 - i : i));
                }
                packed[output++] = (byte)(b & 0x7F);
            }
        }
        return packed;
    }

    /// <summary>Unpacks <paramref name="packed"/> (see <see cref="Pack"/>); a last group of one byte has no data.</summary>
    internal static byte[] Unpack(ReadOnlySpan<byte> packed, bool big)
    {
        var groups = (packed.Length + 7) / 8;
        var data = new byte[packed.Length - groups];
        var output = 0;
        for (var start = 0; start < packed.Length; start += 8)
        {
            var highBits = packed[start];
            var count = Math.Min(7, packed.Length - start - 1);
            if ((highBits & ~UsedBits(count, big)) != 0)
            {
                throw new FormatException($"The high-bit byte {highBits:X2} at {start} has a bit set for a byte the group does not have ({count} bytes).");
            }
            for (var i = 0; i < count; i++)
            {
                var high = (highBits >> (big ? 6 - i : i)) & 1;
                data[output++] = (byte)(packed[start + 1 + i] | (high << 7));
            }
        }
        return data;
    }

    // the bits of the high-bit byte that belong to the 'count' bytes of a group
    private static int UsedBits(int count, bool big)
        => big ? (0x7F << (7 - count)) & 0x7F : (1 << count) - 1;

    private static bool IsBig(FieldProcessorContext context)
        => context.Properties.GetOrDefault(BitOrderProperty, Endianness.Little) == Endianness.Big;
}
