using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// A flat value model that keeps field values by schema path and instance: as a sink it stores what is read, as a
/// source it provides what it holds (no value for anything else). Used to complete atomic groups (read-modify-write).
/// </summary>
internal sealed class FieldValueStore : IValueSink, IValueSource
{
    private readonly Dictionary<(string Path, InstancePath Instance), LogicalField> _values = [];

    public int Count => _values.Count;

    public bool TryGet(string path, InstancePath instance, out LogicalField value)
        => _values.TryGetValue((path, instance), out value!);

    public void Set(string path, InstancePath instance, LogicalField value)
        => _values[(path, instance)] = value;

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
