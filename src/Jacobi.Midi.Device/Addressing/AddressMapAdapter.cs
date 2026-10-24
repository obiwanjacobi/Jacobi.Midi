using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Reads data with any plan of a serializer into the client's value model and lets the address maps in the schemas
/// end their repeats, which have no count (see <see cref="MapRepeatItems"/>). For tools that read arbitrary messages
/// into their own value model, e.g. the Schema Editor. Without address maps in the schemas it reads like the serializer.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A read with the plan of a map root (e.g. a dump of the map) ends the repeats of the map after the items of the
/// address space, or at the end of the data.</item>
/// <item>A read of a message whose open choice gets a map root as its body (selected by the client's value model) reads the
/// group occurrence the message carries, recognised as <see cref="AddressMap.TryReadData"/> does (with any device id).
/// When the message is not recognised, the largest group that starts at the address is read as far as the data goes,
/// with a warning.</item>
/// </list>
/// </remarks>
public sealed class AddressMapReader
{
    private readonly Serializer _serializer;
    private readonly AddressMaps _maps;
    private readonly IReadOnlyList<SchemaName> _mapRoots;

    public AddressMapReader(Serializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;
        _maps = new AddressMaps(serializer);
        _mapRoots = AddressProtocol.FindMapRoots(serializer.Schemas);
    }

    /// <summary>True when the schemas hold an address map; without one the reads are plain serializer reads.</summary>
    public bool HasMaps => _mapRoots.Count > 0;

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

    private bool IsMapRoot(SchemaName name)
        => _mapRoots.Any(root => String.Equals(root.FullName, name.FullName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Narrows the body of an open choice that the client selected to the group occurrence of the map the message carries,
    /// and wraps its scope so the repeats of the map end after it.
    /// </summary>
    private sealed class BodySelector(AddressMapReader reader, byte[] message, ICollection<string> warnings)
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
