using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// A property as it applies to a node after merging its definitions.
/// </summary>
/// <param name="Name">The property name.</param>
/// <param name="Value">The property value.</param>
/// <param name="Source">
/// Where the property comes from: <see cref="EffectiveNodeResolver.OwnSource"/> or the definition (e.g. 'nodeDef X';
/// 'nodeDef Schema.X' for a definition of another schema).
/// </param>
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
/// <param name="Unresolved">Descriptions of the definitions that could not be resolved (e.g. their schema is not loaded).</param>
internal sealed record EffectiveItem(
    IReadOnlyList<ResolvedProperty> Properties,
    IReadOnlyList<ResolvedProcessor> Processors,
    IReadOnlyList<string> Unresolved);

/// <summary>
/// Computes the effective properties and processors of an <see cref="EditNode"/> the way the
/// schema compiler (SchemaSetBuilder) merges them.
/// Precedence (lowest first): the data type def chain (fields), the node def chain, the node itself.
/// A definition of another schema ('schema.name') is looked up with the optional findSchema function (the loaded schemas,
/// see <see cref="SchemaSetWorkspace.Find"/>); the names in that definition are resolved in its own schema.
/// Without findSchema only the definitions of the edited document are found; the others are reported in <see cref="EffectiveItem.Unresolved"/>.
/// A processor name 'namespace.name' that is not a processorDef is told apart from a processor key
/// by the optional isProcessorKey predicate (the processors of the loaded assemblies, see ProcessorCatalog).
/// </summary>
internal static class EffectiveNodeResolver
{
    public const string OwnSource = "own";

    /// <param name="document">The edited document that holds the node.</param>
    /// <param name="node">The node to resolve.</param>
    /// <param name="isProcessorKey">Tells a processor key from a processorDef name; null to not report unknown processors.</param>
    /// <param name="findSchema">The loaded schema with a name; null to resolve within the document only.</param>
    public static EffectiveItem Resolve(EditDocument document, EditNode node, Func<string, bool>? isProcessorKey = null,
        Func<string, EditDocument?>? findSchema = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        var scope = new Scope(document, isProcessorKey, findSchema);
        var unresolved = new List<string>();
        var properties = new List<ResolvedProperty>();
        var processors = new List<ResolvedProcessor>();
        var nodeDefs = GetNodeDefChain(scope, node, unresolved);

        if (node is EditField field)
        {
            // the data type of the field, or of the nearest node def that has one (in the schema of that node def)
            SchemaName? dataType = field.DataType;
            var owner = document;
            if (dataType is null && nodeDefs.FirstOrDefault(def => def.Node is EditField { DataType: not null }) is { Node: EditField typed } withType)
            {
                dataType = typed.DataType;
                owner = withType.Owner;
            }
            foreach (var (dataTypeDef, defOwner) in GetDataTypeDefChain(scope, owner, dataType, unresolved).AsEnumerable().Reverse())
            {
                var source = $"dataType {scope.Name(dataTypeDef.Name, defOwner)}";
                MergeProperties(properties, dataTypeDef.Properties, source, own: false);
                MergeProcessors(scope, defOwner, processors, dataTypeDef.Processors, source, own: false, unresolved);
            }
        }

        // the node def chain is nearest first; merge the farthest first
        foreach (var (nodeDef, owner) in nodeDefs.AsEnumerable().Reverse())
        {
            var source = $"nodeDef {scope.Name(nodeDef.Name, owner)}";
            MergeProperties(properties, nodeDef.Properties, source, own: false);
            MergeProcessors(scope, owner, processors, GetProcessors(nodeDef), source, own: false, unresolved);
        }

        MergeProperties(properties, node.Properties, OwnSource, own: true);
        MergeProcessors(scope, document, processors, GetProcessors(node), OwnSource, own: true, unresolved);

        return new(properties, processors, unresolved.Distinct().ToList());
    }

    /// <summary>
    /// Resolves only the given (own) entries, as for a data type def or processor def.
    /// Processor refs are still merged with their processor defs.
    /// </summary>
    public static EffectiveItem ResolveOwn(EditDocument document, IEnumerable<EditProperty> properties, IEnumerable<EditProcessorRef>? processors,
        Func<string, bool>? isProcessorKey = null, Func<string, EditDocument?>? findSchema = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var scope = new Scope(document, isProcessorKey, findSchema);
        var unresolved = new List<string>();
        var resolvedProperties = new List<ResolvedProperty>();
        var resolvedProcessors = new List<ResolvedProcessor>();
        MergeProperties(resolvedProperties, properties, OwnSource, own: true);
        MergeProcessors(scope, document, resolvedProcessors, processors, OwnSource, own: true, unresolved);
        return new(resolvedProperties, resolvedProcessors, unresolved.Distinct().ToList());
    }

    /// <summary>
    /// Resolves the effective properties of a processor ref: its processor def properties overridden by its own.
    /// </summary>
    public static EffectiveItem ResolveProcessorRef(EditDocument document, EditProcessorRef processor, Func<string, bool>? isProcessorKey = null,
        Func<string, EditDocument?>? findSchema = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(processor);
        var scope = new Scope(document, isProcessorKey, findSchema);
        var unresolved = new List<string>();
        var properties = ResolveProcessorProperties(scope, document, processor, OwnSource, own: true, unresolved);
        return new(properties, [], unresolved.Distinct().ToList());
    }

    /// <summary>
    /// Resolves the effective value processors of a repeat (count) or choice (selected index): those of the node def chain
    /// overridden by the node's own, the way the schema compiler merges them. Properties are not part of the result.
    /// </summary>
    public static EffectiveItem ResolveValueProcessors(EditDocument document, EditNode node, Func<string, bool>? isProcessorKey = null,
        Func<string, EditDocument?>? findSchema = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);
        var scope = new Scope(document, isProcessorKey, findSchema);
        var unresolved = new List<string>();
        var processors = new List<ResolvedProcessor>();
        // the node def chain is nearest first; merge the farthest first
        foreach (var (nodeDef, owner) in GetNodeDefChain(scope, node, unresolved).AsEnumerable().Reverse())
        {
            MergeProcessors(scope, owner, processors, GetValueProcessors(nodeDef), $"nodeDef {scope.Name(nodeDef.Name, owner)}", own: false, unresolved);
        }
        MergeProcessors(scope, document, processors, GetValueProcessors(node), OwnSource, own: true, unresolved);
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

    /// <summary>
    /// The processor def that a processor name designates, as seen from <paramref name="document"/>:
    /// 'name' or 'document.name' in the document, 'schema.name' in that loaded schema (with <paramref name="findSchema"/>).
    /// Null when the name designates no processor def (a processor key 'namespace.id', or the schema is not loaded).
    /// </summary>
    public static EditProcessorDef? FindProcessorDef(EditDocument document, SchemaName name, Func<string, EditDocument?>? findSchema = null)
        => FindProcessorDef(new Scope(document, null, findSchema), document, name)?.Definition;

    /// <summary>
    /// The node def chain of <paramref name="node"/>, nearest first, each with the schema that defines it.
    /// </summary>
    private static List<(EditNode Node, EditDocument Owner)> GetNodeDefChain(Scope scope, EditNode node, List<string> unresolved)
    {
        var chain = new List<(EditNode Node, EditDocument Owner)>();
        var owner = scope.Document;
        for (var current = node; current.NodeDef is { } name;)
        {
            if (!IsLocal(owner, name))
            {
                if (scope.FindSchema is null)
                {
                    unresolved.Add($"nodeDef '{name}' is defined in another schema.");
                    break;
                }
                if (scope.FindSchema(name.Namespace!) is not { } other)
                {
                    unresolved.Add($"nodeDef '{name}': the schema '{name.Namespace}' is not loaded.");
                    break;
                }
                owner = other;
            }
            var nodeDef = owner.NodeDefs.FirstOrDefault(def => String.Equals(def.Name, name.Name, StringComparison.OrdinalIgnoreCase));
            if (nodeDef is null)
            {
                unresolved.Add(ReferenceEquals(owner, scope.Document)
                    ? $"nodeDef '{name}' was not found."
                    : $"nodeDef '{name}' was not found in the schema '{owner.Name}'.");
                break;
            }
            if (ReferenceEquals(nodeDef, node) || chain.Any(item => ReferenceEquals(item.Node, nodeDef)))
            {
                break;
            }
            chain.Add((nodeDef, owner));
            current = nodeDef;
        }
        return chain;
    }

    /// <summary>
    /// The data type def of <paramref name="dataType"/> (as named in <paramref name="owner"/>) followed by the defs it is based on,
    /// each with the schema that defines it. A data type without a def (a built-in type) has an empty chain.
    /// </summary>
    private static List<(EditDataTypeDef Definition, EditDocument Owner)> GetDataTypeDefChain(Scope scope, EditDocument owner, SchemaName? dataType,
        List<string> unresolved)
    {
        var chain = new List<(EditDataTypeDef Definition, EditDocument Owner)>();
        for (var name = dataType; name is { } current;)
        {
            var defOwner = owner;
            if (!IsLocal(owner, current))
            {
                if (scope.FindSchema?.Invoke(current.Namespace!) is not { } other)
                {
                    // built-in types (sys.*) have no def; only report types of included schemas
                    if (IsIncluded(owner, current.Namespace))
                    {
                        unresolved.Add(scope.FindSchema is null
                            ? $"dataType '{current}' is defined in the included schema '{current.Namespace}'."
                            : $"dataType '{current}': the included schema '{current.Namespace}' is not loaded.");
                    }
                    break;
                }
                defOwner = other;
            }
            var dataTypeDef = defOwner.DataTypeDefs.FirstOrDefault(def => String.Equals(def.Name, current.Name, StringComparison.OrdinalIgnoreCase));
            if (dataTypeDef is null)
            {
                // a local name without a def is a built-in type
                if (!ReferenceEquals(defOwner, owner))
                {
                    unresolved.Add($"dataType '{current}' was not found in the schema '{defOwner.Name}'.");
                }
                break;
            }
            if (chain.Any(item => ReferenceEquals(item.Definition, dataTypeDef)))
            {
                break;
            }
            chain.Add((dataTypeDef, defOwner));
            owner = defOwner;
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

    // owner: the schema the processor refs are authored in (their names are resolved in it)
    private static void MergeProcessors(Scope scope, EditDocument owner, List<ResolvedProcessor> target, IEnumerable<EditProcessorRef>? source,
        string sourceName, bool own, List<string> unresolved)
    {
        foreach (var processor in source ?? [])
        {
            target.RemoveAll(existing => existing.Processor.Processor == processor.Processor);
            target.Add(new(processor, sourceName, own, ResolveProcessorProperties(scope, owner, processor, sourceName, own, unresolved)));
        }
    }

    private static List<ResolvedProperty> ResolveProcessorProperties(Scope scope, EditDocument owner, EditProcessorRef processor,
        string sourceName, bool own, List<string> unresolved)
    {
        var properties = new List<ResolvedProperty>();
        var name = processor.Processor;
        if (FindProcessorDef(scope, owner, name) is { } found)
        {
            MergeProperties(properties, found.Definition.Properties, $"processorDef {scope.Name(found.Definition.Name, found.Owner)}", own: false);
        }
        else if (IsLocal(owner, name))
        {
            unresolved.Add($"processorDef '{name}' was not found.");
        }
        // a processor key ('midi.sysex') is a processor of a loaded assembly; any other 'namespace.name' ('midi.sysexbyte')
        // is a processorDef of another schema. Without isProcessorKey they cannot be told apart and nothing is reported.
        else if (scope.IsProcessorKey is not null && !scope.IsProcessorKey(name.FullName))
        {
            if (scope.FindSchema?.Invoke(name.Namespace!) is { } loaded)
            {
                unresolved.Add($"processor '{name}' is not a known processor, nor a processorDef of the schema '{loaded.Name}'.");
            }
            else if (IsIncluded(owner, name.Namespace))
            {
                unresolved.Add(scope.FindSchema is null
                    ? $"processorDef '{name}' is defined in the included schema '{name.Namespace}'."
                    : $"processorDef '{name}': the included schema '{name.Namespace}' is not loaded.");
            }
            else
            {
                unresolved.Add($"processor '{name}' is not a known processor, nor a processorDef of an included schema.");
            }
        }
        MergeProperties(properties, processor.Properties, sourceName, own);
        return properties;
    }

    // the processor def a name designates as seen from owner, with the schema that defines it
    private static (EditProcessorDef Definition, EditDocument Owner)? FindProcessorDef(Scope scope, EditDocument owner, SchemaName name)
    {
        var defOwner = IsLocal(owner, name) ? owner : scope.FindSchema?.Invoke(name.Namespace!);
        return defOwner?.ProcessorDefs.FirstOrDefault(def => String.Equals(def.Name, name.Name, StringComparison.OrdinalIgnoreCase)) is { } definition
            ? (definition, defOwner)
            : null;
    }

    private static bool IsIncluded(EditDocument document, string? schema)
        => !String.IsNullOrEmpty(schema)
            && document.Includes.Any(include => String.Equals(include.Schema, schema, StringComparison.OrdinalIgnoreCase));

    private static bool IsLocal(EditDocument document, SchemaName name)
        => String.IsNullOrEmpty(name.Namespace) || String.Equals(name.Namespace, document.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a resolution works with: the edited document, how processor keys are recognized and how other schemas are found.
    /// </summary>
    private sealed record Scope(EditDocument Document, Func<string, bool>? IsProcessorKey, Func<string, EditDocument?>? FindSchema)
    {
        /// <summary>The name of a definition as a source: 'name' in the edited document, 'Schema.name' in another schema.</summary>
        public string Name(string name, EditDocument owner)
            => ReferenceEquals(owner, Document) ? name : $"{owner.Name}{SchemaName.Separator}{name}";
    }
}
