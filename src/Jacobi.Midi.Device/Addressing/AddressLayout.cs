using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Which way data may be transferred for an addressed node (<c>midi.access</c>).
/// </summary>
public enum NodeAccess
{
    ReadWrite,
    /// <summary>The device only transmits the data (e.g. Yamaha System Information).</summary>
    Read,
    /// <summary>The device only receives the data (trigger addresses such as XG System On).</summary>
    Write,
}

/// <summary>
/// The static address layout of one plan node. <see cref="Offset"/> is relative to the frame the node is in:
/// the item of the nearest enclosing repeat, or the absolute address space outside repeats.
/// One instance per plan node, not per repeat item.
/// </summary>
internal sealed class AddressLayoutNode
{
    public required NodeInfo Node { get; init; }
    public AddressLayoutNode? Parent { get; init; }
    public List<AddressLayoutNode> Members { get; } = [];

    /// <summary>The start address relative to the frame (for a repeat: of its first item).</summary>
    public long Offset { get; set; }
    /// <summary>The number of addresses (for a repeat: all items).</summary>
    public long Size { get; set; }

    /// <summary>Repeat only: the address distance between items.</summary>
    public long Stride { get; set; }
    /// <summary>Repeat only: the number of items.</summary>
    public int Count { get; set; }

    public bool IsRepeat => Node is RepeatInfo;
    public bool IsField => Node is FieldInfo;
    public bool IsAtomic { get; init; }
    public NodeAccess Access { get; init; }

    public AddressRange RangeIn(long frameBase) => new(frameBase + Offset, Size);
}

/// <summary>
/// One occurrence of a layout node at an absolute address: the node plus the instance (repeat item indices) it is in.
/// </summary>
internal readonly record struct AddressOccurrence(AddressLayoutNode Layout, InstancePath Instance, AddressRange Range)
{
    public NodeInfo Node => Layout.Node;
}

/// <summary>
/// Builds the address layout of a map plan from the <c>midi.address</c>, <c>midi.stride</c>, <c>midi.repeat</c>,
/// <c>midi.atomic</c> and <c>midi.access</c> properties, and finds the occurrences of fields and atomic groups in an
/// address range. Addresses only increase in plan order, so a plan range covers one ascending address span.
/// </summary>
internal sealed class AddressLayout
{
    private readonly Dictionary<NodeInfo, AddressLayoutNode> _byNode = [];
    private readonly Dictionary<SchemaPath, AddressLayoutNode> _byPath = [];

    private AddressLayout(AddressLayoutNode root) => Root = root;

    public AddressLayoutNode Root { get; }

    public AddressLayoutNode? Find(NodeInfo node)
        => _byNode.GetValueOrDefault(node);

    // by path: the body of an open choice is a plan of its own, with other node instances
    public AddressLayoutNode? Find(SchemaPath path)
        => _byPath.GetValueOrDefault(path);

    public static AddressLayout Build(string document, GroupInfo root, int bitsPerByte, AddressMapProblems problems)
    {
        var builder = new Builder(document, bitsPerByte, problems);
        var layout = builder.Node(root, parent: null, cursor: 0, inRepeat: false, inAtomic: false, NodeAccess.ReadWrite)!;
        var result = new AddressLayout(layout);
        result.Index(layout);
        return result;
    }

    private void Index(AddressLayoutNode node)
    {
        _byNode[node.Node] = node;
        _byPath[node.Node.Path] = node;
        foreach (var member in node.Members)
        {
            Index(member);
        }
    }

    /// <summary>The absolute address of a node occurrence; missing instance indices are filled with the first or last item.</summary>
    public long AddressOf(AddressLayoutNode node, InstancePath instance, bool lastItem)
    {
        var chain = new List<AddressLayoutNode>();
        for (var n = node; n is not null; n = n.Parent)
        {
            chain.Add(n);
        }
        chain.Reverse();

        long frameBase = 0;
        var depth = 0;
        foreach (var n in chain)
        {
            if (n.IsRepeat && n != node)
            {
                var index = depth < instance.Length && instance[depth] >= 0
                    ? instance[depth]
                    : (lastItem ? n.Count - 1 : 0);
                frameBase += n.Offset + index * n.Stride;
                depth++;
            }
        }
        return frameBase + node.Offset;
    }

    /// <summary>The first field (in plan order) below a node; null when it has none.</summary>
    public static AddressLayoutNode? FirstField(AddressLayoutNode node)
    {
        while (!node.IsField)
        {
            if (node.Members.Count == 0)
            {
                return null;
            }
            node = node.Members[0];
        }
        return node;
    }

    /// <summary>The last field (in plan order) below a node; null when it has none.</summary>
    public static AddressLayoutNode? LastField(AddressLayoutNode node)
    {
        while (!node.IsField)
        {
            if (node.Members.Count == 0)
            {
                return null;
            }
            node = node.Members[^1];
        }
        return node;
    }

    /// <summary>The number of repeats above a node (not counting the node itself).</summary>
    public static int RepeatDepth(AddressLayoutNode node)
    {
        var depth = 0;
        for (var n = node.Parent; n is not null; n = n.Parent)
        {
            if (n.IsRepeat)
            {
                depth++;
            }
        }
        return depth;
    }

    /// <summary>The fields (in address and plan order) whose addresses overlap the span.</summary>
    public IEnumerable<AddressOccurrence> Fields(AddressRange span)
        => Occurrences(Root, 0, InstancePath.Empty, span, atomic: false);

    /// <summary>The atomic groups (in address order) whose addresses overlap the span.</summary>
    public IEnumerable<AddressOccurrence> AtomicGroups(AddressRange span)
        => Occurrences(Root, 0, InstancePath.Empty, span, atomic: true);

    private static IEnumerable<AddressOccurrence> Occurrences(AddressLayoutNode node, long frameBase, InstancePath instance, AddressRange span, bool atomic)
    {
        var range = node.RangeIn(frameBase);
        if (!range.Overlaps(span))
        {
            yield break;
        }

        if (atomic ? node.IsAtomic && !node.IsRepeat : node.IsField)
        {
            yield return new AddressOccurrence(node, instance, range);
            yield break;
        }

        if (node.IsRepeat)
        {
            if (node.Stride <= 0)
            {
                yield break;
            }
            var start = range.Start;
            var first = (int)Math.Max(0, (span.Start - start) / node.Stride);
            var last = (int)Math.Min(node.Count - 1, (span.End - 1 - start) / node.Stride);
            for (var index = first; index <= last; index++)
            {
                var itemBase = start + index * node.Stride;
                var itemInstance = instance.Append(index);
                foreach (var member in node.Members)
                {
                    foreach (var occurrence in Occurrences(member, itemBase, itemInstance, span, atomic))
                    {
                        yield return occurrence;
                    }
                }
            }
            yield break;
        }

        foreach (var member in node.Members)
        {
            foreach (var occurrence in Occurrences(member, frameBase, instance, span, atomic))
            {
                yield return occurrence;
            }
        }
    }

    private sealed class Builder(string document, int bitsPerByte, AddressMapProblems problems)
    {
        public AddressLayoutNode? Node(NodeInfo node, AddressLayoutNode? parent, long cursor, bool inRepeat, bool inAtomic, NodeAccess inheritedAccess)
        {
            var start = cursor;
            if (Address(node, MidiProperties.Address) is { } explicitStart)
            {
                if (inRepeat)
                {
                    Error(node, $"'{MidiProperties.Address}' is not allowed inside a repeat item (all items would get the same address).");
                }
                else if (explicitStart < cursor)
                {
                    Error(node, $"the start address 0x{explicitStart:X} overlaps the previous node, which ends at 0x{cursor:X} (addresses must increase in plan order).");
                }
                else
                {
                    start = explicitStart;
                }
            }

            var atomic = MidiProperties.IsTrue(MidiProperties.Find(node, MidiProperties.Atomic));
            if (atomic && inAtomic)
            {
                Error(node, "an atomic group cannot be inside another atomic group.");
            }
            if (atomic && node is FieldInfo)
            {
                Error(node, $"'{MidiProperties.Atomic}' only applies to groups.");
            }

            var layout = new AddressLayoutNode
            {
                Node = node,
                Parent = parent,
                Offset = start,
                IsAtomic = atomic && node is GroupInfo,
                Access = Access(node, inheritedAccess),
            };
            var explicitSize = Address(node, MidiProperties.Stride);
            if (node is not RepeatInfo && MidiProperties.Find(node, MidiProperties.Repeat) is not null)
            {
                Error(node, $"'{MidiProperties.Repeat}' only applies to repeats.");
            }

            switch (node)
            {
                case FieldInfo field:
                    layout.Size = FieldSize(field, explicitSize);
                    break;
                case RepeatInfo repeat:
                    Repeat(repeat, layout, explicitSize, inAtomic || atomic);
                    break;
                case ChoiceInfo choice:
                    Error(choice, "a choice is not supported in an address map.");
                    break;
                case GroupInfo group when IsBitPacked(group):
                    BitPacked(group, layout, explicitSize);
                    break;
                case GroupInfo group:
                    var end = start;
                    foreach (var member in group.Members)
                    {
                        if (Node(member, layout, end, inRepeat, inAtomic || atomic, layout.Access) is { } child)
                        {
                            layout.Members.Add(child);
                            end = child.Offset + child.Size;
                        }
                    }
                    layout.Size = Size(group, end - start, explicitSize);
                    break;
            }
            return layout;
        }

        private void Repeat(RepeatInfo repeat, AddressLayoutNode layout, long? explicitSize, bool inAtomic)
        {
            if (RepeatCount(repeat) is not int count)
            {
                return;
            }

            // the members are laid out relative to the start of an item
            long end = 0;
            foreach (var member in repeat.Members)
            {
                if (Node(member, layout, end, inRepeat: true, inAtomic, layout.Access) is { } child)
                {
                    layout.Members.Add(child);
                    end = child.Offset + child.Size;
                }
            }

            layout.Count = count;
            layout.Stride = Size(repeat, end, explicitSize);
            layout.Size = count * layout.Stride;
            if (layout.Stride <= 0)
            {
                Error(repeat, "a repeat item occupies no addresses.");
            }
        }

        // the number of items in the address space: 'midi.repeat', else the constant count of the repeat.
        // The count is what a dump holds; 'midi.repeat' is what the device has, also when a dump holds fewer items.
        private int? RepeatCount(RepeatInfo repeat)
        {
            if (MidiProperties.Find(repeat, MidiProperties.Repeat) is { } text)
            {
                if (Int32.TryParse(text, out var repeatCount) && repeatCount > 0)
                {
                    return repeatCount;
                }
                Error(repeat, $"'{MidiProperties.Repeat}' must be a positive number, not '{text}'.");
                return null;
            }
            if (repeat.Count is int count)
            {
                return count;
            }
            Error(repeat, $"a repeat in an address map needs a '{MidiProperties.Repeat}' or a constant count.");
            return null;
        }

        private void BitPacked(GroupInfo group, AddressLayoutNode layout, long? explicitSize)
        {
            var bits = 0;
            foreach (var member in group.Members)
            {
                if (member is not FieldInfo field)
                {
                    Error(member, "only fields are supported in a bit-packed group.");
                    continue;
                }
                bits += Int32.TryParse(MidiProperties.Find(field, "bits"), out var width) ? width : 1;
            }

            long size = (bits + 7) / 8;
            if (ByteLengthOf(group) is { } byteLength)
            {
                size = byteLength;
            }
            layout.Size = Size(group, size, explicitSize);

            // all fields share the addresses of the group
            foreach (var member in group.Members.OfType<FieldInfo>())
            {
                CheckField(member);
                layout.Members.Add(new AddressLayoutNode
                {
                    Node = member,
                    Parent = layout,
                    Offset = layout.Offset,
                    Size = layout.Size,
                    Access = Access(member, layout.Access),
                });
            }
        }

        private long FieldSize(FieldInfo field, long? explicitSize)
        {
            CheckField(field);
            long? size = field.ByteLength is int length ? length : field.DataType.FixedSize;
            if (size is null && explicitSize is null)
            {
                Error(field, $"the field has no constant size; give it a '{MidiProperties.Stride}'.");
                return 0;
            }
            return Size(field, size ?? 0, explicitSize);
        }

        private void CheckField(FieldInfo field)
        {
            if (field.ByteOffset is not null)
            {
                Error(field, "a virtual field (byteOffset) is not supported in an address map.");
            }
        }

        private long Size(NodeInfo node, long computed, long? explicitSize)
        {
            if (explicitSize is not { } size)
            {
                return computed;
            }
            if (size < computed)
            {
                Error(node, $"'{MidiProperties.Stride}' 0x{size:X} is smaller than the content (0x{computed:X} addresses).");
                return computed;
            }
            return size;
        }

        private long? Address(NodeInfo node, string property)
        {
            if (MidiProperties.Find(node, property) is not { } text)
            {
                return null;
            }
            if (!AddressSpace.TryParse(text, bitsPerByte, out var address, out var error))
            {
                Error(node, $"'{property}': {error}");
                return null;
            }
            return address;
        }

        private NodeAccess Access(NodeInfo node, NodeAccess inherited)
        {
            var text = MidiProperties.Find(node, MidiProperties.Access);
            if (text is null)
            {
                return inherited;
            }
            if (Enum.TryParse<NodeAccess>(text, ignoreCase: true, out var access))
            {
                return access;
            }
            Error(node, $"'{MidiProperties.Access}' must be readwrite, read or write, not '{text}'.");
            return inherited;
        }

        private const string BitPackerKey = "sys.bitpacker";
        private static bool IsBitPacked(GroupInfo group)
            => group.Pipeline.OwnLayoutProcessors.Any(binding =>
                String.Equals(binding.Processor.Key.ToString(), BitPackerKey, StringComparison.OrdinalIgnoreCase));

        // the bit-packer capacity: a 'byteLength' property on the group or on its bit-packer
        private static long? ByteLengthOf(GroupInfo group)
        {
            var text = MidiProperties.Find(group, "byteLength")
                ?? group.Pipeline.OwnLayoutProcessors
                    .SelectMany(binding => binding.Properties)
                    .FirstOrDefault(p => p.Name.EndsWith("byteLength", StringComparison.OrdinalIgnoreCase))?.Value;
            return Int64.TryParse(text, out var length) ? length : null;
        }

        private void Error(NodeInfo node, string message)
            => problems.Error(document, node.Path, message);
    }
}
