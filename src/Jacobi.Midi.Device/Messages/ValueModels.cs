using Jacobi.BinarySerializer;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// Forwards the calls for open repeats and open choices of a value model that wraps another one (a scope of it) to
/// that inner scope, and wraps the scope of the result. When the inner scope does not implement the open interface,
/// the call does what the serializer does for such a scope: on read a repeat without a count is entered with
/// <c>EnterRepeat(context, -1)</c> and read item by item with the serializer's own rule; on write such a repeat, and an
/// open choice, is an error.
/// </summary>
internal static class ValueModels
{
    public static OpenRepeatSinkResult EnterOpenRepeat(IValueSink inner, OpenRepeatReadContext context, Func<IValueSink, IValueSink> wrap)
    {
        if (inner is not IOpenRepeatSink open)
        {
            return OpenRepeatSinkResult.ItemByItem(wrap(inner.EnterRepeat(context, -1)));
        }
        var entered = open.EnterOpenRepeat(context);
        return entered.WithScope(wrap(entered.Scope));
    }

    public static OpenRepeatAction NextItem(IValueSink inner, OpenRepeatReadContext context, int index)
        => inner is IOpenRepeatSink open ? open.NextItem(context, index) : OpenRepeatAction.Default;

    public static OpenRepeatSourceResult EnterOpenRepeat(IValueSource inner, RepeatContext context, Func<IValueSource, IValueSource> wrap)
    {
        if (inner is not IOpenRepeatSource open)
        {
            throw new InvalidOperationException($"'{context.Path}': the value model does not implement {nameof(IOpenRepeatSource)}, which a repeat without a count requires.");
        }
        var entered = open.EnterOpenRepeat(context);
        return entered.WithScope(wrap(entered.Scope));
    }

    public static OpenRepeatAction NextItem(IValueSource inner, RepeatContext context, int index)
        => inner is IOpenRepeatSource open ? open.NextItem(context, index) : OpenRepeatAction.Default;

    public static OpenChoiceSinkResult EnterOpenChoice(IValueSink inner, OpenChoiceContext context, Func<IValueSink, IValueSink> wrap)
    {
        if (inner is not IOpenChoiceSink open)
        {
            throw new InvalidOperationException($"'{context.Path}': the value model does not implement {nameof(IOpenChoiceSink)}, which an open choice requires.");
        }
        var selected = open.EnterOpenChoice(context);
        return selected.WithScope(wrap(selected.Scope));
    }

    public static OpenChoiceSourceResult EnterOpenChoice(IValueSource inner, OpenChoiceContext context, Func<IValueSource, IValueSource> wrap)
    {
        if (inner is not IOpenChoiceSource open)
        {
            throw new InvalidOperationException($"'{context.Path}': the value model does not implement {nameof(IOpenChoiceSource)}, which an open choice requires.");
        }
        var selected = open.EnterOpenChoice(context);
        return selected.WithScope(wrap(selected.Scope));
    }
}
