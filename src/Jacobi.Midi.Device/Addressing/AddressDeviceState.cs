using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// The state of a device for an address map: the field values by path and instance of the map plan, guarded by a lock
/// (the device's own data arrives on the listener's flow). What it holds and what a send needs is decided per field,
/// with its access (<c>midi.access</c>) and the atomic groups of the map. As a sink and a source it is the flat
/// <see cref="FieldValueStore"/>.
/// </summary>
internal sealed class AddressDeviceState(AddressMap map) : IDeviceState
{
    private readonly Lock _lock = new();
    private readonly FieldValueStore _store = new();

    public PlanRange Whole { get; } = map.Plan.CreateRange(map.Plan.Root.Path);

    // the device never transmits write-only fields
    public bool Contains(PlanRange block)
        => map.FieldsOf(block)
            .Where(field => field.Layout.Access != NodeAccess.Write)
            .All(field => Has(field.Node.Path.Value, field.Instance));

    public void CopyTo(PlanRange range, IValueSink sink) => map.CopyValues(range, this, sink);

    public FieldValueStore ClientValues(PlanRange range, IValueSource source)
    {
        var client = new FieldValueStore();
        map.CopyValues(range, source, client);
        return client;
    }

    // a send transfers whole atomic groups: the fields that are sent (not read-only) and can be received (not
    // write-only) must be known
    public bool Completes(PlanRange range, FieldValueStore client, bool fresh)
        => map.FieldsOf(map.WidenToAtomic(range))
            .Where(field => field.Layout.Access == NodeAccess.ReadWrite)
            .All(field => client.TryGet(field.Node.Path.Value, field.Instance, out _) || (!fresh && Has(field.Node.Path.Value, field.Instance)));

    // a read-only field is not sent, so the device keeps its own value
    public void Merge(PlanRange range, FieldValueStore client)
    {
        var readOnly = map.FieldsOf(range)
            .Where(field => field.Layout.Access == NodeAccess.Read)
            .Select(field => (field.Node.Path.Value, field.Instance))
            .ToHashSet();
        lock (_lock)
        {
            foreach (var (path, instance, value) in client.Values.Where(value => !readOnly.Contains((value.Path, value.Instance))))
            {
                _store.Set(path, instance, value);
            }
        }
    }

    public void Clear(PlanRange range)
    {
        lock (_lock)
        {
            foreach (var field in map.FieldsOf(range))
            {
                _store.Remove(field.Node.Path.Value, field.Instance);
            }
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

    private bool Has(string path, InstancePath instance)
    {
        lock (_lock)
        {
            return _store.TryGet(path, instance, out _);
        }
    }
}
