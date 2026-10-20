using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Roland.Processors;

public sealed class ProcessorFactory : IProcessorFactory
{
    private readonly static Dictionary<string, IProcessor> ProcessorMap = new()
    {
        // Layout Processors
        {  "roland.checksum", new ChecksumProcessor() },
    };

    public IEnumerable<string> Processors
        => ProcessorMap.Keys.Select(p => p[7..]);

    public string Namespace => "roland";

    public IProcessor? CreateProcessor(string id)
        => ProcessorMap.GetValueOrDefault($"{Namespace}.{id}");

    public IEnumerable<DataTypeDescriptor> DataTypes => [];
}
