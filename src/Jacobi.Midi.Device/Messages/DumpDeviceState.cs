using System.Buffers;
using System.Globalization;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The state of a device for a dump (<see cref="DumpMap"/>): the field values of the root, kept as a whole. A dump is
/// always received and sent whole: the state holds the root or nothing, gives the whole root to a sink, and a send
/// takes all values from the client (a client that supplies only some of them is told so). The fields with a role
/// (e.g. the device id) are not values of the device's data: the device values supply them when a message is written,
/// so the state neither keeps them nor gives them to a sink. Guarded by a lock (the device's own data arrives on the
/// listener's flow).
/// </summary>
/// <param name="rolePaths">The paths of the fields of the root that have a role.</param>
internal sealed class DumpDeviceState(Serializer serializer, PlanRange whole, IReadOnlySet<string> rolePaths) : IDeviceState
{
    private readonly Lock _lock = new();
    private readonly FieldValueStore _store = new();

    public PlanRange Whole { get; } = whole;

    public bool Contains(PlanRange block)
    {
        lock (_lock)
        {
            return _store.Count > 0;
        }
    }

    // the whole root: a dump is one block
    public void CopyTo(PlanRange range, IValueSink sink)
    {
        byte[] bytes;
        lock (_lock)
        {
            bytes = Write(_store);
        }
        serializer.Deserialize(Whole, new ReadOnlySequence<byte>(bytes), new WithoutRoles(sink, rolePaths));
    }

    /// <exception cref="InvalidOperationException">The source does not supply every value of the root: a dump is sent whole.</exception>
    public FieldValueStore ClientValues(PlanRange range, IValueSource source)
    {
        byte[] bytes;
        try
        {
            bytes = Write(source);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"A dump of '{Whole.ExecutionPlan.Name}' is sent whole: the source must supply all its values. {exception.Message}", exception);
        }
        var client = new FieldValueStore();
        serializer.Deserialize(Whole, new ReadOnlySequence<byte>(bytes), new WithoutRoles(client, rolePaths));
        return client;
    }

    // the client supplies the whole root (see ClientValues)
    public bool Completes(PlanRange range, FieldValueStore client, bool fresh) => true;

    public void Merge(PlanRange range, FieldValueStore client)
    {
        lock (_lock)
        {
            foreach (var (path, instance, value) in client.Values)
            {
                _store.Set(path, instance, value);
            }
        }
    }

    // a dump is forgotten as a whole
    public void Clear(PlanRange range)
    {
        if (range.Overlaps(Whole))
        {
            Clear();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _store.Clear();
        }
    }

    public void SetField(FieldContext context, LogicalField value)
    {
        if (rolePaths.Contains(context.Path))
        {
            return;
        }
        lock (_lock)
        {
            _store.SetField(context, value);
        }
    }

    public SourceResult GetField(FieldContext context)
    {
        lock (_lock)
        {
            return _store.GetField(context);
        }
    }

    // the store is flat: every scope is the state itself
    public IValueSink EnterGroup(GroupContext context) => this;
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }
    IValueSource IValueSource.EnterGroup(GroupContext context) => this;
    public int GetCount(RepeatContext context) => ((IValueSource)_store).GetCount(context);
    public IValueSource EnterItem(RepeatContext context, int index) => this;
    public int GetSelectedIndex(ChoiceContext context) => ((IValueSource)_store).GetSelectedIndex(context);
    IValueSource IValueSource.EnterChoice(ChoiceContext context) => this;

    // the whole root from the values; a field with a role gets a placeholder (only the values are read back)
    private byte[] Write(IValueSource source)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(Whole, new RolePlaceholders(source, rolePaths), writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>A source that gives 0 for the fields with a role the inner source has no value for.</summary>
    private sealed class RolePlaceholders(IValueSource inner, IReadOnlySet<string> rolePaths) : IValueSource
    {
        public SourceResult GetField(FieldContext context)
        {
            var result = inner.GetField(context);
            if (result.Status == SourceStatus.Value || !rolePaths.Contains(context.Path))
            {
                return result;
            }
            var type = context.Field.DataType.ClrType;
            return SourceResult.Provided(new LogicalField(context.Name, type, Convert.ChangeType(0, type, CultureInfo.InvariantCulture)));
        }

        public IValueSource EnterGroup(GroupContext context) => new RolePlaceholders(inner.EnterGroup(context), rolePaths);
        public int GetCount(RepeatContext context) => inner.GetCount(context);
        public IValueSource EnterItem(RepeatContext context, int index) => new RolePlaceholders(inner.EnterItem(context, index), rolePaths);
        public int GetSelectedIndex(ChoiceContext context) => inner.GetSelectedIndex(context);
        public IValueSource EnterChoice(ChoiceContext context) => new RolePlaceholders(inner.EnterChoice(context), rolePaths);
    }

    /// <summary>A sink that leaves out the fields with a role.</summary>
    private sealed class WithoutRoles(IValueSink inner, IReadOnlySet<string> rolePaths) : IValueSink
    {
        public void SetField(FieldContext context, LogicalField value)
        {
            if (!rolePaths.Contains(context.Path))
            {
                inner.SetField(context, value);
            }
        }

        public IValueSink EnterGroup(GroupContext context) => new WithoutRoles(inner.EnterGroup(context), rolePaths);
        public IValueSink EnterItem(RepeatContext context, int index, int count) => new WithoutRoles(inner.EnterItem(context, index, count), rolePaths);
        public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => new WithoutRoles(inner.EnterChoice(context, selectedIndex), rolePaths);
        public void ExitGroup(GroupContext context) => inner.ExitGroup(context);
        public void ExitItem(RepeatContext context, int index, int count) => inner.ExitItem(context, index, count);
        public void ExitChoice(ChoiceContext context, int selectedIndex) => inner.ExitChoice(context, selectedIndex);
    }
}
