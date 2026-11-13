using System.Buffers;
using System.Collections.Concurrent;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The message maps of the roots of a serializer's schemas: the map of a plan is created on first use by the first
/// provider of a fixed, ordered chain that claims the root, and cached per plan, so a client only needs a
/// <see cref="PlanRange"/>. <see cref="Prepare"/> and <see cref="PrepareAll"/> create and validate maps up front so
/// schema errors surface early; <see cref="Validate"/> checks a transfer before anything is sent. Thread-safe.
/// A client outside Midi.Device sees a map through <see cref="Info"/>: what it needs to know about a root, whatever
/// its kind (address map or dump).
/// </summary>
public sealed class MessageMaps(Serializer serializer)
{
    // the providers in the order they get to claim a root
    private static readonly IReadOnlyList<IMessageMapProvider> Providers = [new AddressMapProvider(), new DumpMapProvider()];

    private readonly ConcurrentDictionary<ExecutionPlan, Lazy<IMessageMap>> _maps = new(ReferenceEqualityComparer.Instance);

    public Serializer Serializer { get; } = serializer ?? throw new ArgumentNullException(nameof(serializer));

    /// <summary>The map of the plan the range belongs to; created and validated on first use.</summary>
    internal IMessageMap Get(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return Get(range.ExecutionPlan);
    }

    /// <summary>The map of a plan; created and validated on first use.</summary>
    /// <exception cref="InvalidOperationException">No provider claims the root.</exception>
    internal IMessageMap Get(ExecutionPlan plan)
        => _maps.GetOrAdd(plan, p => new Lazy<IMessageMap>(() => Create(p))).Value;

    /// <summary>What a client needs to know about the message map of a root; the map is created and validated on first use.</summary>
    /// <exception cref="MessageMapException">The map of the root is not valid.</exception>
    /// <exception cref="InvalidOperationException">No provider claims the root.</exception>
    public MessageMapInfo Info(SchemaName root)
        => new(Get(Serializer.GetPlan(root)));

    /// <summary>Creates and validates the map of a root; throws the provider's exception when it is not valid.</summary>
    public void Prepare(SchemaName root)
        => Get(Serializer.GetPlan(root));

    /// <summary>
    /// Creates and validates the maps of all roots a data message can carry; throws the provider's exception for the
    /// first one that is not valid.
    /// </summary>
    public void PrepareAll()
    {
        foreach (var root in DeviceMessages.FindCarriedRoots(Serializer.Schemas))
        {
            Prepare(root);
        }
    }

    /// <summary>
    /// The root a data message of the device carries: the first root carried by <paramref name="message"/> whose map
    /// recognises the SysEx message for the device values (e.g. the body an open choice selects by the model in the
    /// message, or the address map of the model). Null when no root's map recognises it; a root whose map is not valid
    /// is not tried.
    /// </summary>
    /// <param name="message">The schema name of the data message, e.g. <c>Roland.Message16.DT1</c>.</param>
    /// <param name="device">The device values to match; a value that is not given matches any value in the message.</param>
    public SchemaName? FindCarriedRoot(SchemaName message, ReadOnlyMemory<byte> sysEx, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(device);
        foreach (var map in CarriedMaps())
        {
            var data = map.DataMessages.FirstOrDefault(candidate => String.Equals(candidate.FullName, message.FullName, StringComparison.OrdinalIgnoreCase));
            if (data is not null && map.Match(data, sysEx, map.Device.With(device)).IsRecognised)
            {
                return map.Root;
            }
        }
        return null;
    }

    /// <summary>
    /// The data message a SysEx message is, and the root it carries: the first data message of a carried root's map
    /// that recognises it for the device values (e.g. <c>Roland.Message32.DT1</c> carrying <c>Gx700.AddressMap</c>).
    /// Null when no map recognises it; a root whose map is not valid is not tried.
    /// </summary>
    /// <param name="device">The device values to match; a value that is not given matches any value in the message.</param>
    public (SchemaName Message, SchemaName Root)? FindDataMessage(ReadOnlyMemory<byte> sysEx, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(device);
        foreach (var map in CarriedMaps())
        {
            var values = map.Device.With(device);
            foreach (var data in map.DataMessages)
            {
                if (map.Match(data, sysEx, values).IsRecognised)
                {
                    return (new SchemaName(data.FullName), map.Root);
                }
            }
        }
        return null;
    }

    // the maps of the roots a data message can carry; a root whose map is not valid is left out
    private IEnumerable<IMessageMap> CarriedMaps()
    {
        foreach (var root in DeviceMessages.FindCarriedRoots(Serializer.Schemas))
        {
            IMessageMap map;
            try
            {
                map = Get(Serializer.GetPlan(root));
            }
            catch (Exception exception) when (exception is MessageMapException or ExecutionPlanException or InvalidOperationException)
            {
                continue;
            }
            yield return map;
        }
    }

    /// <summary>
    /// Reads the data of a root from SysEx messages (e.g. a dump file) into the sink, in order, without a connection or
    /// exchange file: a message is decoded when it is a data message of the root's map for the device, as the message
    /// exchange recognises what it receives. Other messages are skipped with a warning (with the reason for a near miss).
    /// </summary>
    /// <param name="device">The device values the root does not fix (e.g. <c>deviceId</c>).</param>
    /// <param name="sink">The value model: paths and instances of the root's plan.</param>
    /// <param name="warnings">Receives the messages that were skipped.</param>
    /// <returns>The number of messages decoded.</returns>
    /// <exception cref="MessageMapException">The map of the root is not valid.</exception>
    /// <exception cref="InvalidOperationException">A device value the data messages need is not given.</exception>
    public int Load(SchemaName root, IEnumerable<ReadOnlyMemory<byte>> messages, DeviceValues device, IValueSink sink, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(warnings);
        var map = Get(Serializer.GetPlan(root));
        var values = DeviceFor(map, device);

        var decoded = 0;
        var index = 0;
        foreach (var sysEx in messages)
        {
            index++;
            string? problem = null;
            ReceivedData? data = null;
            foreach (var message in map.DataMessages)
            {
                var match = map.Match(message, sysEx, values);
                if (match.IsRecognised)
                {
                    data = match.Data;
                    break;
                }
                problem ??= match.Problem;
            }

            if (data is not null)
            {
                data.Decode(sink);
                decoded++;
            }
            else
            {
                warnings.Add(problem is null
                    ? $"Message {index} ({sysEx.Length} bytes) is not data of '{map.Root}' for this device; it is skipped."
                    : $"Message {index} ({sysEx.Length} bytes) is not read: {problem}");
            }
        }
        return decoded;
    }

    /// <summary>
    /// Reads one data message into a model of the whole message (envelope and body, scope by scope; e.g. the instance
    /// of an editor): the mirror of <see cref="SaveMessages"/>. The map of <paramref name="root"/> recognises the
    /// message, and the body the model selects for the data open choice is narrowed to the block the message carries
    /// (e.g. one patch of an address map, whose repeats end there; the whole root of a dump).
    /// </summary>
    /// <param name="root">The root the body of the data open choice is (the model must select it).</param>
    /// <param name="message">The data message (its schema name, e.g. <c>Roland.Message16.DT1</c>).</param>
    /// <param name="sink">The model of the whole message: paths and instances of the data message's plan.</param>
    /// <param name="device">The device values to match; a value that is not given matches any value in the message.</param>
    /// <returns>The number of bytes read.</returns>
    /// <exception cref="MessageMapException">The map of the root is not valid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is not a data message of the root, the map does not recognise the SysEx message (with the reason
    /// of a near miss), or the model selects another body.
    /// </exception>
    public long ReadMessage(SchemaName root, SchemaName message, ReadOnlyMemory<byte> sysEx, IValueSink sink, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(device);
        var map = Get(Serializer.GetPlan(root));
        var data = map.Message(message);
        if (!map.IsData(data) || !data.Roles.TryGetValue(MidiProperties.RoleData, out var dataChoice))
        {
            throw new InvalidOperationException($"'{message}' is not a data message of '{map.Root}': it cannot carry its values.");
        }

        var match = map.Match(data, sysEx, map.Device.With(device));
        if (match.Data is not { } received)
        {
            throw new InvalidOperationException(match.Problem is { } problem
                ? $"The message is not read as '{data.FullName}' with '{map.Root}': {problem}"
                : $"The message is not a '{data.FullName}' with data of '{map.Root}' for this device.");
        }

        var block = received.Block;
        var whole = ReferenceEquals(block.First, block.ExecutionPlan.Root) && ReferenceEquals(block.Last, block.ExecutionPlan.Root);
        OpenChoiceSinkResult Narrow(OpenChoiceSinkResult selected)
        {
            if (selected.Group != map.Root)
            {
                throw new InvalidOperationException($"'{dataChoice.Path}': the selected body '{selected.Group}' is not '{map.Root}'.");
            }
            var scope = map.WrapBody(selected.Scope, block);
            return whole
                ? selected with { Scope = scope }
                : new OpenChoiceSinkResult(scope, selected.Group, block.First.Path, block.FromInstance, block.Last.Path, block.ToInstance);
        }
        return Serializer.Deserialize(data.Range, new ReadOnlySequence<byte>(sysEx), new ClientBodySink(sink, dataChoice.Path, Narrow));
    }

    /// <summary>
    /// How ranges of a root are transferred with a data message, without writing anything: the blocks the map splits
    /// them into (widened where the map requires it, e.g. to the atomic groups of an address map or the whole root of a
    /// dump; a block that another one holds is planned once), in the order the map transfers them, each with a
    /// description for a person (e.g. its address and size).
    /// </summary>
    /// <param name="ranges">Ranges of the root's plan (one root).</param>
    /// <param name="device">The device values the root does not fix (e.g. <c>deviceId</c>); only used to choose the data message.</param>
    /// <param name="message">The data message (its schema name, e.g. <c>Roland.Message16.DT1</c>); null: the first data message of the map.</param>
    /// <param name="warnings">Receives the ranges that are widened.</param>
    /// <exception cref="MessageMapException">The map of the root is not valid.</exception>
    /// <exception cref="InvalidOperationException">The message is not a data message of the root, or a range cannot be split for it.</exception>
    public TransferPlan Plan(IEnumerable<PlanRange> ranges, DeviceValues device, SchemaName? message = null, ICollection<string>? warnings = null)
    {
        var (map, data, _, blocks) = PlanBlocks(ranges, device, message, requireDevice: false, warnings);
        return new TransferPlan(data, [.. blocks.Select(block => new PlannedBlock(block, map.DescribeBlock(block)))]);
    }

    /// <summary>
    /// Writes ranges of a root as the SysEx messages that transfer them, without a connection or exchange file (e.g. a
    /// dump file), as the message exchange sends them: one message per block of <see cref="Plan"/>. The values come
    /// from a model of the root's values (as the message exchange uses them); the device values from
    /// <paramref name="device"/> and the root.
    /// </summary>
    /// <param name="ranges">Ranges of the root's plan (one root).</param>
    /// <param name="source">The model of the root's values: paths and instances of the root's plan; it must hold the values of the whole (widened) blocks.</param>
    /// <param name="device">The device values the root does not fix (e.g. <c>deviceId</c>).</param>
    /// <param name="message">
    /// The data message to write (its schema name, e.g. <c>Roland.Message16.DT1</c>); null: the first data message of
    /// the map whose device values are known.
    /// </param>
    /// <param name="warnings">Receives the ranges that are widened.</param>
    /// <returns>The messages, in the order of the blocks.</returns>
    /// <exception cref="MessageMapException">The map of the root is not valid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is not a data message of the root, no data message can be written for the device values, a device
    /// value is missing, or the source has no value for a field.
    /// </exception>
    public IReadOnlyList<byte[]> Save(IEnumerable<PlanRange> ranges, IValueSource source, DeviceValues device, SchemaName? message = null, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (map, data, values, blocks) = PlanBlocks(ranges, device, message, requireDevice: true, warnings);
        return [.. blocks.Select(block => map.Write(data, block, source, values))];
    }

    /// <summary>
    /// <see cref="Save"/> with the values from a model of the whole message (envelope and body, scope by scope; e.g.
    /// the instance of an editor): the map supplies the roles of each block (e.g. its address and size) and the device
    /// values the root fixes; the model the other device values, the other envelope fields and the body (its scope
    /// for the data open choice; for a root that is a message itself, the root's values).
    /// </summary>
    /// <param name="ranges">Ranges of the root's plan (one root).</param>
    /// <param name="source">The model of the whole message: paths and instances of the data message's plan.</param>
    /// <param name="device">Device values over those of the model (e.g. none: the model has them).</param>
    /// <param name="message">The data message to write; null: the first data message of the map.</param>
    /// <param name="warnings">Receives the ranges that are widened.</param>
    /// <returns>The messages, in the order of the blocks.</returns>
    /// <exception cref="MessageMapException">The map of the root is not valid.</exception>
    /// <exception cref="InvalidOperationException">The message is not a data message of the root, or a value is missing.</exception>
    public IReadOnlyList<byte[]> SaveMessages(IEnumerable<PlanRange> ranges, IValueSource source, DeviceValues device, SchemaName? message = null, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (map, data, values, blocks) = PlanBlocks(ranges, device, message, requireDevice: false, warnings);
        return [.. blocks.Select(block => map.WriteMessage(data, block, source, values))];
    }

    // the map of the ranges' root, the data message, the device values (the root's with the client's over them) and
    // the blocks: split per range, a block that another one holds once, in the map's order
    private (IMessageMap Map, DeviceMessage Data, DeviceValues Values, IReadOnlyList<PlanRange> Blocks) PlanBlocks(
        IEnumerable<PlanRange> ranges, DeviceValues device, SchemaName? message, bool requireDevice, ICollection<string>? warnings)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(device);
        var list = ranges.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("There is no range to transfer.", nameof(ranges));
        }
        var map = Get(list[0]);
        if (list.Any(range => !ReferenceEquals(range.ExecutionPlan, list[0].ExecutionPlan)))
        {
            throw new ArgumentException($"The ranges are not all of the plan of '{map.Root}'.", nameof(ranges));
        }
        var values = map.Device.With(device);
        var data = DataMessage(map, message, values, requireDevice);

        var blocks = new List<PlanRange>();
        foreach (var range in list)
        {
            if (map.Validate(range, data) is [var problem, ..])
            {
                throw new InvalidOperationException(problem);
            }
            foreach (var block in map.Split(range, data))
            {
                if (!range.Contains(block))
                {
                    warnings?.Add($"'{range.First.Path}' {range.FromInstance.ToLogString()} is part of a larger block of '{map.Root}': {map.DescribeBlock(block)} is sent.");
                }
                if (!blocks.Any(planned => planned.Contains(block)))
                {
                    blocks.RemoveAll(block.Contains);
                    blocks.Add(block);
                }
            }
        }
        return (map, data, values, map.Order(blocks));
    }

    // the given data message, or the first one (whose device values are known, when they are required)
    private static DeviceMessage DataMessage(IMessageMap map, SchemaName? message, DeviceValues values, bool requireDevice)
    {
        DeviceMessage data;
        if (message is { } name)
        {
            data = map.Message(name);
            if (!map.IsData(data))
            {
                throw new InvalidOperationException($"'{name}' is not a data message of '{map.Root}': it cannot carry its values.");
            }
        }
        else if (requireDevice)
        {
            data = map.DataMessages.FirstOrDefault(candidate => DeviceValues.Missing([candidate], values).Count == 0)
                ?? throw new InvalidOperationException($"No data message of '{map.Root}' can be written: the device values " +
                    $"{String.Join(", ", DeviceValues.Missing(map.DataMessages, values))} are required; supply them with the device.");
        }
        else
        {
            data = map.DataMessages.FirstOrDefault()
                ?? throw new InvalidOperationException($"'{map.Root}' has no data message.");
        }

        if (requireDevice && DeviceValues.Missing([data], values) is { Count: > 0 } missing)
        {
            throw new InvalidOperationException($"The device values {String.Join(", ", missing)} are required by '{data.FullName}'; supply them with the device.");
        }
        return data;
    }

    /// <summary>
    /// The problems that prevent transferring the range with the messages, split by <paramref name="splitBy"/>, for the
    /// device; empty when it can: the device values the messages need are known (the root's with the client's over
    /// them), and the map can split the range for the message.
    /// </summary>
    internal IReadOnlyList<string> Validate(PlanRange range, DeviceMessage splitBy, IEnumerable<DeviceMessage> messages, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(range);
        var map = Get(range);
        var problems = new List<string>();
        var missing = DeviceValues.Missing(messages, map.Device.With(device));
        if (missing.Count > 0)
        {
            problems.Add($"The device values {String.Join(", ", missing)} are required by the messages; supply them with the device.");
        }
        problems.AddRange(map.Validate(range, splitBy));
        return problems;
    }

    /// <summary>
    /// The device values of the map's root with the client's values over them; all values the data messages need must be known.
    /// </summary>
    /// <exception cref="InvalidOperationException">A device value the data messages need is not known.</exception>
    internal static DeviceValues DeviceFor(IMessageMap map, DeviceValues client)
    {
        var device = map.Device.With(client);
        var missing = DeviceValues.Missing(map.DataMessages, device);
        return missing.Count == 0
            ? device
            : throw new InvalidOperationException($"The device values {String.Join(", ", missing)} are required by the messages of '{map.Root}'; supply them with the device.");
    }

    private IMessageMap Create(ExecutionPlan plan)
    {
        foreach (var provider in Providers)
        {
            if (provider.TryCreate(Serializer, plan) is { } map)
            {
                return map;
            }
        }
        throw new InvalidOperationException($"No message map can be made for '{plan.Name?.FullName ?? plan.Root.Path.Value}': no provider recognises the root.");
    }
}
