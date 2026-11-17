using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Device.Processors;

public sealed class ProcessorFactory : IProcessorFactory
{
    private readonly static Dictionary<string, IProcessor> ProcessorMap = new()
    {
        // Layout Processors
        {  "sysex", new SysExProcessor() },
        {  "xorchecksum", new XorChecksumProcessor() },

        // Field Processors
        {  "pack7", new Pack7Processor() },
    };

    public string Namespace => "midi";

    public IEnumerable<string> Processors
        => ProcessorMap.Keys;

    public IProcessor? CreateProcessor(string id)
        => ProcessorMap.GetValueOrDefault(id);

    public IEnumerable<DataTypeDescriptor> DataTypes => [];
}
