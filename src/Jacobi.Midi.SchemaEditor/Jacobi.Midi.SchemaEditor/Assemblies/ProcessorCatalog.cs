using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// Looks up what the processors of the opened assemblies publish: the <see cref="PropertyDescriptor"/>s of a processor
/// (<see cref="IProcessor.Properties"/>) and the data types of their properties (<see cref="IProcessorFactory.DataTypes"/>).
/// Used by the node editor to check processor properties. Processors are created once per key and cached.
/// </summary>
/// <param name="processors">The processor factories of the opened assemblies.</param>
internal sealed class ProcessorCatalog(ProcessorManager processors)
{
    private readonly Dictionary<string, IReadOnlyList<PropertyDescriptor>> _properties = new(StringComparer.OrdinalIgnoreCase);
    private DataTypeRegistry? _dataTypes;
    private int _dataTypesNamespaceCount;

    /// <summary>
    /// The published properties of the processor with <paramref name="processorKey"/> ('namespace.id');
    /// null when the processor is not known (its namespace is not loaded or the factory cannot create it).
    /// Empty when the processor publishes no properties: its properties are then not described and cannot be checked.
    /// </summary>
    public IReadOnlyList<PropertyDescriptor>? GetProperties(string? processorKey)
    {
        if (String.IsNullOrWhiteSpace(processorKey) || !processorKey.Contains(ProcessorKey.Separator))
        {
            return null;
        }
        processorKey = processorKey.Trim();
        if (_properties.TryGetValue(processorKey, out var cached))
        {
            return cached;
        }

        try
        {
            // unknown processors are not cached: the assembly that provides them may be opened later
            if (!processors.TryCreateProcessor(new ProcessorKey(processorKey), out var processor))
            {
                return null;
            }
            return _properties[processorKey] = processor.Properties;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or NotImplementedException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The built-in data types and the data types published by the processor factories.
    /// Rebuilt when assemblies with processor factories were opened since the last call.
    /// </summary>
    public DataTypeRegistry DataTypes
    {
        get
        {
            if (_dataTypes is null || _dataTypesNamespaceCount != processors.Namespaces.Count)
            {
                var registry = DataTypeRegistry.CreateDefault();
                foreach (var descriptor in processors.DataTypes)
                {
                    if (!registry.TryGet(descriptor.Name, out _))
                    {
                        registry.Register(descriptor);
                    }
                }
                _dataTypes = registry;
                _dataTypesNamespaceCount = processors.Namespaces.Count;
            }
            return _dataTypes;
        }
    }
}
