using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Roland.Processors;

public sealed class ProcessorFactory : IProcessorFactory
{
    private readonly static Dictionary<string, IProcessor> Processors = new()
    {
        // Layout Processors
        {  "roland.checksum", new ChecksumProcessor() },
    };

    public string Namespace => "roland";

    public IProcessor? CreateProcessor(string id)
        => Processors.GetValueOrDefault($"{Namespace}.{id}");

    public IEnumerable<DataTypeDescriptor> DataTypes => [];
}
