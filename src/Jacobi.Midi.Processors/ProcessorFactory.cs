using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Processors;

public sealed class ProcessorFactory : IProcessorFactory
{
    private readonly static Dictionary<string, IProcessor> ProcessorMap = new()
    {
        // Layout Processors
        {  "midi.sysex", new SysExProcessor() },
    };

    public string Namespace => "midi";

    public IEnumerable<string> Processors
    => ProcessorMap.Keys.Select(p => p[5..]);

    public IProcessor? CreateProcessor(string id)
        => ProcessorMap.GetValueOrDefault($"{Namespace}.{id}");

    public IEnumerable<DataTypeDescriptor> DataTypes => [];
}
