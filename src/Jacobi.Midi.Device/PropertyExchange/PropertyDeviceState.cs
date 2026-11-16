using System.Text.Json.Nodes;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.PropertyExchange;

/// <summary>
/// The state of a device for a Property Exchange root (<c>midi.role="pe"</c>): the values of its resource as the last
/// Get of each block gave them, with the number of items of each array (<see cref="FieldValueStoreWithCounts"/>). A
/// block is the unit of a Get: the whole resource, or one entry (an alternative of the choice <c>resId</c>). A block is
/// held whole or not at all. For a send (Set) the client's values go over the state; a send of an atomic node
/// (<c>midi.transfer="atomic"</c>, a resource set whole) needs every value of it. Guarded by a lock.
/// </summary>
internal sealed class PropertyDeviceState : IDeviceState
{
    private readonly Lock _lock = new();
    private readonly FieldValueStoreWithCounts _store = new();
    private readonly HashSet<GroupInfo> _held = [];
    private readonly GroupInfo _root;
    private readonly ChoiceInfo? _entries;

    public PropertyDeviceState(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _root = plan.Root;
        _entries = PropertyData.EntriesOf(plan.Root);
        Whole = plan.CreateRange(plan.Root.Path);
    }

    public PlanRange Whole { get; }

    /// <summary>True when the resource has entries (the choice <c>resId</c>): a block per entry.</summary>
    public bool HasEntries => _entries is not null;

    /// <summary>
    /// The blocks the range covers: the whole resource (<c>Entry</c> null), or each entry (<c>resId</c>) the range
    /// overlaps.
    /// </summary>
    public IEnumerable<(GroupInfo? Entry, PlanRange Block)> Blocks(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (_entries is null)
        {
            return range.Overlaps(Whole) ? [(null, Whole)] : [];
        }
        return _entries.Members.OfType<GroupInfo>()
            .Select(entry => ((GroupInfo?)entry, Whole.ExecutionPlan.CreateRange(entry.Path)))
            .Where(block => block.Item2.Overlaps(range))
            .ToList();
    }

    /// <summary>
    /// Takes the data of a Get of a block (<paramref name="entry"/> null: the whole resource) in place of what the state
    /// held of it.
    /// </summary>
    /// <exception cref="FormatException">A value does not fit its field: the block is not held.</exception>
    public void Take(GroupInfo? entry, JsonNode? data)
    {
        var block = entry ?? _root;
        lock (_lock)
        {
            _store.Clear(block.Path);
            _held.Remove(block);
            try
            {
                if (entry is null)
                    PropertyData.CopyTo(_root, data, _store);
                else
                    PropertyData.CopyTo(_root, entry, data, _store);
                _held.Add(block);
            }
            catch (FormatException)
            {
                _store.Clear(block.Path);
                throw;
            }
        }
    }

    public bool Contains(PlanRange block)
    {
        var blocks = Blocks(block).ToList();
        lock (_lock)
        {
            return blocks.Count > 0 && blocks.All(covered => _held.Contains(covered.Entry ?? _root));
        }
    }

    // each block of the range the state holds, as a read of the root (as the Get of that block gives it)
    public void CopyTo(PlanRange range, IValueSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        foreach (var (entry, _) in Blocks(range))
        {
            bool held;
            lock (_lock)
            {
                held = _held.Contains(entry ?? _root);
            }
            if (held)
            {
                PropertyData.Frame(_root, entry, sink, (group, scope) => Members(group, scope, InstancePath.Empty));
            }
        }
    }

    // the client's values of the fields in the range; a repeat has the number of items of the schema, of the source
    // (an open repeat it gives a count for), else of the state (the client cannot change the number of items yet)
    public FieldValueStore ClientValues(PlanRange range, IValueSource source)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);
        var client = new FieldValueStore();
        foreach (var (entry, _) in Blocks(range))
        {
            var scope = source;
            if (entry is not null)
            {
                var choice = source.EnterChoice(new ChoiceContext { Node = _entries!, Services = PropertyData.NoServices.Instance, Instance = InstancePath.Empty }, entry.Index);
                scope = choice.EnterGroup(new GroupContext { Node = entry, Services = PropertyData.NoServices.Instance, Instance = InstancePath.Empty });
            }
            Read(entry ?? _root, scope, InstancePath.Empty, range, client);
        }
        return client;
    }

    // a send of an atomic node (midi.transfer="atomic": a resource set whole) sends all of it: every field that can be
    // read and written must be known, from the client or (unless fresh) from the state; the fields and their items are
    // known only from a block the state holds
    public bool Completes(PlanRange range, FieldValueStore client, bool fresh)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(client);
        if (FieldsOf(range.WidenToAtomic()) is not { } fields)
        {
            return false;
        }
        lock (_lock)
        {
            return fields
                .Where(field => MidiProperties.AccessOf(field.Field) == NodeAccess.ReadWrite)
                .All(field => client.TryGet(field.Field.Path, field.Instance, out _)
                    || (!fresh && _store.TryGet(field.Field.Path, field.Instance, out _)));
        }
    }

    // a read-only field is not sent, so the device keeps its own value
    public void Merge(PlanRange range, FieldValueStore client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var plan = Whole.ExecutionPlan;
        var values = client.Values
            .Where(value => plan.Find(new SchemaPath(value.Path)) is { } node && MidiProperties.AccessOf(node) != NodeAccess.Read)
            .ToList();
        lock (_lock)
        {
            foreach (var (path, instance, value) in values)
            {
                _store.Set(path, instance, value);
            }
        }
    }

    /// <summary>
    /// The data of a block (<paramref name="entry"/> null: the whole resource) as the state holds it, for a Set of the
    /// whole block.
    /// </summary>
    /// <exception cref="FormatException">A value has no JSON form.</exception>
    public JsonNode? Body(GroupInfo? entry)
        => PropertyData.ToJson(entry ?? _root,
            (field, instance) => TryGet(field, instance, out var value) ? value : null,
            (repeat, instance) => TryGetCount(repeat, instance, out var count) ? count : null);

    /// <summary>
    /// The values of the fields of a block in the range that can be written, as the state holds them, by their JSON
    /// Pointer, for a partial Set (<c>setPartial</c>, M2-103 section 10).
    /// </summary>
    /// <exception cref="FormatException">A value has no JSON form.</exception>
    public JsonObject PartialBody(GroupInfo? entry, PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        var block = entry ?? _root;
        var fields = new List<(FieldInfo, InstancePath)>();
        Fields(block, InstancePath.Empty, range, fields);

        var body = new JsonObject();
        foreach (var (field, instance) in fields.Where(field => MidiProperties.AccessOf(field.Item1) != NodeAccess.Read))
        {
            if (TryGet(field, instance, out var value))
                body[PropertyData.PointerOf(block, field, instance)] = PropertyData.ToJson(field, value);
        }
        return body;
    }

    // the field occurrences in the range, of the blocks it covers, with the items the data gave; null when the state
    // does not hold one of those blocks
    private List<(FieldInfo Field, InstancePath Instance)>? FieldsOf(PlanRange range)
    {
        var fields = new List<(FieldInfo, InstancePath)>();
        foreach (var (entry, _) in Blocks(range))
        {
            lock (_lock)
            {
                if (!_held.Contains(entry ?? _root))
                {
                    return null;
                }
            }
            Fields(entry ?? _root, InstancePath.Empty, range, fields);
        }
        return fields;
    }

    private void Fields(GroupInfo group, InstancePath instance, PlanRange range, List<(FieldInfo, InstancePath)> fields)
    {
        foreach (var member in group.Members)
        {
            switch (member)
            {
                case FieldInfo field:
                    if (Covers(range, field, instance))
                        fields.Add((field, instance));
                    break;
                case RepeatInfo repeat:
                    var count = repeat.Count is int fixedCount ? fixedCount : TryGetCount(repeat, instance, out var held) ? held : 0;
                    for (var index = 0; index < count; index++)
                        Fields(repeat, instance.Append(index), range, fields);
                    break;
                case ChoiceInfo:
                    // a generated root has no choice below its resId entries
                    break;
                case GroupInfo child:
                    Fields(child, instance, range, fields);
                    break;
            }
        }
    }

    // the client's values of a group occurrence that are in the range
    private void Read(GroupInfo group, IValueSource source, InstancePath instance, PlanRange range, FieldValueStore client)
    {
        foreach (var member in group.Members)
        {
            switch (member)
            {
                case FieldInfo field:
                    if (Covers(range, field, instance)
                        && source.GetField(new FieldContext { Node = field, Services = PropertyData.NoServices.Instance, Instance = instance }) is { Status: SourceStatus.Value, Value: { } value })
                        client.Set(field.Path, instance, value);
                    break;

                case RepeatInfo repeat:
                    var context = new RepeatContext { Node = repeat, Services = PropertyData.NoServices.Instance, Instance = instance };
                    IValueSource scope;
                    int count;
                    if (repeat.Count is int fixedCount)
                    {
                        (scope, count) = (source.EnterRepeat(context, fixedCount), fixedCount);
                    }
                    else if (source is IOpenRepeatSource open && open.EnterOpenRepeat(context) is { Count: >= 0 } counted)
                    {
                        (scope, count) = (counted.Scope, counted.Count);
                    }
                    else if (TryGetCount(repeat, instance, out var held))
                    {
                        (scope, count) = (source is IOpenRepeatSource itemByItem ? itemByItem.EnterOpenRepeat(context).Scope : source.EnterRepeat(context, held), held);
                    }
                    else
                    {
                        // no number of items: none known (the send completes it with a Get first)
                        break;
                    }
                    for (var index = 0; index < count; index++)
                        Read(repeat, scope.EnterItem(context, index, count), instance.Append(index), range, client);
                    break;

                case ChoiceInfo:
                    // a generated root has no choice below its resId entries
                    break;

                case GroupInfo child:
                    Read(child, source.EnterGroup(new GroupContext { Node = child, Services = PropertyData.NoServices.Instance, Instance = instance }), instance, range, client);
                    break;
            }
        }
    }

    // true when the field occurrence is in the range
    private static bool Covers(PlanRange range, FieldInfo field, InstancePath instance)
        => range.Contains(range.ExecutionPlan.CreateRange(field.Path, instance, field.Path, instance));

    // a block is the unit of a Get: a range forgets the blocks it overlaps
    public void Clear(PlanRange range)
    {
        var blocks = Blocks(range).ToList();
        lock (_lock)
        {
            foreach (var (entry, _) in blocks)
            {
                var block = entry ?? _root;
                _store.Clear(block.Path);
                _held.Remove(block);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _store.Clear();
            _held.Clear();
        }
    }

    // the values of a group occurrence, with the number of items of its repeats as the data gave them
    private void Members(GroupInfo group, IValueSink sink, InstancePath instance)
    {
        foreach (var member in group.Members)
        {
            switch (member)
            {
                case FieldInfo field:
                    if (TryGet(field, instance, out var value))
                        sink.SetField(new FieldContext { Node = field, Services = PropertyData.NoServices.Instance, Instance = instance }, value);
                    break;

                case RepeatInfo repeat:
                    if (!TryGetCount(repeat, instance, out var count))
                        break;
                    var context = new RepeatContext { Node = repeat, Services = PropertyData.NoServices.Instance, Instance = instance };
                    var repeatScope = sink.EnterRepeat(context, count);
                    for (var index = 0; index < count; index++)
                    {
                        var item = repeatScope.EnterItem(context, index, count);
                        Members(repeat, item, instance.Append(index));
                        item.ExitItem(context, index, count);
                    }
                    repeatScope.ExitRepeat(context, count);
                    break;

                case ChoiceInfo:
                    // a generated root has no choice below its resId entries
                    break;

                case GroupInfo child:
                    var groupContext = new GroupContext { Node = child, Services = PropertyData.NoServices.Instance, Instance = instance };
                    var scope = sink.EnterGroup(groupContext);
                    Members(child, scope, instance);
                    scope.ExitGroup(groupContext);
                    break;
            }
        }
    }

    private bool TryGet(FieldInfo field, InstancePath instance, out LogicalField value)
    {
        lock (_lock)
        {
            return _store.TryGet(field.Path, instance, out value);
        }
    }

    private bool TryGetCount(RepeatInfo repeat, InstancePath instance, out int count)
    {
        lock (_lock)
        {
            return _store.TryGetCount(repeat.Path, instance, out count);
        }
    }

    // as a sink and a source the state is its store (the session takes a Get with Take)
    public void SetField(FieldContext context, LogicalField value)
    {
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

    public IValueSink EnterRepeat(RepeatContext context, int count)
    {
        lock (_lock)
        {
            _store.EnterRepeat(context, count);
        }
        return this;
    }

    public OpenRepeatSourceResult EnterOpenRepeat(RepeatContext context)
    {
        lock (_lock)
        {
            return _store.TryGetCount(context.Path, context.Instance, out var count)
                ? OpenRepeatSourceResult.Counted(this, count)
                : OpenRepeatSourceResult.ItemByItem(this);
        }
    }

    public OpenRepeatAction NextItem(RepeatContext context, int index) => OpenRepeatAction.Default;
    public IValueSink EnterGroup(GroupContext context) => this;
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitRepeat(RepeatContext context, int count) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }
    IValueSource IValueSource.EnterGroup(GroupContext context) => this;
    IValueSource IValueSource.EnterRepeat(RepeatContext context, int count) => this;
    IValueSource IValueSource.EnterItem(RepeatContext context, int index, int count) => this;
    IValueSource IValueSource.EnterChoice(ChoiceContext context, int selectedIndex) => this;
}
