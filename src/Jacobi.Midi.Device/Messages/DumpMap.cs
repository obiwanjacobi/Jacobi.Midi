using System.Buffers;
using System.Collections.Concurrent;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The message map of a root that is transferred as a dump, without an address map: a range of it is always
/// transferred whole (one block: the whole root), and a block is complete after one message. Two kinds of root:
/// <list type="bullet">
/// <item>a root that is a message itself (e.g. an Identity Reply, or a function-code dump written out as one message
/// group): the root is its only data message;</item>
/// <item>a root carried as the body of a message's open choice (<c>midi.role="data"</c>, its nodeDef the
/// <c>memberNodeDef</c>): the messages that hold such an open choice are its data messages, and of the roots that
/// conform to the contract the device values read from a message select the body (<see cref="ConformingBodies"/>).</item>
/// </list>
/// Any other message (a request, an ack) is found by its schema name and holds only constants and device values.
/// Stateless (apart from a cache of the named messages) and thread-safe.
/// </summary>
internal sealed class DumpMap : IMessageMap
{
    private readonly Serializer _serializer;
    private readonly ExecutionPlan _plan;
    // the bodies that conform to the contract of the carriers; null when the root is a message itself
    private readonly ConformingBodies? _bodies;
    private readonly ConcurrentDictionary<string, Lazy<DeviceMessage>> _named = new(StringComparer.OrdinalIgnoreCase);

    private DumpMap(Serializer serializer, ExecutionPlan plan, SchemaName root, DeviceValues device,
        IReadOnlyList<DeviceMessage> dataMessages, ConformingBodies? bodies)
    {
        _serializer = serializer;
        _plan = plan;
        Root = root;
        Device = device;
        DataMessages = dataMessages;
        _bodies = bodies;
        Whole = plan.CreateRange(plan.Root.Path);
    }

    /// <summary>
    /// The map of a root that is a message itself, or that a data message carries; throws a
    /// <see cref="MessageMapException"/> with all problems when the root cannot be transferred as a dump.
    /// </summary>
    public static DumpMap Create(Serializer serializer, ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(plan);

        var root = plan.Name ?? throw new MessageMapException(plan.Root.Path.Value,
            [new MessageMapProblem(String.Empty, plan.Root.Path, "the plan has no schema name; create it with the serializer (GetPlan).")]);
        var document = root.Namespace;
        var problems = new MessageMapProblems();
        var device = DeviceValues.FromProperties(plan.Root.Group, document, plan.Root.Path, problems);

        var map = IsCarried(serializer, root)
            ? CreateCarried(serializer, plan, root, device, problems)
            : CreateMessage(serializer, plan, root, device, problems);
        return problems.HasErrors ? throw new MessageMapException(root.FullName, problems) : map!;
    }

    public SchemaName Root { get; }
    public DeviceValues Device { get; }
    public IReadOnlyList<DeviceMessage> DataMessages { get; }

    /// <summary>The range of the whole root: the only block.</summary>
    public PlanRange Whole { get; }

    // true when the root is a body: a data open choice can select it
    private bool IsCarriedRoot => _bodies is not null;

    public DeviceMessage Message(SchemaName name)
        => _named.GetOrAdd(name.FullName, _ => new Lazy<DeviceMessage>(() => Resolve(name))).Value;

    public bool IsData(DeviceMessage message) => DataMessages.Any(data => ReferenceEquals(data, message));

    // a dump is transferred whole with any message
    public IReadOnlyList<string> Validate(PlanRange range, DeviceMessage splitBy) => [];

    public IReadOnlyList<PlanRange> Split(PlanRange range, DeviceMessage message)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!ReferenceEquals(range.ExecutionPlan, _plan))
        {
            throw new ArgumentException($"The range is not a range of the plan of '{Root}'.", nameof(range));
        }
        return [Whole];
    }

    public byte[] Write(DeviceMessage message, PlanRange block, IValueSource? source, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsData(message))
        {
            return DeviceMessages.WriteWithoutData(_serializer, message, EnvelopeSource.NoBlockRoles, device);
        }

        ArgumentNullException.ThrowIfNull(source);
        // a carrier: the root is the body of its open choice; the root itself: the client's values are the message's
        var envelope = IsCarriedRoot
            ? new EnvelopeSource(EnvelopeSource.NoBlockRoles, device, _ => new EnvelopeBody<IValueSource>(Root, null, source))
            : new EnvelopeSource(EnvelopeSource.NoBlockRoles, device, client: source);
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(message.Range, envelope, writer);
        return writer.WrittenSpan.ToArray();
    }

    public MessageMatch Match(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (IsData(message) && IsCarriedRoot)
        {
            return ReadCarried(message, sysEx, device);
        }

        var match = DeviceMessages.Recognise(_serializer, message, sysEx, device, out var read);
        return match.IsRecognised && IsData(message)
            ? MessageMatch.Recognised(new ReceivedData(message, sysEx, read, Whole, Decode))
            : match;
    }

    // every message of the dump holds the whole root
    public bool IsComplete(PlanRange block, IReadOnlyList<ReceivedData> received) => received.Count > 0;

    public IDeviceState CreateState()
        => new DumpDeviceState(_serializer, Whole, IsCarriedRoot
            // the roles are in the carriers, not in the body
            ? new HashSet<string>(StringComparer.Ordinal)
            : DataMessages[0].Roles.Values.Select(node => node.Path.Value).ToHashSet(StringComparer.Ordinal));

    // the root is a message itself: it is its own data message
    private static DumpMap? CreateMessage(Serializer serializer, ExecutionPlan plan, SchemaName root, DeviceValues device, MessageMapProblems problems)
    {
        var dump = DeviceMessages.Create(plan, plan.Root, root.Namespace, problems);
        if (dump.IsData)
        {
            problems.Error(root.Namespace, plan.Root.Path, "the root has a data open choice (midi.role=\"data\"): it carries other data, it is not a dump itself.");
            return null;
        }
        return new DumpMap(serializer, plan, root, device, [dump], bodies: null);
    }

    // the root is the body of a data open choice: the messages that hold the choice carry it
    private static DumpMap? CreateCarried(Serializer serializer, ExecutionPlan plan, SchemaName root, DeviceValues device, MessageMapProblems problems)
    {
        var document = root.Namespace;
        var contract = plan.Root.Group.NodeDef!.Value.Qualify(document);
        var bodies = ConformingBodies.Find(serializer.Schemas, contract, problems);

        var dataMessages = new List<DeviceMessage>();
        var nested = new List<string>();
        foreach (var (messagePlan, group, messageDocument) in DeviceMessages.FindDataMessageGroups(serializer, contract))
        {
            // a group of a root that is a body itself is not a message: the root is carried inside another body
            if (messagePlan.Name is { } carrierRoot && IsCarried(serializer, carrierRoot))
            {
                nested.Add(carrierRoot.FullName);
                continue;
            }
            dataMessages.Add(DeviceMessages.Create(messagePlan, group, messageDocument, problems));
        }

        if (dataMessages.Count == 0)
        {
            problems.Error(document, plan.Root.Path, nested.Count > 0
                ? $"the root is only carried inside another body ({String.Join(", ", nested)}): that is not supported."
                : $"no message carries the root (an open choice with midi.role=\"data\" and memberNodeDef '{contract}').");
            return null;
        }
        return new DumpMap(serializer, plan, root, device, dataMessages, bodies);
    }

    // true when a data open choice can select the root (it is a body)
    private static bool IsCarried(Serializer serializer, SchemaName root)
        => serializer.Schemas.FindOpenChoices(serializer.Schemas.FindRoot(root)).Any(choice => DeviceMessages.IsRole(choice, MidiProperties.RoleData));

    // the device values read before the body must agree with the device and select this root as the body; once it
    // is selected, a message that cannot be read is a near miss
    private MessageMatch ReadCarried(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device)
    {
        var selected = false;
        var sink = new EnvelopeSink(envelope =>
        {
            if (!Selects(envelope.Device, device))
            {
                throw new InvalidOperationException("The message is for another device or body.");
            }
            selected = true;
            return new EnvelopeBody<IValueSink>(Root, null, NullSink.Instance);
        });
        long length;
        try
        {
            length = _serializer.Deserialize(message.Range, new ReadOnlySequence<byte>(sysEx), sink);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MessageMatch.Failed(exception, sink.Device, device, committed: selected);
        }

        if (length != sysEx.Length)
        {
            return selected
                ? MessageMatch.Rejected($"'{message.FullName}': the message has {sysEx.Length} bytes, {length} were expected for '{Root}'.")
                : MessageMatch.NotThis;
        }
        return MessageMatch.Recognised(new ReceivedData(message, sysEx, sink.Device, Whole, Decode));
    }

    private bool Selects(DeviceValues read, DeviceValues device)
    {
        if (read.Any(value => device.TryGet(value.Key, out var expected) && expected != value.Value))
        {
            return false;
        }
        try
        {
            return _bodies!.Select(read) == Root;
        }
        catch (InvalidOperationException)
        {
            // no body, or more than one, matches the device values
            return false;
        }
    }

    private long Decode(ReceivedData data, IValueSink sink)
        => IsCarriedRoot
            ? _serializer.Deserialize(data.Message.Range, new ReadOnlySequence<byte>(data.SysEx),
                new EnvelopeSink(_ => new EnvelopeBody<IValueSink>(Root, null, sink)))
            : _serializer.Deserialize(data.Message.Range, new ReadOnlySequence<byte>(data.SysEx), sink);

    private DeviceMessage Resolve(SchemaName name)
    {
        // a root that is a message itself is named by its own schema name
        if (DataMessages.FirstOrDefault(data => String.Equals(data.FullName, name.FullName, StringComparison.OrdinalIgnoreCase)) is { } dataMessage)
        {
            return dataMessage;
        }

        var (plan, group, document) = DeviceMessages.FindMessageGroup(_serializer, name);
        var problems = new MessageMapProblems();
        var message = DeviceMessages.Create(plan, group, document, problems);
        if (message.IsData)
        {
            throw new InvalidOperationException($"'{name}' carries data, but it cannot carry '{Root}' (its data open choice has another memberNodeDef).");
        }
        if (problems.HasErrors)
        {
            throw new MessageMapException(name.FullName, problems);
        }
        return message;
    }
}

/// <summary>
/// The provider of the default message map: the last of the chain, it takes every root no other provider claims and
/// transfers it as a dump (<see cref="DumpMap"/>); it reports what it cannot handle.
/// </summary>
internal sealed class DumpMapProvider : IMessageMapProvider
{
    public IMessageMap? TryCreate(Serializer serializer, ExecutionPlan plan) => DumpMap.Create(serializer, plan);
}
