using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// A property as it applies to a node after merging its definitions.
/// </summary>
/// <param name="Name">The property name.</param>
/// <param name="Value">The property value.</param>
/// <param name="Source">Where the property comes from: <see cref="EffectiveNodeResolver.OwnSource"/> or the definition (e.g. 'nodeDef X').</param>
/// <param name="Own">The authored property when it is the node's (or processor ref's) own; null when inherited.</param>
internal sealed record ResolvedProperty(string Name, string? Value, string Source, EditProperty? Own);

/// <summary>
/// A processor as it applies to a node after merging its definitions, with its effective properties.
/// </summary>
/// <param name="Processor">The authored processor ref (of the node itself or of one of its definitions).</param>
/// <param name="Source">Where the processor comes from: <see cref="EffectiveNodeResolver.OwnSource"/> or the definition.</param>
/// <param name="IsOwn">True when the processor ref is the node's own.</param>
/// <param name="Properties">The processor def properties overridden by the properties of the ref.</param>
internal sealed record ResolvedProcessor(EditProcessorRef Processor, string Source, bool IsOwn, IReadOnlyList<ResolvedProperty> Properties);

/// <summary>
/// The effective properties and processors of a node or another document item.
/// </summary>
/// <param name="Properties">The effective properties.</param>
/// <param name="Processors">The effective processors.</param>
/// <param name="Unresolved">Descriptions of the definitions that could not be resolved within the document.</param>
internal sealed record EffectiveItem(
    IReadOnlyList<ResolvedProperty> Properties,
    IReadOnlyList<ResolvedProcessor> Processors,
    IReadOnlyList<string> Unresolved);

/// <summary>
/// Computes the effective properties and processors of an <see cref="EditNode"/> the way the
/// schema compiler (SchemaSetBuilder) merges them, but only with the definitions found in the edited document.
/// Precedence (lowest first): the data type def chain (fields), the node def chain, the node itself.
/// Definitions in other (included) documents are reported in <see cref="EffectiveItem.Unresolved"/>.
/// A processor name 'namespace.name' that is not a processorDef of the document is told apart from a processor key
/// by the optional isProcessorKey predicate (the processors of the loaded assemblies, see ProcessorCatalog).
/// </summary>
internal static class EffectiveNodeResolver
{
    public const string OwnSource = "own";

    public static EffectiveItem Resolve(EditDocument document, EditNode node, Func<string, bool>? isProcessorKey = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        var unresolved = new List<string>();
        var properties = new List<ResolvedProperty>();
        var processors = new List<ResolvedProcessor>();
        var nodeDefs = GetNodeDefChain(document, node, unresolved);

        if (node is EditField field)
        {
            var dataType = field.DataType ?? nodeDefs.OfType<EditField>().Select(def => def.DataType).FirstOrDefault(type => type is not null);
            foreach (var dataTypeDef in GetDataTypeDefChain(document, dataType, unresolved).AsEnumerable().Reverse())
            {
                var source = $"dataType {dataTypeDef.Name}";
                MergeProperties(properties, dataTypeDef.Properties, source, own: false);
                MergeProcessors(document, isProcessorKey, processors, dataTypeDef.Processors, source, own: false, unresolved);
            }
        }

        // the node def chain is nearest first; merge the farthest first
        foreach (var nodeDef in nodeDefs.AsEnumerable().Reverse())
        {
            var source = $"nodeDef {nodeDef.Name}";
            MergeProperties(properties, nodeDef.Properties, source, own: false);
            MergeProcessors(document, isProcessorKey, processors, GetProcessors(nodeDef), source, own: false, unresolved);
        }

        MergeProperties(properties, node.Properties, OwnSource, own: true);
        MergeProcessors(document, isProcessorKey, processors, GetProcessors(node), OwnSource, own: true, unresolved);

        return new(properties, processors, unresolved.Distinct().ToList());
    }

    /// <summary>
    /// Resolves only the given (own) entries, as for a data type def or processor def.
    /// Processor refs are still merged with their processor defs.
    /// </summary>
    public static EffectiveItem ResolveOwn(EditDocument document, IEnumerable<EditProperty> properties, IEnumerable<EditProcessorRef>? processors,
        Func<string, bool>? isProcessorKey = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var unresolved = new List<string>();
        var resolvedProperties = new List<ResolvedProperty>();
        var resolvedProcessors = new List<ResolvedProcessor>();
        MergeProperties(resolvedProperties, properties, OwnSource, own: true);
        MergeProcessors(document, isProcessorKey, resolvedProcessors, processors, OwnSource, own: true, unresolved);
        return new(resolvedProperties, resolvedProcessors, unresolved.Distinct().ToList());
    }

    /// <summary>
    /// Resolves the effective properties of a processor ref: its processor def properties overridden by its own.
    /// </summary>
    public static EffectiveItem ResolveProcessorRef(EditDocument document, EditProcessorRef processor, Func<string, bool>? isProcessorKey = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(processor);
        var unresolved = new List<string>();
        var properties = ResolveProcessorProperties(document, isProcessorKey, processor, OwnSource, own: true, unresolved);
        return new(properties, [], unresolved.Distinct().ToList());
    }

    /// <summary>
    /// Resolves the effective value processors of a repeat (count) or choice (selected index): those of the node def chain
    /// overridden by the node's own, the way the schema compiler merges them. Properties are not part of the result.
    /// </summary>
    public static EffectiveItem ResolveValueProcessors(EditDocument document, EditNode node, Func<string, bool>? isProcessorKey = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);
        var unresolved = new List<string>();
        var processors = new List<ResolvedProcessor>();
        // the node def chain is nearest first; merge the farthest first
        foreach (var nodeDef in GetNodeDefChain(document, node, unresolved).AsEnumerable().Reverse())
        {
            MergeProcessors(document, isProcessorKey, processors, GetValueProcessors(nodeDef), $"nodeDef {nodeDef.Name}", own: false, unresolved);
        }
        MergeProcessors(document, isProcessorKey, processors, GetValueProcessors(node), OwnSource, own: true, unresolved);
        return new([], processors, unresolved.Distinct().ToList());
    }

    /// <summary>
    /// The (authored) value processors of a repeat (count) or choice (selected index); null for other nodes.
    /// </summary>
    public static IList<EditProcessorRef>? GetValueProcessors(EditNode node) => node switch
    {
        EditRepeat repeat => repeat.ValueProcessors,
        EditChoice choice => choice.ValueProcessors,
        _ => null
    };

    /// <summary>
    /// The (authored) processors of a node, or null for a node without processors.
    /// </summary>
    public static IList<EditProcessorRef>? GetProcessors(EditNode node) => node switch
    {
        EditField field => field.Processors,
        EditGroup group => group.Processors,
        _ => null
    };

    private static List<EditNode> GetNodeDefChain(EditDocument document, EditNode node, List<string> unresolved)
    {
        var chain = new List<EditNode>();
        for (var current = node; current.NodeDef is { } name;)
        {
            if (!IsLocal(document, name))
            {
                unresolved.Add($"nodeDef '{name}' is defined in another schema.");
                break;
            }
            var nodeDef = document.NodeDefs.FirstOrDefault(def => String.Equals(def.Name, name.Name, StringComparison.OrdinalIgnoreCase));
            if (nodeDef is null)
            {
                unresolved.Add($"nodeDef '{name}' was not found.");
                break;
            }
            if (ReferenceEquals(nodeDef, node) || chain.Contains(nodeDef))
            {
                break;
            }
            chain.Add(nodeDef);
            current = nodeDef;
        }
        return chain;
    }

    /// <summary>
    /// The data type def of <paramref name="dataType"/> followed by the defs it is based on.
    /// A data type without a def (a built-in type) has an empty chain.
    /// </summary>
    private static List<EditDataTypeDef> GetDataTypeDefChain(EditDocument document, SchemaName? dataType, List<string> unresolved)
    {
        var chain = new List<EditDataTypeDef>();
        for (var name = dataType; name is { } current;)
        {
            if (!IsLocal(document, current))
            {
                // built-in types (sys.*) have no def; only report types of included documents
                if (document.Includes.Any(include => String.Equals(include.Schema, current.Namespace, StringComparison.OrdinalIgnoreCase)))
                {
                    unresolved.Add($"dataType '{current}' is defined in the included schema '{current.Namespace}'.");
                }
                break;
            }
            var dataTypeDef = document.DataTypeDefs.FirstOrDefault(def => String.Equals(def.Name, current.Name, StringComparison.OrdinalIgnoreCase));
            if (dataTypeDef is null || chain.Contains(dataTypeDef))
            {
                break;
            }
            chain.Add(dataTypeDef);
            name = dataTypeDef.BasedOn;
        }
        return chain;
    }

    private static void MergeProperties(List<ResolvedProperty> target, IEnumerable<EditProperty> source, string sourceName, bool own)
    {
        foreach (var property in source)
        {
            target.RemoveAll(existing => existing.Name == property.Name);
            target.Add(new(property.Name, property.Value, sourceName, own ? property : null));
        }
    }

    private static void MergeProcessors(EditDocument document, Func<string, bool>? isProcessorKey, List<ResolvedProcessor> target, IEnumerable<EditProcessorRef>? source,
        string sourceName, bool own, List<string> unresolved)
    {
        foreach (var processor in source ?? [])
        {
            target.RemoveAll(existing => existing.Processor.Processor == processor.Processor);
            target.Add(new(processor, sourceName, own, ResolveProcessorProperties(document, isProcessorKey, processor, sourceName, own, unresolved)));
        }
    }

    private static List<ResolvedProperty> ResolveProcessorProperties(EditDocument document, Func<string, bool>? isProcessorKey, EditProcessorRef processor,
        string sourceName, bool own, List<string> unresolved)
    {
        var properties = new List<ResolvedProperty>();
        var name = processor.Processor;
        if (FindProcessorDef(document, name) is { } processorDef)
        {
            MergeProperties(properties, processorDef.Properties, $"processorDef {processorDef.Name}", own: false);
        }
        else if (IsLocal(document, name))
        {
            unresolved.Add($"processorDef '{name}' was not found.");
        }
        // a processor key ('midi.sysex') is a processor of a loaded assembly; any other 'namespace.name' ('midi.sysexbyte')
        // is a processorDef of another schema. Without isProcessorKey they cannot be told apart and nothing is reported.
        else if (isProcessorKey is not null && !isProcessorKey(name.FullName))
        {
            unresolved.Add(IsIncluded(document, name.Namespace)
                ? $"processorDef '{name}' is defined in the included schema '{name.Namespace}'."
                : $"processor '{name}' is not a known processor, nor a processorDef of an included schema.");
        }
        MergeProperties(properties, processor.Properties, sourceName, own);
        return properties;
    }

    /// <summary>
    /// The processor def in <paramref name="document"/> that a processor name designates ('name' or 'document.name');
    /// null when the name designates a processor def of another schema or a processor key ('namespace.id').
    /// </summary>
    public static EditProcessorDef? FindProcessorDef(EditDocument document, SchemaName name)
        => IsLocal(document, name)
            ? document.ProcessorDefs.FirstOrDefault(def => String.Equals(def.Name, name.Name, StringComparison.OrdinalIgnoreCase))
            : null;

    private static bool IsIncluded(EditDocument document, string? schema)
        => !String.IsNullOrEmpty(schema)
            && document.Includes.Any(include => String.Equals(include.Schema, schema, StringComparison.OrdinalIgnoreCase));

    private static bool IsLocal(EditDocument document, SchemaName name)
        => String.IsNullOrEmpty(name.Namespace) || String.Equals(name.Namespace, document.Name, StringComparison.OrdinalIgnoreCase);
}
