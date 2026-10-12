using System.Buffers;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Roland.Processors;

public sealed class ChecksumProcessor : ILayoutProcessor,
    ILayoutReader<ReadOnlyMemory<byte>>, ILayoutWriter<ReadOnlySpan<byte>>
{
    private byte _sum = 0;

    public void BeginWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.BeginWrite(writer, context);

    public WriteResult Write(IBufferWriter<byte> writer, EncodedField encodedValue, LayoutProcessorContext context)
    {
        AddToSum(encodedValue.Value);
        return DefaultProcessors.LayoutProcessor.Write(writer, encodedValue, context);
    }

    public void EndWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
    {
        var checksum = CalcChecksum();
        writer.GetSpan(1)[0] = checksum;
        writer.Advance(1);
        DefaultProcessors.LayoutProcessor.EndWrite(writer, context);
    }

    public void BeginRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.BeginRead(ref reader, context);

    public LayoutReadResult<EncodedField> Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        var result = DefaultProcessors.LayoutProcessor.Read(ref reader, context);
        AddToSum(result.Value.Value); // EncodedField.Value
        return result;
    }

    public void EndRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        DefaultProcessors.LayoutProcessor.EndRead(ref reader, context);

        if (reader.TryRead(out var readChecksum))
        {
            var checksum = CalcChecksum();
            if (readChecksum != checksum)
            {
                throw new InvalidOperationException($"Checksum mismatch: expected {checksum:X2}, but read {readChecksum:X2}.");
            }
        }
        else
        {
            throw new InvalidOperationException("Failed to read checksum.");
        }
    }

    public ProcessorKey Key => new("roland.checksum");
    public string Name => "Roland Checksum Processor";
    public PipelineStage Stage => PipelineStage.Layout;
    public IReadOnlyList<PropertyDescriptor> Properties { get; } = [];

    LayoutReadResult<ReadOnlyMemory<byte>> ILayoutReader<ReadOnlyMemory<byte>>.Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        AddToSum(reader.UnreadSpan);
        return LayoutChain.PassThroughRead(ref reader);
    }

    public WriteResult Write(IBufferWriter<byte> writer, ReadOnlySpan<byte> encodedValue, LayoutProcessorContext context)
    {
        AddToSum(encodedValue);
        return LayoutChain.PassThroughWrite(writer, encodedValue);
    }

    public byte CalcChecksum()
    {
        return (byte)(0x80 - (_sum % 0x80));
    }

    private void AddToSum(object? value)
    {
        switch (value)
        {
            case null:
                // skipped: use sys.null processor if you want to include null-as-value in the checksum.
                break;
            case byte[] bytes:
                AddToSum(bytes.AsSpan());
                break;
            case ReadOnlyMemory<byte> memory:
                AddToSum(memory.Span);
                break;
            case Memory<byte> memory:
                AddToSum(memory.Span);
                break;
            default:
                throw new InvalidOperationException($"Cannot add value of type {value.GetType()} to the Roland checksum.");
        }
    }

    private void AddToSum(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            _sum += b;
        }
    }
}
