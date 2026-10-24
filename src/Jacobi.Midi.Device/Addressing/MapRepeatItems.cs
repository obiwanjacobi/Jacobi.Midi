using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Decides how many items of a repeat in an address map are read or written. The repeats of a map have no count in
/// the schema (a dump may hold fewer items), so the value model ends them: after the number of items in the address
/// space (<c>midi.repeat</c>, or the constant count) and after the last item of the range or target that is transferred.
/// </summary>
internal sealed class MapRepeatItems
{
    private readonly AddressLayout _layout;
    private readonly SchemaPath _lastPath;
    private readonly InstancePath _lastInstance;

    private MapRepeatItems(AddressLayout layout, SchemaPath lastPath, InstancePath lastInstance)
    {
        _layout = layout;
        _lastPath = lastPath;
        _lastInstance = lastInstance;
    }

    /// <summary>The items up to the last node of a range of the map plan.</summary>
    public static MapRepeatItems Of(AddressLayout layout, PlanRange range) => new(layout, range.Last.Path, range.ToInstance);

    /// <summary>The items up to a group occurrence (the target of a data message body).</summary>
    public static MapRepeatItems Of(AddressLayout layout, SchemaPath target, InstancePath instance) => new(layout, target, instance);

    /// <summary>All the items of the address space (the whole map, e.g. a dump of it).</summary>
    public static MapRepeatItems All(AddressLayout layout) => new(layout, default, default);

    public OpenRepeatAction NextItem(OpenRepeatContext context)
    {
        if (_layout.Find(context.Repeat.Path) is not { IsRepeat: true } repeat)
        {
            return OpenRepeatAction.Default;
        }
        return context.Index <= LastIndex(repeat, context.Instance) ? OpenRepeatAction.Next : OpenRepeatAction.End;
    }

    // The last item index to visit. Only the occurrence of the repeat that holds the last node is cut short;
    // in any other occurrence all items are in plan order before the last node.
    private int LastIndex(AddressLayoutNode repeat, InstancePath instance)
    {
        var last = repeat.Count - 1;
        var depth = instance.Length;
        if (_lastPath.IsEmpty || !_lastPath.IsSameOrDescendantOf(repeat.Node.Path)
            || _lastInstance.Length <= depth || _lastInstance[depth] < 0)
        {
            return last;
        }
        for (var level = 0; level < depth; level++)
        {
            if (instance[level] != _lastInstance[level])
            {
                return last;
            }
        }
        return Math.Min(last, _lastInstance[depth]);
    }
}

/// <summary>
/// Wraps the client's value sink for reading the map: forwards all values and ends the repeats of the map
/// (see <see cref="MapRepeatItems"/>), also when a dump holds fewer items than the address space.
/// The other open repeats are left to the client's sink when it ends them (<see cref="IOpenRepeatSink"/>).
/// </summary>
internal sealed class MapRepeatSink(IValueSink inner, MapRepeatItems items) : IOpenRepeatSink
{
    public OpenRepeatAction NextItem(OpenRepeatReadContext context)
    {
        if (context.AtDefaultEnd)
        {
            return OpenRepeatAction.End;
        }
        var action = items.NextItem(context);
        return action == OpenRepeatAction.Default && inner is IOpenRepeatSink openSink ? openSink.NextItem(context) : action;
    }

    public void SetField(FieldContext context, LogicalField value) => inner.SetField(context, value);
    public IValueSink EnterGroup(GroupContext context) => new MapRepeatSink(inner.EnterGroup(context), items);
    public IValueSink EnterItem(RepeatContext context, int index, int count) => new MapRepeatSink(inner.EnterItem(context, index, count), items);
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => new MapRepeatSink(inner.EnterChoice(context, selectedIndex), items);
    public void ExitGroup(GroupContext context) => inner.ExitGroup(context);
    public void ExitItem(RepeatContext context, int index, int count) => inner.ExitItem(context, index, count);
    public void ExitChoice(ChoiceContext context, int selectedIndex) => inner.ExitChoice(context, selectedIndex);
}

/// <summary>
/// Wraps the client's value source for writing the map: forwards all values and ends the repeats of the map
/// (see <see cref="MapRepeatItems"/>). The other open repeats are left to the client's source when it ends them
/// (<see cref="IOpenRepeatSource"/>).
/// </summary>
internal sealed class MapRepeatSource(IValueSource inner, MapRepeatItems items) : IOpenRepeatSource
{
    public OpenRepeatAction NextItem(OpenRepeatContext context)
    {
        var action = items.NextItem(context);
        return action == OpenRepeatAction.Default && inner is IOpenRepeatSource openSource ? openSource.NextItem(context) : action;
    }

    public SourceResult GetField(FieldContext context) => inner.GetField(context);
    public IValueSource EnterGroup(GroupContext context) => new MapRepeatSource(inner.EnterGroup(context), items);
    public int GetCount(RepeatContext context) => inner.GetCount(context);
    public IValueSource EnterItem(RepeatContext context, int index) => new MapRepeatSource(inner.EnterItem(context, index), items);
    public int GetSelectedIndex(ChoiceContext context) => inner.GetSelectedIndex(context);
    public IValueSource EnterChoice(ChoiceContext context) => new MapRepeatSource(inner.EnterChoice(context), items);
}
