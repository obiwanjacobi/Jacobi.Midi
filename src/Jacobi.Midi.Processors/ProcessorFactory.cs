using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Processors;

public sealed class ProcessorFactory : IProcessorFactory
{
    private readonly static Dictionary<string, IProcessor> Processors = new()
    {
        // Layout Processors
        {  "midi.sysex", new SysExProcessor() },
    };

    public string Namespace => "midi";

    public IProcessor? CreateProcessor(string id)
        => Processors.GetValueOrDefault($"{Namespace}.{id}");

    public IEnumerable<DataTypeDescriptor> DataTypes => [];
}
