using System.Buffers;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Yamaha.Processors;

/// <summary>
/// The Yamaha bulk dump checksum: the value that makes the lower 7 bits of the sum of all bytes of the group
/// plus the checksum itself zero. Appended after the group on write and validated on read.
/// </summary>
/// <remarks>
/// The group decides what is summed. That differs per format: the G50 sums the bytes after the byte count
/// (header and data), XG models also sum the byte count and address.
/// </remarks>
public sealed class ChecksumProcessor : ILayoutProcessor,
    ILayoutReader<ReadOnlyMemory<byte>>, ILayoutWriter<ReadOnlySpan<byte>>
{
    public void BeginWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.BeginWrite(writer, context);

    public WriteResult Write(IBufferWriter<byte> writer, EncodedField encodedValue, LayoutProcessorContext context)
    {
        AddToSum(encodedValue.Value, context);
        return DefaultProcessors.LayoutProcessor.Write(writer, encodedValue, context);
    }

    public void EndWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
    {
        var state = context.GetOrCreateState<ChecksumState>();
        var checksum = state.CalcChecksum();
        writer.GetSpan(1)[0] = checksum;
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

    public LayoutReadResult EndRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        var result = DefaultProcessors.LayoutProcessor.EndRead(ref reader, context);
        if (result.Status != ReadResult.Success)
        {
            return result;
        }

        var length = context.GroupData.Position;
        var data = reader.Sequence.Slice(reader.Consumed - length, length);
        var state = new ChecksumState();
        foreach (var segment in data)
        {
            state.AddToSum(segment.Span);
        }
        var checksum = state.CalcChecksum();

        if (!reader.TryRead(out var readChecksum))
        {
            return LayoutReadResult.NeedMoreData(1);
        }
        if (readChecksum != checksum)
        {
            throw new InvalidOperationException($"Checksum mismatch: expected {checksum:X2}, but read {readChecksum:X2}.");
        }

        return LayoutReadResult.Success();
    }

    public ProcessorKey Key => new("yamaha.checksum");
    public string Name => "Yamaha Checksum Processor";
    public PipelineStage Stage => PipelineStage.Layout;
    public IReadOnlyList<PropertyDescriptor> Properties { get; } = [];

    LayoutReadResult<ReadOnlyMemory<byte>> ILayoutReader<ReadOnlyMemory<byte>>.Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
        => LayoutChain.PassThroughRead(ref reader);

    public WriteResult Write(IBufferWriter<byte> writer, ReadOnlySpan<byte> encodedValue, LayoutProcessorContext context)
    {
        var state = context.GetOrCreateState<ChecksumState>();
        state.AddToSum(encodedValue);
        return LayoutChain.PassThroughWrite(writer, encodedValue);
    }

    private static void AddToSum(object? value, LayoutProcessorContext context)
    {
        var state = context.GetOrCreateState<ChecksumState>();

        switch (value)
        {
            case null:
                break;
            case byte[] bytes:
                state.AddToSum(bytes.AsSpan());
                break;
            case ReadOnlyMemory<byte> memory:
                state.AddToSum(memory.Span);
                break;
            case Memory<byte> memory:
                state.AddToSum(memory.Span);
                break;
            default:
                throw new InvalidOperationException($"Cannot add value of type {value.GetType()} to the Yamaha checksum.");
        }
    }

    /// <summary>
    /// The running sum of the checksummed bytes of one group.
    /// </summary>
    private sealed class ChecksumState
    {
        private int _sum = 0;

        public void AddToSum(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                _sum += b;
            }
        }

        public byte CalcChecksum()
            => (byte)((0x80 - (_sum % 0x80)) % 0x80);
    }
}
