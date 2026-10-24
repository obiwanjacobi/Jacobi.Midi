using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// A unit of transfer: an address range plus the node range whose data it holds.
/// </summary>
public sealed record AddressBlock(AddressRange Addresses, PlanRange Nodes);

/// <summary>
/// The result of resolving an address range against a map: the block of whole fields it covers, and whether the
/// range started or ended inside a field or touched unmapped addresses.
/// </summary>
public sealed class AddressMatch
{
    internal AddressMatch(AddressRange requested, AddressBlock? block, bool isAligned, IReadOnlyList<AddressRange> unmapped)
    {
        Requested = requested;
        Block = block;
        IsAligned = isAligned;
        Unmapped = unmapped;
    }

    public AddressRange Requested { get; }
    /// <summary>The covered fields; null when nothing in the range is mapped.</summary>
    public AddressBlock? Block { get; }
    /// <summary>True when the range starts and ends on field boundaries, so <see cref="Block"/> can be decoded from the data.</summary>
    public bool IsAligned { get; }
    /// <summary>The addresses in the requested range that no field occupies.</summary>
    public IReadOnlyList<AddressRange> Unmapped { get; }
}

/// <summary>
/// The address layout of one address-map root: converts between schema node ranges (<see cref="PlanRange"/>) and
/// address ranges and knows the atomic groups. Deduced from the root's <see cref="ExecutionPlan"/> and validated
/// against it, together with the envelope messages that can carry its contract (memberNodeDef).
/// Immutable and thread-safe; <see cref="AddressMaps"/> creates and caches one per plan.
/// </summary>
internal sealed class AddressMap
{
    private readonly AddressLayout _layout;
    private readonly Serializer _serializer;

    private AddressMap(Serializer serializer, SchemaName root, ExecutionPlan plan, AddressProtocol protocol, DeviceValues device, AddressLayout layout)
    {
        _serializer = serializer;
        Root = root;
        Plan = plan;
        Protocol = protocol;
        Device = device;
        _layout = layout;
        Range = layout.Root.RangeIn(0);
    }

    /// <summary>
    /// Builds and validates the map of a schema root and finds its messages; see <see cref="Create(Serializer, ExecutionPlan)"/>.
    /// </summary>
    public static AddressMap Create(Serializer serializer, SchemaName root)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        return Create(serializer, serializer.GetPlan(root));
    }

    /// <summary>
    /// Builds and validates the map of a plan and finds its messages (open choices whose memberNodeDef is the root's
    /// nodeDef, in the serializer's schemas); throws an <see cref="AddressMapException"/> with all problems found.
    /// </summary>
    public static AddressMap Create(Serializer serializer, ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Name is not { } root)
        {
            throw new AddressMapException(plan.Root.Path, [$"{plan.Root.Path}: the plan has no schema name; create it with the serializer (GetPlan)."]);
        }

        var errors = new List<string>();
        var group = plan.Root.Group;

        if (group.NodeDef is not { } contract)
        {
            throw new AddressMapException(root.FullName, [$"{plan.Root.Path}: the map root does not conform to a message contract (it has no nodeDef)."]);
        }
        contract = String.IsNullOrEmpty(contract.Namespace) ? new SchemaName($"{root.Namespace}.{contract.Name}") : contract;

        var protocol = AddressProtocol.Create(serializer.Schemas, serializer, contract, errors).WithOverrides(group, errors);
        var device = DeviceValues.FromProperties(group, errors);
        var layout = AddressLayout.Build(plan.Root, protocol.AddressSpace.BitsPerByte, errors);

        var end = layout.Root.RangeIn(0).End;
        if (protocol.AddressSpace.ByteCount > 0 && end > protocol.AddressSpace.Capacity)
        {
            errors.Add($"{plan.Root.Path}: the map ends at 0x{end:X}, outside the address space of {protocol.AddressSpace.ByteCount} x {protocol.AddressSpace.BitsPerByte} bits.");
        }

        if (errors.Count > 0)
        {
            throw new AddressMapException(root.FullName, errors);
        }
        return new AddressMap(serializer, root, plan, protocol, device, layout);
    }

    public SchemaName Root { get; }
    public ExecutionPlan Plan { get; }
    /// <summary>The messages that can carry this map (with the map root's overrides applied).</summary>
    public AddressProtocol Protocol { get; }
    /// <summary>The device values fixed by the map root (<c>midi.device.*</c>).</summary>
    public DeviceValues Device { get; }
    /// <summary>The addresses the map covers (from the root's start to its end).</summary>
    public AddressRange Range { get; }

    /// <summary>The device value names the messages need that the map root does not fix; the client supplies them.</summary>
    public IReadOnlyList<string> RequiredDeviceValues
        => [.. Protocol.Requests.Concat(Protocol.DataMessages)
            .SelectMany(m => m.DeviceValueNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !Device.TryGet(name, out _))];

    /// <summary>A range over one node occurrence (a group with all its fields, or one field).</summary>
    public PlanRange CreateRange(SchemaPath path, InstancePath instance = default)
        => Plan.CreateRange(path, instance, path, instance);

    /// <summary>A range from one node occurrence up to and including another (in plan order).</summary>
    public PlanRange CreateRange(SchemaPath fromPath, InstancePath fromInstance, SchemaPath toPath, InstancePath toInstance)
        => Plan.CreateRange(fromPath, fromInstance, toPath, toInstance);

    /// <summary>
    /// A range over one node occurrence (see <see cref="MapOccurrence"/>): a group or field, all items of a repeat, or one
    /// item of a repeat when the instance has an index for the repeat itself.
    /// </summary>
    internal PlanRange CreateOccurrenceRange(SchemaPath path, InstancePath instance)
    {
        var layout = _layout.Find(path) ?? throw new ArgumentException($"'{path}' is not a node of the map '{Root}'.", nameof(path));
        if (layout.IsRepeat && instance.Length > AddressLayout.RepeatDepth(layout))
        {
            // one item: from its first to its last member
            return layout.Members is [var first, ..]
                ? CreateRange(first.Node.Path, instance, layout.Members[^1].Node.Path, instance)
                : throw new ArgumentException($"The repeat '{path}' has no members.", nameof(path));
        }
        return CreateRange(path, instance);
    }

    /// <summary>The addresses a node range occupies (widened to whole addresses, gaps included).</summary>
    public AddressRange GetAddressRange(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (range.ExecutionPlan != Plan)
        {
            throw new ArgumentException($"The range is not a range of the map plan '{Root}'.", nameof(range));
        }

        var first = LayoutOf(range.First);
        var last = LayoutOf(range.Last);
        var start = _layout.AddressOf(first, range.FromInstance, lastItem: false);
        var end = _layout.AddressOf(last, range.ToInstance, lastItem: true) + last.Size;
        return new AddressRange(start, end - start);
    }

    /// <summary>Which nodes an address range targets (e.g. the address and size of a received data message).</summary>
    public AddressMatch Resolve(AddressRange range)
    {
        var fields = _layout.Fields(range).ToList();
        if (fields.Count == 0)
        {
            return new AddressMatch(range, null, false, [range]);
        }

        var unmapped = new List<AddressRange>();
        var position = range.Start;
        foreach (var field in fields)
        {
            if (field.Range.Start > position)
            {
                unmapped.Add(new AddressRange(position, field.Range.Start - position));
            }
            position = Math.Max(position, field.Range.End);
        }
        if (position < range.End)
        {
            unmapped.Add(new AddressRange(position, range.End - position));
        }

        var block = CreateBlock(fields);
        var aligned = block.Addresses.Start == range.Start && block.Addresses.End == range.End;
        return new AddressMatch(range, block, aligned, unmapped);
    }

    /// <summary>
    /// Widens a node range to whole addresses and to the atomic groups it overlaps.
    /// </summary>
    public PlanRange WidenToAtomic(PlanRange range)
    {
        var span = GetAddressRange(range);
        var widened = span;
        foreach (var atomic in _layout.AtomicGroups(span))
        {
            widened = widened.Union(atomic.Range);
        }
        return Resolve(widened).Block?.Nodes ?? range;
    }

    /// <summary>
    /// Splits a node range into the blocks to transfer with the given message: per field, per atomic group, or in
    /// contiguous parts split at unmapped gaps and at the message's maximum data size, never inside a field or an
    /// atomic group. Nodes the access excludes (write-only on receive, read-only on send) are left out.
    /// With an atomic message, only the atomic groups are returned.
    /// </summary>
    public IReadOnlyList<AddressBlock> Split(PlanRange range, AddressMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var span = GetAddressRange(range);
        var excluded = message.Kind == AddressMessageKind.Request ? AddressAccess.Write : AddressAccess.Read;
        var fields = _layout.Fields(span).Where(f => f.Layout.Access != excluded).ToList();
        var blocks = new List<AddressBlock>();

        switch (message.Unit)
        {
            case AddressUnit.Field:
                // fields that share their addresses (a bit-packed byte) go together
                foreach (var shared in fields.GroupBy(f => f.Range))
                {
                    blocks.Add(CreateBlock([.. shared]));
                }
                break;

            case AddressUnit.Atomic:
                foreach (var atomic in _layout.AtomicGroups(span))
                {
                    var inside = fields.Where(f => atomic.Range.Contains(f.Range)).ToList();
                    if (inside.Count > 0)
                    {
                        blocks.Add(CreateBlock(inside));
                    }
                }
                break;

            default:
                var atomics = _layout.AtomicGroups(span).Select(a => a.Range).ToList();
                bool CanSplitAt(long address) => !atomics.Any(a => a.Start < address && address < a.End);

                var current = new List<AddressOccurrence>();
                foreach (var field in fields)
                {
                    if (current.Count > 0)
                    {
                        var start = current[0].Range.Start;
                        var end = current[^1].Range.End;
                        var gap = field.Range.Start > end;
                        var tooBig = message.MaxDataSize > 0 && field.Range.End - start > message.MaxDataSize && CanSplitAt(field.Range.Start);
                        if ((gap || tooBig) && field.Range.Start >= end)
                        {
                            blocks.Add(CreateBlock(current));
                            current = [];
                        }
                    }
                    current.Add(field);
                }
                if (current.Count > 0)
                {
                    blocks.Add(CreateBlock(current));
                }
                break;
        }
        return blocks;
    }

    /// <summary>
    /// A data message with the values of the block, as a complete SysEx message. The block must be one group
    /// occurrence of the map (e.g. a patch): the message body is written for that group.
    /// </summary>
    /// <param name="source">The values of the map (paths and instances of the map plan).</param>
    public byte[] CreateData(AddressMessage message, AddressBlock block, IValueSource source, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateData(message, block, device, client: null, _ => source);
    }

    /// <summary>
    /// A data message with the values of the block, written from the client's value model of the whole message
    /// (envelope and body, e.g. the instance of an editor): the address and size are those of the block, the device
    /// values come from <paramref name="device"/> or else from the client, and the body is the client's scope for the
    /// data open choice. The block must be one group occurrence of the map.
    /// </summary>
    internal byte[] CreateMessage(AddressMessage message, AddressBlock block, IValueSource source, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateData(message, block, device, source, body => body
            ?? throw new InvalidOperationException($"'{message.Name}': the value model has no body for the data of the message."));
    }

    private byte[] CreateData(AddressMessage message, AddressBlock block, DeviceValues device, IValueSource? client, Func<IValueSource?, IValueSource> bodySource)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(block);
        if (message.Kind != AddressMessageKind.Data)
        {
            throw new ArgumentException($"'{message.Name}' is not a data message.", nameof(message));
        }

        var target = GroupTargets(block.Addresses.Start, block.Addresses.Size)
            .FirstOrDefault(t => t.Block.Addresses == block.Addresses)
            ?? throw new NotSupportedException(
                $"The block {block.Addresses} ({block.Nodes.First.Path} to {block.Nodes.Last.Path}) is not one group of the map '{Root}'; a data message can only carry a group.");

        var envelope = new EnvelopeSource(block.Addresses, device,
            clientBody => new EnvelopeBody<IValueSource>(Root, target.Target, target.Instance, Wrap(bodySource(clientBody), target)), client);
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(message.Range, envelope, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// The blocks to transfer a node range with, and the message for each: a range message where one is given, else
    /// the atomic message for the atomic groups and the field message for the remaining fields (see <see cref="Split"/>).
    /// In address order.
    /// </summary>
    internal List<(AddressMessage Message, AddressBlock Block)> PlanBlocks(PlanRange range, IReadOnlyList<AddressMessage> messages)
    {
        if (messages.FirstOrDefault(m => m.Unit == AddressUnit.Range) is { } rangeMessage)
        {
            return [.. Split(range, rangeMessage).Select(b => (rangeMessage, b))];
        }

        var blocks = new List<(AddressMessage Message, AddressBlock Block)>();
        if (messages.FirstOrDefault(m => m.Unit == AddressUnit.Atomic) is { } atomicMessage)
        {
            blocks.AddRange(Split(range, atomicMessage).Select(b => (atomicMessage, b)));
        }
        if (messages.FirstOrDefault(m => m.Unit == AddressUnit.Field) is { } fieldMessage)
        {
            var covered = blocks.Select(t => t.Block.Addresses).ToList();
            blocks.AddRange(Split(range, fieldMessage)
                .Where(b => !covered.Any(c => c.Contains(b.Addresses)))
                .Select(b => (fieldMessage, b)));
        }
        return [.. blocks.OrderBy(t => t.Block.Addresses.Start)];
    }

    /// <summary>The number of items of a repeat of the map in the address space (<c>midi.repeat</c>, or its constant count); null for any other node.</summary>
    internal int? ItemCount(SchemaPath repeat) => _layout.Find(repeat) is { IsRepeat: true } layout ? layout.Count : null;

    /// <summary>
    /// The data message (of the messages that can carry this map) whose data is the open choice at
    /// <paramref name="dataChoice"/> in the plan of <paramref name="envelope"/>; null when there is none.
    /// </summary>
    internal AddressMessage? FindDataMessage(SchemaName envelope, SchemaPath dataChoice)
        => Protocol.DataMessages.FirstOrDefault(message =>
            message.Range.ExecutionPlan.Name is { } name
            && String.Equals(name.FullName, envelope.FullName, StringComparison.OrdinalIgnoreCase)
            && message.Roles.TryGetValue(MidiProperties.RoleData, out var data)
            && String.Equals(data.Path.Value, dataChoice.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Recognises a data message for this map and device: the constants and checksum match, the device values read
    /// select this map (of the groups that conform to the contract) and match <paramref name="device"/>, and the data
    /// is one group of the map. Returns false for any other message.
    /// </summary>
    public bool TryReadData(ReadOnlyMemory<byte> sysEx, DeviceValues device, out ReceivedData data)
    {
        ArgumentNullException.ThrowIfNull(device);
        foreach (var message in Protocol.DataMessages)
        {
            if (TryRead(message, sysEx, device) is { } received)
            {
                data = received;
                return true;
            }
        }
        data = null!;
        return false;
    }

    /// <summary>
    /// Copies the values of a node range from a source to a sink by writing and reading them with the map plan,
    /// so the sink gets exactly what would be sent (e.g. to merge the client's values into the current data).
    /// </summary>
    internal void CopyValues(PlanRange range, IValueSource source, IValueSink sink)
    {
        var items = MapRepeatItems.Of(_layout, range);
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(range, new MapRepeatSource(source, items), writer);
        _serializer.Deserialize(range, new ReadOnlySequence<byte>(writer.WrittenMemory), new MapRepeatSink(sink, items));
    }

    /// <summary>
    /// Wraps the scope of a data message body (the map) that is narrowed to a target group occurrence, so the repeats
    /// of the map, which have no count, end after the target (see <see cref="MapRepeatItems"/>).
    /// </summary>
    internal IValueSource WrapBody(IValueSource source, SchemaPath target, InstancePath instance)
        => new MapRepeatSource(source, MapRepeatItems.Of(_layout, target, instance));

    /// <inheritdoc cref="WrapBody(IValueSource, SchemaPath, InstancePath)"/>
    internal IValueSink WrapBody(IValueSink sink, SchemaPath target, InstancePath instance)
        => new MapRepeatSink(sink, MapRepeatItems.Of(_layout, target, instance));

    /// <summary>
    /// Wraps the scope of a read with the map plan itself (not through a data message, e.g. a dump of the map), so the
    /// repeats of the map end after <paramref name="range"/> (null: the whole map) or at the end of the data.
    /// </summary>
    internal IValueSink WrapMap(IValueSink sink, PlanRange? range)
        => new MapRepeatSink(sink, range is null ? MapRepeatItems.All(_layout) : MapRepeatItems.Of(_layout, range));

    /// <summary>
    /// The group occurrence the data of a message at the address is read into when the message is not recognised
    /// (<see cref="TryReadData"/>): the largest group that starts at the address; null when no group starts there.
    /// </summary>
    internal GroupTarget? LargestTargetAt(long address)
        => GroupTargets(address, Int64.MaxValue) is [.., var largest] ? largest : null;

    private IValueSource Wrap(IValueSource source, GroupTarget target) => WrapBody(source, target.Target, target.Instance);
    private IValueSink Wrap(IValueSink sink, GroupTarget target) => WrapBody(sink, target.Target, target.Instance);

    internal long Decode(ReceivedData data, IValueSink sink)
    {
        var target = data.Target;
        var envelope = new EnvelopeSink(_ => new EnvelopeBody<IValueSink>(Root, target.Target, target.Instance, Wrap(sink, target)));
        return _serializer.Deserialize(data.Message.Range, new ReadOnlySequence<byte>(data.SysEx), envelope);
    }

    // The length of the data is only known from the message as a whole: try the groups that start at the
    // address, largest first. A group that is too large runs out of data, one that is too small does not end
    // at the end of the message (checksum, end of exclusive). The trial reads discard the values.
    private ReceivedData? TryRead(AddressMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device)
    {
        List<GroupTarget>? candidates = null;
        for (var attempt = 0; ; attempt++)
        {
            var rejected = false;
            GroupTarget? tried = null;

            EnvelopeBody<IValueSink> Select(EnvelopeSink envelope)
            {
                if (envelope.Address is not { } address)
                {
                    rejected = true;
                    throw new InvalidOperationException($"'{message.Name}': the data comes before the address.");
                }
                if (!Matches(envelope.Device, device))
                {
                    rejected = true;
                    throw new InvalidOperationException("The message is for another device.");
                }

                candidates ??= [.. GroupTargets(address, sysEx.Length).OrderByDescending(t => t.Block.Addresses.Size)];
                if (attempt >= candidates.Count)
                {
                    rejected = true;
                    throw new InvalidOperationException("No group of the map fits the data.");
                }
                tried = candidates[attempt];
                return new EnvelopeBody<IValueSink>(Root, tried.Target, tried.Instance, Wrap(NullSink.Instance, tried));
            }

            try
            {
                var sink = new EnvelopeSink(Select);
                var read = _serializer.Deserialize(message.Range, new ReadOnlySequence<byte>(sysEx), sink);
                if (read == sysEx.Length && tried is not null)
                {
                    return new ReceivedData(this, message, sysEx, sink.Address!.Value, sink.Device, tried);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // not this message, not this device, or the tried group does not fit
            }

            if (rejected || candidates is null || attempt + 1 >= candidates.Count)
            {
                return null;
            }
        }
    }

    // the device values read must agree with the device and select this map
    private bool Matches(DeviceValues read, DeviceValues device)
    {
        foreach (var (name, value) in read)
        {
            if (device.TryGet(name, out var expected) && expected != value)
            {
                return false;
            }
        }

        try
        {
            return String.Equals(Protocol.SelectBody(read).FullName, Root.FullName, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The group occurrences that start at the address and are at most <paramref name="maxSize"/> addresses long,
    /// smallest first: the targets a data message at that address can carry.
    /// </summary>
    internal IReadOnlyList<GroupTarget> GroupTargets(long address, long maxSize)
    {
        var targets = new List<GroupTarget>();
        var first = _layout.Fields(new AddressRange(address, 1)).FirstOrDefault(f => f.Range.Start == address);
        if (first.Layout is null)
        {
            return targets;
        }

        for (var node = first.Layout.Parent; node is not null; node = node.Parent)
        {
            if (node.IsRepeat)
            {
                continue;
            }

            var depth = AddressLayout.RepeatDepth(node);
            var instance = new InstancePath(first.Instance.AsSpan()[..depth].ToArray());
            if (AddressLayout.FirstField(node) is not { } firstField || AddressLayout.LastField(node) is not { } lastField
                || _layout.AddressOf(firstField, instance, lastItem: false) != address)
            {
                break;
            }

            var end = _layout.AddressOf(lastField, instance, lastItem: true) + lastField.Size;
            if (end - address > maxSize)
            {
                break;
            }
            if (Resolve(new AddressRange(address, end - address)).Block is { } block)
            {
                targets.Add(new GroupTarget(node.Node.Path, instance, block));
            }
        }
        return targets;
    }

    private AddressBlock CreateBlock(IReadOnlyList<AddressOccurrence> fields)
    {
        var first = fields[0];
        var last = fields[^1];
        var nodes = Plan.CreateRange(first.Node.Path, first.Instance, last.Node.Path, last.Instance);
        var start = first.Range.Start;
        var end = fields.Max(f => f.Range.End);
        return new AddressBlock(new AddressRange(start, end - start), nodes);
    }

    private AddressLayoutNode LayoutOf(FieldInfo field)
        => _layout.Find(field) ?? throw new ArgumentException($"'{field.Path}' has no address in the map '{Root}'.", nameof(field));
}

/// <summary>
/// A group occurrence of a map that a data message can carry: the target path and instance inside the map
/// (the open choice body) and the block of its fields.
/// </summary>
internal sealed record GroupTarget(SchemaPath Target, InstancePath Instance, AddressBlock Block);

/// <summary>
/// A data message that was recognised for a map: its address, the device values in it and the block of the map it
/// carries. Call <see cref="Decode"/> to read its values into a value model.
/// </summary>
public sealed class ReceivedData
{
    private readonly AddressMap _map;

    internal ReceivedData(AddressMap map, AddressMessage message, ReadOnlyMemory<byte> sysEx, long address, DeviceValues device, GroupTarget target)
    {
        _map = map;
        Message = message;
        SysEx = sysEx;
        Address = address;
        Device = device;
        Target = target;
    }

    public AddressMessage Message { get; }
    public ReadOnlyMemory<byte> SysEx { get; }
    public long Address { get; }
    /// <summary>The device values read from the message.</summary>
    public DeviceValues Device { get; }
    /// <summary>The addresses and nodes of the map the message carries.</summary>
    public AddressBlock Block => Target.Block;

    internal GroupTarget Target { get; }

    /// <summary>Reads the values of the message into the value model (paths and instances of the map plan).</summary>
    public long Decode(IValueSink sink) => _map.Decode(this, sink);
}
