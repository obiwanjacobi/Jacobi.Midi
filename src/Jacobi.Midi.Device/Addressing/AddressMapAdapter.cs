using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// A node occurrence in an address map: the path of a group, repeat or field of the map plan and the item indices of
/// the repeats around it (outermost first). For a repeat, one index more than the repeats around it selects one item.
/// </summary>
public readonly record struct MapOccurrence(SchemaPath Path, InstancePath Instance);

/// <summary>
/// How a selection of map nodes is transferred: the data message that carries them, the address space of the map
/// (to format the addresses) and the blocks, one message each, in address order.
/// </summary>
public sealed record DataPlan(AddressMessage Message, AddressSpace AddressSpace, IReadOnlyList<AddressBlock> Blocks);

/// <summary>One data message that was written: the message, the block of the map it carries and its SysEx bytes.</summary>
public sealed record EncodedDataMessage(AddressMessage Message, AddressBlock Block, byte[] SysEx);

/// <summary>
/// Lets a client that reads and writes arbitrary messages with its own value model (e.g. the Schema Editor) work with
/// the address maps in the schemas of a serializer: the repeats of a map, which have no count (see
/// <see cref="MapRepeatItems"/>), get their item count from the address space and end there, the data of a message is
/// placed in the map by its address, and a selection of map nodes is written as the data messages that carry it,
/// with the address and size calculated from the map. Without address maps in the schemas the reads are plain
/// serializer reads.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A read with the plan of a map root (e.g. a dump of the map) ends the repeats of the map after the items of the
/// address space, or at the end of the data.</item>
/// <item>A read of a message whose open choice gets a map root as its body (selected by the client's value model) reads the
/// group occurrence the message carries, recognised as <see cref="AddressMap.TryReadData"/> does (with any device id).
/// When the message is not recognised, the largest group that starts at the address is read as far as the data goes,
/// with a warning.</item>
/// <item>A write of a selection (<see cref="WriteData"/>) is split into blocks as the message exchange does
/// (<c>midi.unit</c>, maximum data size, atomic groups); each block is one message. The client's value model supplies
/// the whole message: the envelope (device values the map root does not fix) and the body.</item>
/// </list>
/// </remarks>
public sealed class AddressMapAdapter
{
    private readonly Serializer _serializer;
    private readonly AddressMaps _maps;
    private readonly IReadOnlyList<SchemaName> _mapRoots;

    public AddressMapAdapter(Serializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;
        _maps = new AddressMaps(serializer);
        _mapRoots = AddressProtocol.FindMapRoots(serializer.Schemas);
    }

    /// <summary>True when the schemas hold an address map; without one the reads are plain serializer reads.</summary>
    public bool HasMaps => _mapRoots.Count > 0;

    /// <summary>The address map roots of the schemas: the groups a data message can carry (see <see cref="IsMap"/>).</summary>
    public IReadOnlyList<SchemaName> MapRoots => _mapRoots;

    /// <summary>Reads the input with a whole plan into the sink.</summary>
    /// <param name="warnings">Receives the situations the user may not expect, also when the read fails.</param>
    /// <returns>The number of bytes read.</returns>
    /// <exception cref="AddressMapException">The map that is read is not valid.</exception>
    public long Deserialize(ExecutionPlan plan, ReadOnlySequence<byte> input, IValueSink sink, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _serializer.Deserialize(plan, input, Wrap(plan, null, input, sink, warnings));
    }

    /// <summary>Reads the input with a range of a plan into the sink.</summary>
    /// <inheritdoc cref="Deserialize(ExecutionPlan, ReadOnlySequence{byte}, IValueSink, ICollection{string})"/>
    public long Deserialize(PlanRange range, ReadOnlySequence<byte> input, IValueSink sink, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(range);
        return _serializer.Deserialize(range, input, Wrap(range.ExecutionPlan, range, input, sink, warnings));
    }

    private IValueSink Wrap(ExecutionPlan plan, PlanRange? range, ReadOnlySequence<byte> input, IValueSink sink, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(warnings);
        if (!HasMaps)
        {
            return sink;
        }
        if (plan.Name is { } name && IsMapRoot(name))
        {
            return _maps.Get(plan).WrapMap(sink, range);
        }
        var body = new BodySelector(this, input.ToArray(), warnings);
        return new MapBodySink(sink, body, new EnvelopeSink(_ => throw new InvalidOperationException("The envelope values are only kept here.")));
    }

    /// <summary>True when the group is an address map root: a group a data message can carry.</summary>
    public bool IsMap(SchemaName group) => IsMapRoot(group);

    /// <summary>
    /// The number of items of a repeat of a map in the address space (<c>midi.repeat</c>, or its constant count);
    /// null for a node that is not a repeat of the map.
    /// </summary>
    /// <param name="map">The map root (see <see cref="IsMap"/>).</param>
    /// <param name="repeat">The path of the repeat in the map plan.</param>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    public int? ItemCount(SchemaName map, SchemaPath repeat) => MapOf(map).ItemCount(repeat);

    /// <summary>
    /// How the selected nodes of a map are transferred with the data message whose data is the open choice
    /// <paramref name="dataChoice"/> in the plan of <paramref name="envelope"/>: the blocks (one message each) with their
    /// addresses. A selection inside an atomic group is widened to the whole group (with a warning).
    /// </summary>
    /// <param name="warnings">Receives the situations the user may not expect.</param>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    /// <exception cref="InvalidOperationException">The open choice is not the data of a message that can carry the map.</exception>
    public DataPlan PlanData(SchemaName envelope, SchemaPath dataChoice, SchemaName map, IEnumerable<MapOccurrence> selection, ICollection<string> warnings)
        => Plan(envelope, dataChoice, map, selection, warnings).Plan;

    /// <summary>
    /// Writes the selected nodes of a map as the data messages of <see cref="PlanData"/>. The <paramref name="source"/> is
    /// the client's value model of the whole message (the plan of <paramref name="envelope"/>): the address and size come
    /// from the block, the device values from the map root or else from the source, and the body values from the source's
    /// scope for the data open choice (paths of the map plan).
    /// </summary>
    /// <param name="warnings">Receives the situations the user may not expect.</param>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    /// <exception cref="InvalidOperationException">The open choice is not the data of a message that can carry the map, or a value is missing.</exception>
    /// <exception cref="NotSupportedException">A block is not one group of the map (a data message can only carry a group).</exception>
    public IReadOnlyList<EncodedDataMessage> WriteData(SchemaName envelope, SchemaPath dataChoice, SchemaName map,
        IEnumerable<MapOccurrence> selection, IValueSource source, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (addressMap, plan) = Plan(envelope, dataChoice, map, selection, warnings);
        return [.. plan.Blocks.Select(block => new EncodedDataMessage(plan.Message, block,
            addressMap.CreateMessage(plan.Message, block, source, addressMap.Device)))];
    }

    /// <summary>
    /// Decodes the data messages of a map in <paramref name="messages"/> (e.g. a dump read from a .syx file) into the
    /// sink, in order, as the message exchange decodes what it receives: a message is the data of the map when it is
    /// recognised for the device (<see cref="AddressMap.TryReadData"/>). Other messages are skipped with a warning.
    /// </summary>
    /// <param name="map">The map root (see <see cref="IsMap"/>).</param>
    /// <param name="device">The device values the map root does not fix (e.g. <c>deviceId</c>).</param>
    /// <param name="sink">The value model: paths and instances of the map plan.</param>
    /// <param name="warnings">Receives the messages that were skipped.</param>
    /// <returns>The number of messages decoded.</returns>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    /// <exception cref="InvalidOperationException">A device value the messages need is not given.</exception>
    public int ReadData(SchemaName map, IEnumerable<ReadOnlyMemory<byte>> messages, DeviceValues device, IValueSink sink, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(warnings);
        var addressMap = MapOf(map);
        var values = addressMap.DeviceFor(device);

        var decoded = 0;
        var index = 0;
        foreach (var message in messages)
        {
            index++;
            if (addressMap.TryReadData(message, values, out var data))
            {
                data.Decode(sink);
                decoded++;
            }
            else
            {
                warnings.Add($"Message {index} ({message.Length} bytes) is not data of '{addressMap.Root}' for this device; it is skipped.");
            }
        }
        return decoded;
    }

    /// <summary>
    /// Encodes a node range of a map as the data messages that transfer it, as the message exchange sends it: widened
    /// to the atomic groups it overlaps and split into blocks for the data messages whose device values are known.
    /// The source must hold the values of the whole widened range.
    /// </summary>
    /// <param name="range">A range of the map plan.</param>
    /// <param name="source">The value model: paths and instances of the map plan.</param>
    /// <param name="device">The device values the map root does not fix (e.g. <c>deviceId</c>).</param>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    /// <exception cref="InvalidOperationException">A device value the messages need is not given, or the source has no value for a field.</exception>
    /// <exception cref="NotSupportedException">A block is not one group of the map (a data message can only carry a group).</exception>
    public IReadOnlyList<EncodedDataMessage> EncodeData(PlanRange range, IValueSource source, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(device);
        var addressMap = _maps.Get(range);
        var values = addressMap.DeviceFor(device);
        var widened = addressMap.WidenToAtomic(range);
        return [.. addressMap.SelectBlocks(widened, addressMap.Protocol.DataMessages, values)
            .Select(planned => new EncodedDataMessage(planned.Message, planned.Block,
                addressMap.CreateData(planned.Message, planned.Block, source, values)))];
    }

    private (AddressMap Map, DataPlan Plan) Plan(SchemaName envelope, SchemaPath dataChoice, SchemaName map,
        IEnumerable<MapOccurrence> selection, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(warnings);
        var addressMap = MapOf(map);
        var message = addressMap.FindDataMessage(envelope, dataChoice) ?? throw new InvalidOperationException(
            $"'{dataChoice}' of '{envelope}' is not the data of a data message that can carry the map '{addressMap.Root}'.");

        var blocks = new List<AddressBlock>();
        foreach (var occurrence in selection)
        {
            var range = addressMap.CreateOccurrenceRange(occurrence.Path, occurrence.Instance);
            var widened = addressMap.WidenToAtomic(range);
            if (addressMap.GetAddressRange(widened) != addressMap.GetAddressRange(range))
            {
                warnings.Add($"'{occurrence.Path}' {occurrence.Instance.ToLogString()} is part of an atomic group of '{addressMap.Root}': the whole group is sent.");
            }
            foreach (var (_, block) in addressMap.PlanBlocks(widened, [message]))
            {
                if (!blocks.Any(existing => existing.Addresses.Contains(block.Addresses)))
                {
                    blocks.Add(block);
                }
            }
        }
        return (addressMap, new DataPlan(message, addressMap.Protocol.AddressSpace, [.. blocks.OrderBy(block => block.Addresses.Start)]));
    }

    /// <summary>
    /// How the value of an envelope node with <paramref name="role"/> (<c>midi.role</c>) is supplied when a data message
    /// carries <paramref name="map"/>: a short description, or null when the client's value model supplies it (a device
    /// value the map root does not fix, or not a role).
    /// </summary>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    public string? DescribeRole(SchemaName map, string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (String.Equals(role, MidiProperties.RoleAddress, StringComparison.OrdinalIgnoreCase))
        {
            return "address: calculated from the map";
        }
        if (String.Equals(role, MidiProperties.RoleSize, StringComparison.OrdinalIgnoreCase))
        {
            return "size: calculated from the map";
        }
        if (String.Equals(role, MidiProperties.RoleData, StringComparison.OrdinalIgnoreCase))
        {
            return "data: the included groups of the map";
        }
        if (role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase)
            && MapOf(map).Device.TryGet(role[MidiProperties.RoleDevicePrefix.Length..], out var value))
        {
            return $"{role}: 0x{value:X2}, fixed by '{map}'";
        }
        return null;
    }

    private AddressMap MapOf(SchemaName map)
        => IsMapRoot(map)
            ? _maps.Get(_serializer.GetPlan(map))
            : throw new ArgumentException($"'{map}' is not an address map: no data message can carry it.", nameof(map));

    private bool IsMapRoot(SchemaName name)
        => _mapRoots.Any(root => String.Equals(root.FullName, name.FullName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Narrows the body of an open choice that the client selected to the group occurrence of the map the message carries,
    /// and wraps its scope so the repeats of the map end after it.
    /// </summary>
    private sealed class BodySelector(AddressMapAdapter reader, byte[] message, ICollection<string> warnings)
    {
        public OpenChoiceSinkResult Select(OpenChoiceContext context, OpenChoiceSinkResult selected, EnvelopeSink envelope)
        {
            if (!reader.IsMapRoot(selected.Group))
            {
                return selected;
            }

            var map = reader._maps.Get(reader._serializer.GetPlan(selected.Group));
            if (!selected.Target.IsEmpty)
            {
                // the client narrowed the body itself
                return selected with { Scope = map.WrapBody(selected.Scope, selected.Target, selected.TargetInstance) };
            }

            var target = Recognize(map, context) ?? Fallback(map, context, envelope);
            return new OpenChoiceSinkResult(map.WrapBody(selected.Scope, target.Target, target.Instance), selected.Group, target.Target, target.Instance);
        }

        // the group the message carries, when it is a data message of the map and this open choice is its data
        private GroupTarget? Recognize(AddressMap map, OpenChoiceContext context)
        {
            // [TODO] any device id is accepted for now; only the device values the map root fixes (model id) select it
            if (!map.TryReadData(message, new DeviceValues(), out var data)
                || !data.Message.Roles.TryGetValue(MidiProperties.RoleData, out var dataNode)
                || !String.Equals(dataNode.Path.Value, context.Node.Path.Value, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return data.Target;
        }

        // read as much as possible: the largest group at the address, until the data runs out
        private GroupTarget Fallback(AddressMap map, OpenChoiceContext context, EnvelopeSink envelope)
        {
            if (envelope.Address is not { } address)
            {
                throw new InvalidOperationException(
                    $"'{context.Path}': no address was read before the data, so the data cannot be placed in the map '{map.Root}'.");
            }
            var target = map.LargestTargetAt(address) ?? throw new InvalidOperationException(
                $"'{context.Path}': no group of the map '{map.Root}' starts at the address {map.Protocol.AddressSpace.Format(address)}.");
            warnings.Add($"'{context.Path}': the message is not recognised as one whole group of the map '{map.Root}' " +
                $"(another device model, or the data is incomplete or spans more than one group). " +
                $"It is read into '{target.Target}' at {map.Protocol.AddressSpace.Format(address)} as far as the data goes.");
            return target;
        }
    }

    /// <summary>
    /// Wraps the client's value sink for reading a message that may carry an address map in an open choice: forwards all
    /// values, keeps the address read (<see cref="EnvelopeSink"/>) and lets the <see cref="BodySelector"/> narrow a body
    /// that is a map root. The client's open repeats are still ended by the client (<see cref="IOpenRepeatSink"/>).
    /// </summary>
    private sealed class MapBodySink(IValueSink inner, BodySelector body, EnvelopeSink envelope) : IOpenChoiceSink, IOpenRepeatSink
    {
        public void SetField(FieldContext context, LogicalField value)
        {
            envelope.SetField(context, value);
            inner.SetField(context, value);
        }

        public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
        {
            if (inner is not IOpenChoiceSink openChoice)
            {
                throw new InvalidOperationException($"'{context.Path}': the value model does not support open choices.");
            }
            var selected = openChoice.EnterOpenChoice(context);
            return body.Select(context, selected with { Scope = Wrap(selected.Scope) }, envelope);
        }

        public OpenRepeatAction NextItem(OpenRepeatReadContext context)
            => inner is IOpenRepeatSink openSink ? openSink.NextItem(context) : OpenRepeatAction.Default;

        public IValueSink EnterGroup(GroupContext context) => Wrap(inner.EnterGroup(context));
        public IValueSink EnterItem(RepeatContext context, int index, int count) => Wrap(inner.EnterItem(context, index, count));
        public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => Wrap(inner.EnterChoice(context, selectedIndex));
        public void ExitGroup(GroupContext context) => inner.ExitGroup(context);
        public void ExitItem(RepeatContext context, int index, int count) => inner.ExitItem(context, index, count);
        public void ExitChoice(ChoiceContext context, int selectedIndex) => inner.ExitChoice(context, selectedIndex);

        private MapBodySink Wrap(IValueSink scope) => new(scope, body, envelope);
    }
}
