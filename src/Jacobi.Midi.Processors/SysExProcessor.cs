using System.Buffers;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Processors;

public sealed class SysExProcessor : ILayoutProcessor,
    ILayoutReader<ReadOnlyMemory<byte>>, ILayoutWriter<ReadOnlySpan<byte>>
{
    public void BeginWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
    {
        writer.GetSpan(1)[0] = 0xF0; // Start of SysEx
        writer.Advance(1);
    }

    public WriteResult Write(IBufferWriter<byte> writer, EncodedField encodedValue, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.Write(writer, encodedValue, context);

    public void EndWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
    {
        writer.GetSpan(1)[0] = 0xF7; // End of SysEx
        writer.Advance(1);
    }

    public void BeginRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        if (!reader.TryRead(out byte startByte))
        {
            throw new InvalidOperationException("Unable to read start byte of SysEx message.");
        }
        if (startByte != 0xF0)
        {
            throw new InvalidOperationException($"Invalid start byte for SysEx message: {startByte:X2}");
        }
    }

    public LayoutReadResult<EncodedField> Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.Read(ref reader, context);

    public void EndRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        if (!reader.TryRead(out byte endByte))
        {
            throw new InvalidOperationException("Unable to read end byte of SysEx message.");
        }
        if (endByte != 0xF7)
        {
            throw new InvalidOperationException($"Invalid end byte for SysEx message: {endByte:X2}");
        }
    }

    public ProcessorKey Key => new("midi.sysex");
    public string Name => "System Exclusive Stream Processor";
    public PipelineStage Stage => PipelineStage.Layout;
    public IReadOnlyList<PropertyDescriptor> Properties { get; } = [];

    LayoutReadResult<ReadOnlyMemory<byte>> ILayoutReader<ReadOnlyMemory<byte>>.Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
        => LayoutChain.PassThroughRead(ref reader);

    public WriteResult Write(IBufferWriter<byte> writer, ReadOnlySpan<byte> encodedValue, LayoutProcessorContext context)
        => LayoutChain.PassThroughWrite(writer, encodedValue);
}
