using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;

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

    /// <summary>The items up to the last node of a range of the map plan (e.g. the target of a data message body).</summary>
    public static MapRepeatItems Of(AddressLayout layout, PlanRange range) => new(layout, range.Last.Path, range.EndInstance);

    /// <summary>The number of items of a repeat of the map to visit; null for a repeat that is not one of the map.</summary>
    public int? Count(RepeatContext context)
        => _layout.Find(context.Repeat.Path) is { IsRepeat: true } repeat ? LastIndex(repeat, context.Instance) + 1 : null;

    public OpenRepeatAction NextItem(RepeatContext context, int index)
        => Count(context) is { } count
            ? index < count ? OpenRepeatAction.Next : OpenRepeatAction.End
            : OpenRepeatAction.Default;

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
/// (see <see cref="MapRepeatItems"/>), also when a dump holds fewer items than the address space: they are read item
/// by item, the client's scope enters them with <c>EnterRepeat(context, -1)</c>. The other open repeats and the open
/// choices are left to the client's sink.
/// </summary>
internal sealed class MapRepeatSink(IValueSink inner, MapRepeatItems items) : IOpenRepeatSink, IOpenChoiceSink
{
    public OpenRepeatSinkResult EnterOpenRepeat(OpenRepeatReadContext context)
        => items.Count(context) is null
            ? ValueModels.EnterOpenRepeat(inner, context, Wrap)
            : OpenRepeatSinkResult.ItemByItem(Wrap(inner.EnterRepeat(context, -1)));

    public OpenRepeatAction NextItem(OpenRepeatReadContext context, int index)
    {
        if (context.AtDefaultEnd)
        {
            return OpenRepeatAction.End;
        }
        var action = items.NextItem(context, index);
        return action == OpenRepeatAction.Default ? ValueModels.NextItem(inner, context, index) : action;
    }

    public void SetField(FieldContext context, LogicalField value) => inner.SetField(context, value);
    public IValueSink EnterGroup(GroupContext context) => Wrap(inner.EnterGroup(context));
    public void ExitGroup(GroupContext context) => inner.ExitGroup(context);
    public IValueSink EnterRepeat(RepeatContext context, int count) => Wrap(inner.EnterRepeat(context, count));
    public void ExitRepeat(RepeatContext context, int count) => inner.ExitRepeat(context, count);
    public IValueSink EnterItem(RepeatContext context, int index, int count) => Wrap(inner.EnterItem(context, index, count));
    public void ExitItem(RepeatContext context, int index, int count) => inner.ExitItem(context, index, count);
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => Wrap(inner.EnterChoice(context, selectedIndex));
    public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context) => ValueModels.EnterOpenChoice(inner, context, Wrap);
    public void ExitChoice(ChoiceContext context, int selectedIndex) => inner.ExitChoice(context, selectedIndex);

    private MapRepeatSink Wrap(IValueSink scope) => new(scope, items);
}

/// <summary>
/// Wraps the client's value source for writing the map: forwards all values and gives the repeats of the map their
/// number of items (see <see cref="MapRepeatItems"/>): the client's scope enters them with that count. The other open
/// repeats and the open choices are left to the client's source.
/// </summary>
internal sealed class MapRepeatSource(IValueSource inner, MapRepeatItems items) : IOpenRepeatSource, IOpenChoiceSource
{
    public OpenRepeatSourceResult EnterOpenRepeat(RepeatContext context)
        => items.Count(context) is { } count
            ? OpenRepeatSourceResult.Counted(Wrap(inner.EnterRepeat(context, count)), count)
            : ValueModels.EnterOpenRepeat(inner, context, Wrap);

    public OpenRepeatAction NextItem(RepeatContext context, int index) => ValueModels.NextItem(inner, context, index);

    public SourceResult GetField(FieldContext context) => inner.GetField(context);
    public IValueSource EnterGroup(GroupContext context) => Wrap(inner.EnterGroup(context));
    public void ExitGroup(GroupContext context) => inner.ExitGroup(context);
    public IValueSource EnterRepeat(RepeatContext context, int count) => Wrap(inner.EnterRepeat(context, count));
    public void ExitRepeat(RepeatContext context, int count) => inner.ExitRepeat(context, count);
    public IValueSource EnterItem(RepeatContext context, int index, int count) => Wrap(inner.EnterItem(context, index, count));
    public void ExitItem(RepeatContext context, int index, int count) => inner.ExitItem(context, index, count);
    public IValueSource EnterChoice(ChoiceContext context, int selectedIndex) => Wrap(inner.EnterChoice(context, selectedIndex));
    public OpenChoiceSourceResult EnterOpenChoice(OpenChoiceContext context) => ValueModels.EnterOpenChoice(inner, context, Wrap);
    public void ExitChoice(ChoiceContext context, int selectedIndex) => inner.ExitChoice(context, selectedIndex);

    private MapRepeatSource Wrap(IValueSource scope) => new(scope, items);
}
