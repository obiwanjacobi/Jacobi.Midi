using System.Globalization;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// A node of the tree the <see cref="ValueRefFinder"/> walks, taken from the compiled schema (node defs expanded) or,
/// when that does not compile, from the authored schema. Only what the finder needs.
/// </summary>
/// <param name="DataType">The data type of a field (or its node def); empty for other nodes.</param>
/// <param name="Count">The constant count of a repeat; null otherwise.</param>
/// <param name="Processors">The processors of the node that can publish values.</param>
internal sealed record ValueRefNode(string Name, SchemaNodeKind Kind, string DataType, int? Count,
    IReadOnlyList<ValueRefPublisher> Processors, IReadOnlyList<ValueRefNode> Members);

/// <summary>
/// A processor of a node: its key ('namespace.id', a processor def resolved) and the namespace ('pubns') it publishes under.
/// </summary>
internal sealed record ValueRefPublisher(string Key, string? PublishNamespace)
{
    /// <summary>The namespace the processor publishes its values under: its 'pubns', otherwise its key.</summary>
    public string Namespace => String.IsNullOrWhiteSpace(PublishNamespace) ? Key : PublishNamespace;
}

/// <summary>
/// One node on the path of a <see cref="ValueRefTarget"/> (from the outermost node down).
/// </summary>
/// <param name="Path">The schema path up to and including this node (without instance indices).</param>
/// <param name="HoldsReferrer">True when the node is an ancestor of the node that refers (so '[]', the same item, applies).</param>
/// <param name="Count">The constant count of a repeat; null otherwise.</param>
internal sealed record ValueRefSegment(string Name, string Path, bool IsRepeat, bool HoldsReferrer, int? Count);

/// <summary>
/// A field that a 'ref:' can target, with the nodes on its path (to give the repeats on it an instance index).
/// </summary>
internal sealed record ValueRefTarget(IReadOnlyList<ValueRefSegment> Segments, string DataType)
{
    /// <summary>The schema path of the field (without instance indices).</summary>
    public string Path => Segments[^1].Path;

    /// <summary>The path with '[]' after each repeat, to show where an instance index goes.</summary>
    public string DisplayPath => String.Join(SchemaPath.Separator, Segments.Select(segment => segment.IsRepeat ? $"{segment.Name}[]" : segment.Name));

    public IEnumerable<ValueRefSegment> Repeats => Segments.Where(segment => segment.IsRepeat);

    /// <summary>
    /// The reference text: 'ref:' and the path, with the index of each repeat on it: <paramref name="index"/> gives the index
    /// of a repeat segment (null for '[]', the same item as the referring node).
    /// </summary>
    public string ToReference(Func<ValueRefSegment, int?> index)
        => SchemaNodeRef.Prefix + String.Join(SchemaPath.Separator, Segments.Select(segment => segment.IsRepeat
            ? $"{segment.Name}[{index(segment)?.ToString(CultureInfo.InvariantCulture)}]"
            : segment.Name));

    /// <summary>
    /// The relative reference text from <paramref name="referrer"/>: 'ref:', a '^' per level up to the nearest ancestor
    /// it shares with the field, then the path down. It has no instance indices: it keeps the instance of the referrer
    /// (so it cannot reach into a repeat that does not hold the referrer).
    /// </summary>
    public string ToRelativeReference(SchemaPath referrer)
    {
        var common = Segments.Count(segment => segment.HoldsReferrer);
        return SchemaNodeRef.Prefix + String.Join(SchemaPath.Separator,
            Enumerable.Repeat(SchemaNodeRef.Up, referrer.Segments.Count() - common).Concat(Segments.Skip(common).Select(segment => segment.Name)));
    }
}

/// <summary>
/// A value that a processor publishes, as a 'pub:' reference.
/// </summary>
/// <param name="Reference">The reference text: 'pub:namespace.name'.</param>
/// <param name="Source">The path of the node with the processor.</param>
internal sealed record ValueRefPublished(string Reference, string DataType, string Source, string? Description);

/// <summary>The fields (for 'ref:') and published values (for 'pub:') a node can refer to, nearest first.</summary>
/// <param name="Referrer">The path of the node that refers (from its outermost node): relative paths start there.</param>
internal sealed record ValueRefCandidates(SchemaPath Referrer, IReadOnlyList<ValueRefTarget> Targets, IReadOnlyList<ValueRefPublished> Published);

/// <summary>
/// Finds what the value-or-reference properties (value, byteLength, byteSize, count, selectedIndex) of a node can refer to.
/// A referenced value must be known when the node is processed, so only the nodes before it are offered: from the node
/// (not included) back to its outermost node (a root or a node def), nearest first. Those are the members before the node
/// and before each of its ancestors, with their nested members. The alternatives of a choice are never offered (which one is
/// read is only known at runtime), except for the alternative the node itself is in.
/// Groups are not offered: a reference must target a field.
/// </summary>
internal static class ValueRefFinder
{
    /// <summary>The tree of a compiled node (node defs expanded).</summary>
    public static ValueRefNode FromCompiled(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            SchemaField field => new(field.Name, field.Kind, field.DataType?.ToString() ?? field.NodeDef?.ToString() ?? String.Empty, null,
                Publishers(field.Processors), []),
            SchemaGroup group => new(group.Name, group.Kind, String.Empty, (group as SchemaRepeat)?.Count is int count ? count : null,
                Publishers(group.Processors), [.. group.Members.Select(FromCompiled)]),
            _ => new(node.Name, node.Kind, String.Empty, null, [], [])
        };

        static IReadOnlyList<ValueRefPublisher> Publishers(IReadOnlyList<SchemaProcessorRef> processors)
            => [.. processors.Select(processor => new ValueRefPublisher(processor.Key?.ToString() ?? processor.Processor.FullName, processor.PublishNamespace))];
    }

    /// <summary>
    /// The tree of an authored node. The members a node def adds to a node are not in it (they are only known when compiled).
    /// </summary>
    /// <param name="processorKey">The key of a processor ref (resolves a processor def name).</param>
    public static ValueRefNode FromDraft(EditNode node, Func<EditProcessorRef, string> processorKey)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(processorKey);
        return node switch
        {
            EditField field => new(field.Name, field.Kind, field.DataType?.ToString() ?? field.NodeDef?.ToString() ?? String.Empty, null,
                Publishers(field.Processors), []),
            EditGroup group => new(group.Name, group.Kind, String.Empty, (group as EditRepeat)?.Count is int count ? count : null,
                Publishers(group.Processors), [.. group.Members.Select(member => FromDraft(member, processorKey))]),
            _ => new(node.Name, node.Kind, String.Empty, null, [], [])
        };

        IReadOnlyList<ValueRefPublisher> Publishers(IEnumerable<EditProcessorRef> processors)
            => [.. processors.Select(processor => new ValueRefPublisher(processorKey(processor), processor.PublishNamespace))];
    }

    /// <summary>
    /// Finds the fields and published values the node at <paramref name="names"/> can refer to, nearest first.
    /// </summary>
    /// <param name="top">The outermost node (a root or a node def).</param>
    /// <param name="names">The names from <paramref name="top"/> (its own name first) down to the node that refers.</param>
    /// <param name="describe">The property descriptors of a processor key ('namespace.id'); null when the processor is not known.</param>
    /// <exception cref="InvalidOperationException">The names do not lead to a node in <paramref name="top"/>.</exception>
    public static ValueRefCandidates Find(ValueRefNode top, IReadOnlyList<string> names,
        Func<string, IReadOnlyList<PropertyDescriptor>?> describe)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(describe);
        var chain = Chain(top, names);
        var targets = new List<ValueRefTarget>();
        var published = new List<ValueRefPublished>();
        // the ancestors of the node: the segments of the path down to each level
        var ancestors = new List<ValueRefSegment>();
        for (var level = 0; level < chain.Count - 1; level++)
        {
            ancestors.Add(Segment(chain[level], ancestors, holdsReferrer: true));
        }

        for (var level = chain.Count - 1; level > 0; level--)
        {
            var parent = chain[level - 1];
            // the other members of a choice are alternatives: they are not read when this one is
            if (parent.Kind == SchemaNodeKind.Choice)
            {
                continue;
            }
            var prefix = ancestors.Take(level).ToList();
            var index = IndexOf(parent.Members, chain[level]);
            for (var before = index - 1; before >= 0; before--)
            {
                Collect(parent.Members[before], prefix);
            }
        }
        return new ValueRefCandidates(new SchemaPath(String.Join(SchemaPath.Separator, names)), targets, published.DistinctBy(value => value.Reference, StringComparer.OrdinalIgnoreCase).ToList());

        // the fields of a node before the referring node, in reverse order (nearest first)
        void Collect(ValueRefNode node, List<ValueRefSegment> prefix)
        {
            // which alternative of a choice is read is only known at runtime
            if (node.Kind == SchemaNodeKind.Choice)
            {
                return;
            }
            var segments = new List<ValueRefSegment>(prefix) { Segment(node, prefix, holdsReferrer: false) };
            AddPublished(node, segments[^1].Path);
            if (node.Kind == SchemaNodeKind.Field)
            {
                targets.Add(new ValueRefTarget(segments, node.DataType));
                return;
            }
            for (var member = node.Members.Count - 1; member >= 0; member--)
            {
                Collect(node.Members[member], segments);
            }
        }

        void AddPublished(ValueRefNode node, string source)
        {
            foreach (var processor in node.Processors)
            {
                foreach (var descriptor in describe(processor.Key) ?? [])
                {
                    if (descriptor.IsPublished)
                    {
                        published.Add(new ValueRefPublished($"{SchemaPubRef.Prefix}{processor.Namespace}{SchemaPubRef.Separator}{descriptor.Name}",
                            descriptor.DataType.ToString(), source, descriptor.Description));
                    }
                }
            }
        }
    }

    private static ValueRefSegment Segment(ValueRefNode node, IReadOnlyList<ValueRefSegment> prefix, bool holdsReferrer)
        => new(node.Name, prefix.Count == 0 ? node.Name : $"{prefix[^1].Path}{SchemaPath.Separator}{node.Name}",
            node.Kind == SchemaNodeKind.Repeat, holdsReferrer, node.Count);

    // the nodes from the top down to the referring node
    private static List<ValueRefNode> Chain(ValueRefNode top, IReadOnlyList<string> names)
    {
        if (names.Count == 0 || !String.Equals(top.Name, names[0], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The node '{String.Join(SchemaPath.Separator, names)}' is not in '{top.Name}'.");
        }
        var chain = new List<ValueRefNode> { top };
        foreach (var name in names.Skip(1))
        {
            chain.Add(chain[^1].Members.FirstOrDefault(member => String.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The node '{String.Join(SchemaPath.Separator, names)}' is not in '{top.Name}'."));
        }
        return chain;
    }

    private static int IndexOf(IReadOnlyList<ValueRefNode> members, ValueRefNode member)
    {
        for (var index = 0; index < members.Count; index++)
        {
            if (ReferenceEquals(members[index], member))
            {
                return index;
            }
        }
        return -1;
    }
}
