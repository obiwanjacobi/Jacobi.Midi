using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;

namespace Jacobi.Midi.Tests;

internal sealed class ValueSource : IValueSource
{
    private readonly Dictionary<string, object> _values = new(StringComparer.InvariantCultureIgnoreCase);
    public IDictionary<string, object> Values => _values;

    public SourceResult GetField(FieldContext context)
    {
        if (!TryGetValue(context.Path, out var value))
        {
            return SourceResult.NoValue();
        }
        return SourceResult.Provided(new LogicalField(context.Field.Name, context.Field.DataType.ClrType, value));
    }

    public IValueSource EnterGroup(GroupContext context)
    {
        return this;
    }

    public void ExitGroup(GroupContext context)
    {
    }

    public IValueSource EnterRepeat(RepeatContext context, int count)
    {
        return this;
    }

    public void ExitRepeat(RepeatContext context, int count)
    {
    }

    public IValueSource EnterItem(RepeatContext context, int index, int count)
    {
        return this;
    }

    public void ExitItem(RepeatContext context, int index, int count)
    {
    }

    public IValueSource EnterChoice(ChoiceContext context, int selectedIndex)
    {
        return this;
    }

    public void ExitChoice(ChoiceContext context, int selectedIndex)
    {
    }

    private bool TryGetValue(string name, out object? value)
        => _values.TryGetValue(name, out value);
}


internal sealed class ValueSink : IValueSink
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.InvariantCultureIgnoreCase);
    public IDictionary<string, object?> Values => _values;

    public void SetField(FieldContext context, LogicalField value)
    {
        _values[context.Path] = value.Value;
    }

    public IValueSink EnterGroup(GroupContext context)
    {
        return this;
    }

    public IValueSink EnterRepeat(RepeatContext context, int count)
    {
        return this;
    }

    public IValueSink EnterItem(RepeatContext context, int index, int count)
    {
        return this;
    }

    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex)
    {
        return this;
    }

    public void ExitGroup(GroupContext context)
    {
    }

    public void ExitRepeat(RepeatContext context, int count)
    {
    }

    public void ExitItem(RepeatContext context, int index, int count)
    {
    }

    public void ExitChoice(ChoiceContext context, int selectedIndex)
    {
    }
}