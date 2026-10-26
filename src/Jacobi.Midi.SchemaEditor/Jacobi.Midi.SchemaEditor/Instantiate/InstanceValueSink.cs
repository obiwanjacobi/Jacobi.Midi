using System.Runtime.CompilerServices;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The value model the serializer reads into: one scope of an <see cref="InstanceNode"/> tree (a group, repeat item,
/// choice or open choice body). The mirror of <see cref="InstanceValueSource"/>: the members of the scope are found by name
/// and the field values are formatted as text (the label of a data type option when it has one).
/// The items of a repeat are added as they are read and a choice selects the alternative that is read;
/// the body of an open choice must have been selected by the user.
/// When the values are accumulated (see <see cref="Begin"/>), a read adds to the values of the earlier reads, e.g. the data
/// messages of one patch that each carry a part of it.
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
    // the address map repeats whose item was not read: another item than the accumulated one (shared by all scopes)
    private readonly List<string> _skipped;
    // true: the values are added to those of the earlier reads (shared by all scopes, see Begin)
    private readonly StrongBox<bool> _accumulate;
    // true for the scope of an item that is not read: its values are passed over
    private readonly bool _ignore;

    /// <param name="node">The node the scope represents; null for a scope above <paramref name="target"/>.</param>
    /// <param name="target">The instance that is read: the root of the instance tree.</param>
    public InstanceValueSink(InstanceNode? node, InstanceNode target)
        : this(node, target, [], [], [], new StrongBox<bool>(), ignore: false)
    {
    }

    private InstanceValueSink(InstanceNode? node, InstanceNode target, Dictionary<InstanceRepeat, int> itemCounts, List<InstanceField> read,
        List<string> skipped, StrongBox<bool> accumulate, bool ignore)
    {
        _node = node;
        _target = target;
        _itemCounts = itemCounts;
        _read = read;
        _skipped = skipped;
        _accumulate = accumulate;
        _ignore = ignore;
    }

    /// <summary>
    /// The address map repeats (display path and items) whose item was not read in the last read: the message addressed
    /// another item than the one the accumulated values are of. Empty when nothing was skipped.
    /// </summary>
    public IReadOnlyList<string> Skipped => _skipped;

    /// <summary>
    /// Prepares the instance tree for a read: the values of the (non-constant) fields are cleared, unless
    /// <paramref name="accumulate"/>: then the read adds to the values of the earlier reads. A repeat of an address map that
    /// holds values keeps its item then; a message for another item of it is not read into it (see <see cref="Skipped"/>).
    /// </summary>
    public void Begin(bool accumulate = false)
    {
        _accumulate.Value = accumulate;
        if (!accumulate)
        {
            foreach (var field in _target.Fields().Where(field => !field.IsConstant))
            {
                field.Value = String.Empty;
            }
        }
        _itemCounts.Clear();
        _read.Clear();
        _skipped.Clear();
    }

    /// <summary>
    /// Removes the items of the open repeats that were not read. After a failed read (<paramref name="completed"/> false),
    /// or when the values are accumulated, only the repeats that were reached are trimmed: the others keep their items.
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
            else if (completed && !_accumulate.Value)
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
    private void RevealRead() => ReadNodeIn(_target)?.Reveal();

    /// <summary>
    /// After <see cref="End"/>: the nearest node at or below <paramref name="scope"/> that holds all the fields below it that
    /// received a value (a group when only one field was read), e.g. the patch of an address map a DT1 message addressed;
    /// null when no field below it received a value.
    /// </summary>
    public InstanceNode? ReadNodeIn(InstanceNode scope)
    {
        // the nodes from the scope down to each read field that is still in the tree (not removed with its repeat item)
        var fields = scope.Fields().ToHashSet();
        var paths = _read.Where(fields.Contains).Select(field => Ancestors(field, scope)).ToList();
        if (paths.Count == 0)
        {
            return null;
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
        if (common.Count == 0)
        {
            return null;
        }
        // the field itself has nothing to expand: its group when only one field was read
        return common[^1] is InstanceField && common.Count > 1 ? common[^2] : common[^1];
    }

    // the nodes from the top node down to the node
    private static List<InstanceNode> Ancestors(InstanceNode node, InstanceNode top)
    {
        var path = new List<InstanceNode>();
        for (InstanceNode? current = node; current is not null; current = current.Parent)
        {
            path.Insert(0, current);
            if (ReferenceEquals(current, top))
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
            field.Value = DataTypeValueValidator.Format(value.Value);
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
        if (_accumulate.Value && repeat is { IsAddressed: true, Items: [var shown] } && repeat.ItemIndex != index && HasValues(shown))
        {
            // the accumulated values are of another item of the address space: they stay one consistent item
            _skipped.Add($"{repeat.DisplayPath}: item {index} (the values are of item {repeat.ItemIndex})");
            return new InstanceValueSink(null, _target, _itemCounts, _read, _skipped, _accumulate, ignore: true);
        }
        _itemCounts[repeat] = Math.Max(_itemCounts.GetValueOrDefault(repeat), index + 1);
        return Scope(repeat.GetOrAddItem(index));
    }

    // a field below the node has a value that was read or typed (not a constant)
    private static bool HasValues(InstanceNode node) => node.Fields().Any(field => !field.IsConstant && field.Value.Length > 0);

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

    private InstanceValueSink Scope(InstanceNode? node) => new(node, _target, _itemCounts, _read, _skipped, _accumulate, _ignore);

    /// <summary>
    /// The instance node of the plan node in <paramref name="context"/>: a member of this scope (by name) or, above the
    /// instance, the instance itself when the plan path matches.
    /// </summary>
    private InstanceNode? Find(ValueContext context) => _node switch
    {
        _ when _ignore => null,
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
}
