using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The value model the serializer writes from: one scope of an <see cref="InstanceNode"/> tree (a group, repeat item,
/// choice or open choice body). The members of the scope are found by name; the field values (text) are parsed with the data type of the field.
/// </summary>
/// <remarks>
/// When the instantiated node is not the root of the plan (a range is written), the scopes above it have no node:
/// they only lead to the node whose plan path is the path of the instantiated node.
/// </remarks>
internal sealed class InstanceValueSource : IOpenChoiceSource
{
    // the scope: null above the instantiated node
    private readonly InstanceNode? _node;
    private readonly InstanceNode _target;

    /// <param name="node">The node the scope represents; null for a scope above <paramref name="target"/>.</param>
    /// <param name="target">The instantiated node: the root of the instance tree.</param>
    public InstanceValueSource(InstanceNode? node, InstanceNode target)
    {
        _node = node;
        _target = target;
    }

    public SourceResult GetField(FieldContext context)
    {
        if (Find(context) is not InstanceField field)
        {
            return SourceResult.NoValue();
        }

        // the value is the logical value: of the value type of the data type (e.g. the option value with options)
        var valueType = context.Field.ValueType ?? context.Field.DataType;
        // the spaces of a text are part of its value (e.g. a patch name that starts with a space); other values are trimmed
        var text = valueType.ClrType == typeof(string) ? field.Value ?? String.Empty : field.Value?.Trim() ?? String.Empty;
        // an empty value or a reference is derived by the engine (or fails there)
        if (text.Length == 0 ||
            text.StartsWith(SchemaNodeRef.Prefix, StringComparison.Ordinal) ||
            text.StartsWith(SchemaPubRef.Prefix, StringComparison.Ordinal))
        {
            return SourceResult.NoValue();
        }

        // an option value is matched case-insensitive
        if (context.Field.ValueType is not null && context.Field.DataType.Options?.Values
            .FirstOrDefault(option => String.Equals(InstanceValueSink.Format(option), text, StringComparison.OrdinalIgnoreCase)) is { } option)
        {
            return SourceResult.Provided(new LogicalField(context.Field.Name, valueType.ClrType, option));
        }
        if (!valueType.Parse(text, out var value))
        {
            // the field declares its own semantic processors (e.g. sys.map): they convert the text themselves
            if (context.Field.ValueType is null && context.Field.Pipeline.ValueProcessors.Count > 0)
            {
                return SourceResult.Provided(new LogicalField(context.Field.Name, typeof(string), text));
            }
            throw new InvalidOperationException($"'{text}' is not a valid value for {field.DisplayPath} ({valueType.Name}).");
        }
        return SourceResult.Provided(new LogicalField(context.Field.Name, valueType.ClrType, value));
    }

    public IValueSource EnterGroup(GroupContext context) => new InstanceValueSource(Find(context), _target);

    public int GetCount(RepeatContext context) => Find(context) switch
    {
        InstanceRepeat repeat => repeat.Items.Count,
        // a repeat above the instantiated node: one item leads to it
        _ when _node is null && context.Repeat.UntilEnd => 1,
        // the count of the schema is used
        _ => throw new NotSupportedException($"The instance has no items for '{context.Path}'.")
    };

    // the item by its index: the one item of an address map repeat is any item of the address space
    public IValueSource EnterItem(RepeatContext context, int index)
        => new InstanceValueSource(Find(context) is InstanceRepeat repeat ? repeat.Items.FirstOrDefault(item => item.Index == index) : null, _target);

    public int GetSelectedIndex(ChoiceContext context)
        => Find(context) is InstanceChoice { SelectedOption: { } option } choice
            ? IndexOf(choice.Options, option)
            : throw new NotSupportedException($"The instance has no alternative selected for '{context.Path}'.");

    public IValueSource EnterChoice(ChoiceContext context) => new InstanceValueSource(Find(context), _target);

    public OpenChoiceSourceResult EnterOpenChoice(OpenChoiceContext context)
        => Find(context) is InstanceChoice { SelectedOption: { } option, Selected: { } body }
            ? new OpenChoiceSourceResult(new InstanceValueSource(body, _target), new SchemaName(option.Name))
            : throw new InvalidOperationException($"Select the body of the open choice '{context.Path}'.");

    /// <summary>
    /// The instance node of the plan node in <paramref name="context"/>: a member of this scope (by name) or, above the
    /// instantiated node, the instantiated node itself when the plan path matches.
    /// </summary>
    private InstanceNode? Find(ValueContext context) => _node switch
    {
        null => String.Equals(context.Node.Path.Value, _target.Path.Value, StringComparison.OrdinalIgnoreCase) ? _target : null,
        InstanceGroup group => group.Members.FirstOrDefault(member => IsNamed(member, context.Name)),
        // the scope of a choice holds the selected alternative
        InstanceChoice choice => choice.Selected is { } selected && IsNamed(selected, context.Name) ? selected : null,
        _ => null
    };

    private static bool IsNamed(InstanceNode node, string name) => String.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<ChoiceOption> options, ChoiceOption option)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (ReferenceEquals(options[index], option))
            {
                return index;
            }
        }
        return -1;
    }
}
