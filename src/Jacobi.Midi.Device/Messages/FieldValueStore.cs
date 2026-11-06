using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// A flat value model that keeps field values by schema path and instance (the paths and instances of the map plan):
/// as a sink it stores what is read, as a source it provides what it holds (no value for anything else). Used by the
/// device session for its state of a device and by clients that keep the values of a device (e.g. the console's
/// device state). Not thread-safe.
/// </summary>
public sealed class FieldValueStore : IValueSink, IValueSource
{
    private readonly Dictionary<(string Path, InstancePath Instance), LogicalField> _values = [];

    public int Count => _values.Count;

    public bool TryGet(string path, InstancePath instance, out LogicalField value)
        => _values.TryGetValue((path, instance), out value!);

    public void Set(string path, InstancePath instance, LogicalField value)
        => _values[(path, instance)] = value;

    /// <summary>Forgets the value of a field; false when the store has none.</summary>
    public bool Remove(string path, InstancePath instance)
        => _values.Remove((path, instance));

    /// <summary>Forgets all values.</summary>
    public void Clear() => _values.Clear();

    /// <summary>The values the store holds, by path and instance.</summary>
    public IEnumerable<(string Path, InstancePath Instance, LogicalField Value)> Values
        => _values.Select(entry => (entry.Key.Path, entry.Key.Instance, entry.Value));

    public void SetField(FieldContext context, LogicalField value)
        => _values[(context.Path, context.Instance)] = value;

    public SourceResult GetField(FieldContext context)
        => _values.TryGetValue((context.Path, context.Instance), out var value)
            ? SourceResult.Provided(value)
            : SourceResult.NoValue();

    // the store is flat: every scope is the store itself
    public IValueSink EnterGroup(GroupContext context) => this;
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }

    IValueSource IValueSource.EnterGroup(GroupContext context) => this;
    public int GetCount(RepeatContext context) => -1;
    public IValueSource EnterItem(RepeatContext context, int index) => this;
    public int GetSelectedIndex(ChoiceContext context)
        => throw new NotSupportedException($"'{context.Path}': choices are not supported in an address map.");
    IValueSource IValueSource.EnterChoice(ChoiceContext context) => this;
}
