using System.Buffers;
using System.Collections.Concurrent;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The message map of a root that is a message itself (a dump: e.g. an Identity Reply, or a function-code dump
/// written out as one message group): the root is its only data message, a range of it is always transferred whole
/// (one block: the whole root), and a block is complete after one message. Any other message (a request, an ack) is
/// found by its schema name and holds only constants and device values. Stateless (apart from a cache of the named
/// messages) and thread-safe.
/// </summary>
internal sealed class DumpMap : IMessageMap
{
    private readonly Serializer _serializer;
    private readonly ExecutionPlan _plan;
    private readonly DeviceMessage _dump;
    private readonly ConcurrentDictionary<string, Lazy<DeviceMessage>> _named = new(StringComparer.OrdinalIgnoreCase);

    private DumpMap(Serializer serializer, ExecutionPlan plan, SchemaName root, DeviceValues device, DeviceMessage dump)
    {
        _serializer = serializer;
        _plan = plan;
        Root = root;
        Device = device;
        _dump = dump;
        Whole = plan.CreateRange(plan.Root.Path);
        DataMessages = [dump];
    }

    /// <summary>
    /// The map of a root that is a message itself; throws a <see cref="MessageMapException"/> when the root cannot be
    /// transferred as a dump (e.g. it is the body of an open choice: not supported yet).
    /// </summary>
    public static DumpMap Create(Serializer serializer, ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(plan);

        var root = plan.Name ?? throw new MessageMapException(plan.Root.Path.Value,
            [new MessageMapProblem(String.Empty, plan.Root.Path, "the plan has no schema name; create it with the serializer (GetPlan).")]);
        var document = root.Namespace;
        var problems = new MessageMapProblems();

        if (serializer.Schemas.FindOpenChoices(serializer.Schemas.FindRoot(root)).Any(choice => DeviceMessages.IsRole(choice, MidiProperties.RoleData)))
        {
            problems.Error(document, plan.Root.Path, "the root is the body of a data message (an open choice with midi.role=\"data\"): transferring it without an address map is not supported yet.");
        }

        var device = DeviceValues.FromProperties(plan.Root.Group, document, plan.Root.Path, problems);
        var dump = DeviceMessages.Create(plan, plan.Root, document, problems);
        if (dump.IsData)
        {
            problems.Error(document, plan.Root.Path, "the root has a data open choice (midi.role=\"data\"): it carries other data, it is not a dump itself.");
        }

        if (problems.HasErrors)
        {
            throw new MessageMapException(root.FullName, problems);
        }
        return new DumpMap(serializer, plan, root, device, dump);
    }

    public SchemaName Root { get; }
    public DeviceValues Device { get; }
    public IReadOnlyList<DeviceMessage> DataMessages { get; }

    /// <summary>The range of the whole root: the only block.</summary>
    public PlanRange Whole { get; }

    public DeviceMessage Message(SchemaName name)
        => _named.GetOrAdd(name.FullName, _ => new Lazy<DeviceMessage>(() => Resolve(name))).Value;

    public bool IsData(DeviceMessage message) => ReferenceEquals(message, _dump);

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
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(message.Range, new EnvelopeSource(EnvelopeSource.NoBlockRoles, device, client: source), writer);
        return writer.WrittenSpan.ToArray();
    }

    public bool TryRead(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device, out ReceivedData? data)
    {
        ArgumentNullException.ThrowIfNull(message);
        data = null;
        if (!DeviceMessages.TryRecognise(_serializer, message, sysEx, device, out var read))
        {
            return false;
        }
        if (IsData(message))
        {
            data = new ReceivedData(message, sysEx, read, Whole, Decode);
        }
        return true;
    }

    // every message of the dump holds the whole root
    public bool IsComplete(PlanRange block, IReadOnlyList<ReceivedData> received) => received.Count > 0;

    public IDeviceState CreateState()
        => new DumpDeviceState(_serializer, Whole, _dump.Roles.Values.Select(node => node.Path.Value).ToHashSet(StringComparer.Ordinal));

    private long Decode(ReceivedData data, IValueSink sink)
        => _serializer.Deserialize(data.Message.Range, new ReadOnlySequence<byte>(data.SysEx), sink);

    private DeviceMessage Resolve(SchemaName name)
    {
        if (String.Equals(name.FullName, Root.FullName, StringComparison.OrdinalIgnoreCase))
        {
            return _dump;
        }

        var (plan, group, document) = DeviceMessages.FindMessageGroup(_serializer, name);
        var problems = new MessageMapProblems();
        var message = DeviceMessages.Create(plan, group, document, problems);
        if (message.IsData)
        {
            throw new InvalidOperationException($"'{name}' carries data of another root: a dump of '{Root}' is the root itself.");
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
