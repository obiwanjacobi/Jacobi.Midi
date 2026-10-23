using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// Looks up what the processors of the opened assemblies publish: the <see cref="PropertyDescriptor"/>s of a processor
/// (<see cref="IProcessor.Properties"/>), its pipeline stage and the data types of their properties (<see cref="IProcessorFactory.DataTypes"/>).
/// Used by the node editor to offer processors and check processor properties. Processors are created once per key and cached.
/// </summary>
/// <param name="processors">The processor factories of the opened assemblies.</param>
internal sealed class ProcessorCatalog(ProcessorManager processors)
{
    private readonly Dictionary<string, IProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);
    private DataTypeRegistry? _dataTypes;
    private int _dataTypesNamespaceCount;

    /// <summary>
    /// The published properties of the processor with <paramref name="processorKey"/> ('namespace.id');
    /// null when the processor is not known (its namespace is not loaded or the factory cannot create it).
    /// Empty when the processor publishes no properties: its properties are then not described and cannot be checked.
    /// </summary>
    public IReadOnlyList<PropertyDescriptor>? GetProperties(string? processorKey)
        => GetProcessor(processorKey)?.Properties;

    /// <summary>
    /// True when <paramref name="processorKey"/> ('namespace.id') is a processor of the loaded processor factories.
    /// A processor name that is not, designates a processor def of a schema (e.g. 'midi.sysexbyte').
    /// </summary>
    public bool IsProcessorKey(string? processorKey) => GetProcessor(processorKey) is not null;

    /// <summary>
    /// The pipeline stage of the processor with <paramref name="processorKey"/>; null when the processor is not known.
    /// </summary>
    public PipelineStage? GetStage(string? processorKey)
        => GetProcessor(processorKey)?.Stage;

    /// <summary>
    /// The keys ('namespace.id') of all processors of the loaded processor factories, ordered.
    /// </summary>
    public IReadOnlyList<string> GetProcessorKeys()
    {
        var keys = new List<string>();
        foreach (var @namespace in processors.Namespaces)
        {
            try
            {
                keys.AddRange(processors.GetFactory(@namespace).Processors.Select(id => new ProcessorKey(@namespace, id).ToString()));
            }
            catch (Exception exception) when (exception is NotImplementedException or NotSupportedException or InvalidOperationException)
            {
                // a factory that cannot list its processors offers none
            }
        }
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The processors to choose from in the processor drop-down list, in two sections: the processors of the loaded
    /// processor factories and the names of the processor defs of <paramref name="document"/>.
    /// With a <paramref name="stage"/>, only the processors of that stage (and the defs that reference one) are offered.
    /// </summary>
    public IReadOnlyList<ProcessorOption> GetProcessorOptions(EditDocument? document, PipelineStage? stage = null)
    {
        bool Matches(string key) => stage is null || GetStage(key) == stage;

        var options = new List<ProcessorOption> { new("Global processors", IsHeader: true) };
        options.AddRange(GetProcessorKeys().Where(Matches).Select(key => new ProcessorOption(key)));
        var processorDefs = (document?.ProcessorDefs ?? []).Where(def => Matches(def.Processor)).ToList();
        if (processorDefs.Count > 0)
        {
            options.Add(new("Processor defs", IsHeader: true));
            options.AddRange(processorDefs
                .Select(def => def.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => new ProcessorOption(name)));
        }
        return options;
    }

    private IProcessor? GetProcessor(string? processorKey)
    {
        if (String.IsNullOrWhiteSpace(processorKey) || !processorKey.Contains(ProcessorKey.Separator))
        {
            return null;
        }
        processorKey = processorKey.Trim();
        if (_processors.TryGetValue(processorKey, out var cached))
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
            return _processors[processorKey] = processor;
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
