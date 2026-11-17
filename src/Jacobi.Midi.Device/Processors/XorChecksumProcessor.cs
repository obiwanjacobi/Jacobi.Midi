using System.Buffers;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Device.Processors;

/// <summary>
/// The checksum of the universal System Exclusive dumps (Sample Dump, File Dump, MIDI Tuning Standard): the XOR of the
/// bytes of the group it is declared on, kept to 7 bits. On write it appends the checksum after the group's content; on
/// read it checks the byte after the content (a mismatch fails the read). The checksum is not a field of the schema.
/// Declared on a message root (inside <c>midi.sysex</c>) it covers the whole message between F0 and the checksum.
/// </summary>
internal sealed class XorChecksumProcessor : ILayoutProcessor,
    ILayoutReader<ReadOnlyMemory<byte>>, ILayoutWriter<ReadOnlySpan<byte>>
{
    public ProcessorKey Key => new("midi.xorchecksum");
    public string Name => "MIDI XOR Checksum Processor";
    public PipelineStage Stage => PipelineStage.Layout;
    public IReadOnlyList<PropertyDescriptor> Properties { get; } = [];

    public void BeginWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.BeginWrite(writer, context);

    public WriteResult Write(IBufferWriter<byte> writer, EncodedField encodedValue, LayoutProcessorContext context)
    {
        Add(encodedValue.Value, context);
        return DefaultProcessors.LayoutProcessor.Write(writer, encodedValue, context);
    }

    // as an outer processor of the chain: the bytes of the inner processors
    public WriteResult Write(IBufferWriter<byte> writer, ReadOnlySpan<byte> encodedValue, LayoutProcessorContext context)
    {
        context.GetOrCreateState<ChecksumState>().Add(encodedValue);
        return LayoutChain.PassThroughWrite(writer, encodedValue);
    }

    public void EndWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
    {
        writer.GetSpan(1)[0] = context.GetOrCreateState<ChecksumState>().Checksum;
        writer.Advance(1);
        DefaultProcessors.LayoutProcessor.EndWrite(writer, context);
    }

    public LayoutReadResult BeginRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        context.ReserveTail(1);
        return DefaultProcessors.LayoutProcessor.BeginRead(ref reader, context);
    }

    public LayoutReadResult<EncodedField> Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.Read(ref reader, context);

    LayoutReadResult<ReadOnlyMemory<byte>> ILayoutReader<ReadOnlyMemory<byte>>.Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
        => LayoutChain.PassThroughRead(ref reader);

    public LayoutReadResult EndRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        var result = DefaultProcessors.LayoutProcessor.EndRead(ref reader, context);
        if (result.Status != ReadResult.Success)
        {
            return result;
        }

        // the content of the group: the bytes just read
        var length = context.GroupData.Position;
        var state = new ChecksumState();
        foreach (var segment in reader.Sequence.Slice(reader.Consumed - length, length))
        {
            state.Add(segment.Span);
        }

        if (!reader.TryRead(out var read))
        {
            return LayoutReadResult.NeedMoreData(1);
        }
        if (read != state.Checksum)
        {
            throw new InvalidOperationException($"Checksum mismatch: expected {state.Checksum:X2}, but read {read:X2}.");
        }
        return LayoutReadResult.Success();
    }

    private static void Add(object? value, LayoutProcessorContext context)
    {
        var state = context.GetOrCreateState<ChecksumState>();
        switch (value)
        {
            case null:
                break;
            case byte[] bytes:
                state.Add(bytes);
                break;
            case ReadOnlyMemory<byte> memory:
                state.Add(memory.Span);
                break;
            case Memory<byte> memory:
                state.Add(memory.Span);
                break;
            default:
                throw new InvalidOperationException($"Cannot add a value of type {value.GetType()} to the XOR checksum.");
        }
    }

    /// <summary>The XOR of the bytes of one group occurrence.</summary>
    private sealed class ChecksumState
    {
        private byte _xor;

        public void Add(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                _xor ^= b;
            }
        }

        public byte Checksum => (byte)(_xor & 0x7F);
    }
}
