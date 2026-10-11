using System.Buffers;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Roland.Processors;

public sealed class ChecksumProcessor : ILayoutProcessor
{
    public void BeginWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.BeginWrite(writer, context);

    public WriteResult Write(IBufferWriter<byte> writer, EncodedField encodedValue, LayoutProcessorContext context)
    {
        return DefaultProcessors.LayoutProcessor.Write(writer, encodedValue, context);
    }

    public void EndWrite(IBufferWriter<byte> writer, LayoutProcessorContext context)
    {
        DefaultProcessors.LayoutProcessor.EndWrite(writer, context);
    }

    public void BeginRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
        => DefaultProcessors.LayoutProcessor.BeginRead(ref reader, context);

    public LayoutReadResult<EncodedField> Read(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        return DefaultProcessors.LayoutProcessor.Read(ref reader, context);
    }

    public void EndRead(ref SequenceReader<byte> reader, LayoutProcessorContext context)
    {
        DefaultProcessors.LayoutProcessor.EndRead(ref reader, context);
    }

    public ProcessorKey Key => new("roland.checksum");
    public string Name => "Roland Checksum Processor";
    public PipelineStage Stage => PipelineStage.Layout;
    public IReadOnlyList<PropertyDescriptor> Properties { get; } = [];
}
