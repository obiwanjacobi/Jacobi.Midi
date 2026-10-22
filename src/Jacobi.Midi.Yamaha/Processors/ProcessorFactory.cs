using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Yamaha.Processors;

/// <summary>
/// Provides the Yamaha-specific processors under the <c>yamaha</c> namespace (e.g. <c>yamaha.checksum</c>).
/// </summary>
public sealed class ProcessorFactory : IProcessorFactory
{
    private readonly static Dictionary<string, IProcessor> ProcessorMap = new()
    {
        // Layout Processors
        {  "checksum", new ChecksumProcessor() },
    };

    public IEnumerable<string> Processors
        => ProcessorMap.Keys;

    public string Namespace => "yamaha";

    public IProcessor? CreateProcessor(string id)
        => ProcessorMap.GetValueOrDefault(id);

    public IEnumerable<DataTypeDescriptor> DataTypes => [];
}
