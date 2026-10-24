using System.Globalization;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The value model the serializer reads into: one scope of an <see cref="InstanceNode"/> tree (a group, repeat item,
/// choice or open choice body). The mirror of <see cref="InstanceValueSource"/>: the members of the scope are found by name
/// and the field values are formatted as text (the label of a data type option when it has one).
/// The items of a repeat are added as they are read and a choice selects the alternative that is read;
/// the body of an open choice must have been selected by the user.
/// </summary>
/// <remarks>
/// When the instance is not the root of the plan (a range is read), the scopes above it have no node:
/// they only lead to the node whose plan path is the path of the instance.
/// </remarks>
internal sealed class InstanceValueSink : IOpenChoiceSink
{
    // the scope: null above the instance
    private readonly InstanceNode? _node;
    private readonly InstanceNode _target;
    private readonly Dictionary<InstanceRepeat, int> _itemCounts;
    // the fields that received a value (shared by all scopes)
    private readonly List<InstanceField> _read;

    /// <param name="node">The node the scope represents; null for a scope above <paramref name="target"/>.</param>
    /// <param name="target">The instance that is read: the root of the instance tree.</param>
    public InstanceValueSink(InstanceNode? node, InstanceNode target)
        : this(node, target, [], [])
    {
    }

    private InstanceValueSink(InstanceNode? node, InstanceNode target, Dictionary<InstanceRepeat, int> itemCounts, List<InstanceField> read)
    {
        _node = node;
        _target = target;
        _itemCounts = itemCounts;
        _read = read;
    }

    /// <summary>
    /// Prepares the instance tree for a read: the values of the (non-constant) fields are cleared.
    /// </summary>
    public void Begin()
    {
        foreach (var field in _target.Fields().Where(field => !field.IsConstant))
        {
            field.Value = String.Empty;
        }
        _itemCounts.Clear();
        _read.Clear();
    }

    /// <summary>
    /// Removes the items of the open repeats that were not read. After a failed read (<paramref name="completed"/> false)
    /// only the repeats that were reached are trimmed: the ones after the failure keep their items.
    /// Then the part that received values is expanded (see <see cref="RevealRead"/>).
    /// </summary>
    public void End(bool completed)
    {
        foreach (var repeat in Repeats(_target))
        {
            if (_itemCounts.TryGetValue(repeat, out var count))
            {
                repeat.Truncate(count);
            }
            else if (completed)
            {
                repeat.Truncate(0);
            }
        }
        RevealRead();
    }

    /// <summary>
    /// Expands the nearest node that holds all the fields that received a value, and the nodes above it,
    /// e.g. the patch a DT1 message addressed; the large parts below it keep their expansion.
    /// </summary>
    private void RevealRead()
    {
        // the nodes from the read instance down to each read field that is still in the tree (not removed with its repeat item)
        var fields = _target.Fields().ToHashSet();
        var paths = _read.Where(fields.Contains).Select(Ancestors).ToList();
        if (paths.Count == 0)
        {
            return;
        }
        var common = paths[0];
        foreach (var path in paths.Skip(1))
        {
            var length = 0;
            while (length < common.Count && length < path.Count && ReferenceEquals(common[length], path[length]))
            {
                length++;
            }
            common = common[..length];
        }
        if (common.Count > 0)
        {
            // the field itself has nothing to expand: reveal its group when only one field was read
            (common[^1] is InstanceField && common.Count > 1 ? common[^2] : common[^1]).Reveal();
        }
    }

    // the nodes from the read instance down to the node
    private List<InstanceNode> Ancestors(InstanceNode node)
    {
        var path = new List<InstanceNode>();
        for (InstanceNode? current = node; current is not null; current = current.Parent)
        {
            path.Insert(0, current);
            if (ReferenceEquals(current, _target))
            {
                break;
            }
        }
        return path;
    }

    public void SetField(FieldContext context, LogicalField value)
    {
        if (Find(context) is InstanceField { IsConstant: false } field)
        {
            field.Value = Format(context.Field.DataType, value.Value);
            _read.Add(field);
        }
    }

    public IValueSink EnterGroup(GroupContext context) => Scope(Find(context));

    public IValueSink EnterItem(RepeatContext context, int index, int count)
    {
        if (Find(context) is not InstanceRepeat repeat)
        {
            return Scope(null);
        }
        _itemCounts[repeat] = Math.Max(_itemCounts.GetValueOrDefault(repeat), index + 1);
        return Scope(repeat.GetOrAddItem(index));
    }

    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex)
    {
        if (Find(context) is InstanceChoice choice && selectedIndex >= 0 && selectedIndex < choice.Options.Count
            && !ReferenceEquals(choice.SelectedOption, choice.Options[selectedIndex]))
        {
            choice.SelectedOption = choice.Options[selectedIndex];
        }
        return Scope(Find(context));
    }

    public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
        => Find(context) is InstanceChoice { SelectedOption: { } option, Selected: { } body }
            ? new OpenChoiceSinkResult(Scope(body), new SchemaName(option.Name))
            : throw new InvalidOperationException($"Select the body of the open choice '{context.Path}' before matching.");

    public void ExitGroup(GroupContext context)
    {
    }

    public void ExitItem(RepeatContext context, int index, int count)
    {
    }

    public void ExitChoice(ChoiceContext context, int selectedIndex)
    {
    }

    private InstanceValueSink Scope(InstanceNode? node) => new(node, _target, _itemCounts, _read);

    /// <summary>
    /// The instance node of the plan node in <paramref name="context"/>: a member of this scope (by name) or, above the
    /// instance, the instance itself when the plan path matches.
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

    /// <summary>The repeats in the instance tree (through the selected alternatives of the choices).</summary>
    private static IEnumerable<InstanceRepeat> Repeats(InstanceNode node)
    {
        switch (node)
        {
            case InstanceRepeat repeat:
                yield return repeat;
                foreach (var nested in repeat.Items.ToList().SelectMany(Repeats))
                {
                    yield return nested;
                }
                break;
            case InstanceGroup group:
                foreach (var nested in group.Members.SelectMany(Repeats))
                {
                    yield return nested;
                }
                break;
            case InstanceChoice { Selected: { } selected }:
                foreach (var nested in Repeats(selected))
                {
                    yield return nested;
                }
                break;
        }
    }

    /// <summary>
    /// Formats a value that was read as text the data type parses again (see <see cref="InstanceValueSource"/>):
    /// the label of an option, hex bytes, or the invariant text of the value.
    /// </summary>
    internal static string Format(DataTypeDescriptor dataType, object? value)
    {
        if (dataType.Options is { } options && TryGetKey(value, out var key) && options.TryGetValue(key, out var label))
        {
            return label;
        }
        return value switch
        {
            null => String.Empty,
            byte[] bytes => Convert.ToHexString(bytes),
            ReadOnlyMemory<byte> memory => Convert.ToHexString(memory.Span),
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? String.Empty
        };
    }

    private static bool TryGetKey(object? value, out long key)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                key = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            case ulong number when number <= Int64.MaxValue:
                key = (long)number;
                return true;
            default:
                key = 0;
                return false;
        }
    }
}
