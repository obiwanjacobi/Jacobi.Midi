using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// A flat value model that keeps field values by schema path and instance, like <see cref="FieldValueStore"/>, and also
/// the number of items of each repeat occurrence, for data that gives its own item counts (e.g. a Property Exchange
/// resource, whose arrays have no count in the schema). As a sink it takes the count of every repeat it enters with
/// one: the data it is given must be complete (a transfer of a part of a repeat would give a count that is too small).
/// As a source it gives that count for a repeat without a count. Not thread-safe.
/// </summary>
internal sealed class FieldValueStoreWithCounts : IValueSink, IOpenRepeatSource
{
    private readonly Dictionary<(string Path, InstancePath Instance), LogicalField> _values = [];
    private readonly Dictionary<(string Path, InstancePath Instance), int> _counts = [];

    /// <summary>The value of a field occurrence; false when the store has none.</summary>
    public bool TryGet(string path, InstancePath instance, out LogicalField value)
        => _values.TryGetValue((path, instance), out value!);

    /// <summary>Sets the value of a field occurrence.</summary>
    public void Set(string path, InstancePath instance, LogicalField value)
        => _values[(path, instance)] = value;

    /// <summary>
    /// The number of items of a repeat occurrence (<paramref name="instance"/>: the items of the repeats above it);
    /// false when the store has none.
    /// </summary>
    public bool TryGetCount(string path, InstancePath instance, out int count)
        => _counts.TryGetValue((path, instance), out count);

    /// <summary>Forgets the values and counts of the node at <paramref name="path"/> and of all nodes below it.</summary>
    public void Clear(string path)
    {
        foreach (var key in _values.Keys.Where(key => IsAtOrBelow(key.Path, path)).ToList())
        {
            _values.Remove(key);
        }
        foreach (var key in _counts.Keys.Where(key => IsAtOrBelow(key.Path, path)).ToList())
        {
            _counts.Remove(key);
        }
    }

    /// <summary>Forgets all values and counts.</summary>
    public void Clear()
    {
        _values.Clear();
        _counts.Clear();
    }

    public void SetField(FieldContext context, LogicalField value)
        => _values[(context.Path, context.Instance)] = value;

    public SourceResult GetField(FieldContext context)
        => _values.TryGetValue((context.Path, context.Instance), out var value)
            ? SourceResult.Provided(value)
            : SourceResult.NoValue();

    // a repeat entered with a count: the data gives the number of items (a negative count is not known)
    public IValueSink EnterRepeat(RepeatContext context, int count)
    {
        if (count >= 0)
        {
            _counts[(context.Path, context.Instance)] = count;
        }
        return this;
    }

    // a repeat without a count has the number of items the store holds; without one it is written item by item
    public OpenRepeatSourceResult EnterOpenRepeat(RepeatContext context)
        => _counts.TryGetValue((context.Path, context.Instance), out var count)
            ? OpenRepeatSourceResult.Counted(this, count)
            : OpenRepeatSourceResult.ItemByItem(this);

    public OpenRepeatAction NextItem(RepeatContext context, int index) => OpenRepeatAction.Default;

    // the store is flat: every scope is the store itself
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

    private static bool IsAtOrBelow(string candidate, string path)
        => candidate.Length == path.Length
            ? String.Equals(candidate, path, StringComparison.Ordinal)
            : candidate.Length > path.Length && candidate.StartsWith(path, StringComparison.Ordinal) && candidate[path.Length] == SchemaPath.Separator;
}
