using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// Wraps the client's sink of a whole message (envelope and body, e.g. the instance of an editor) for reading a data
/// message: forwards all values, lets the client select the body of the data open choice and then narrows that body
/// to the block the message carries, as its map recognised it (see <see cref="MessageMaps.ReadMessage"/>). Other
/// open choices and the client's open repeats are left to the client.
/// </summary>
internal sealed class ClientBodySink(IValueSink inner, SchemaPath dataChoice, Func<OpenChoiceSinkResult, OpenChoiceSinkResult> narrow)
    : IOpenChoiceSink, IOpenRepeatSink
{
    public void SetField(FieldContext context, LogicalField value) => inner.SetField(context, value);

    public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
    {
        if (inner is not IOpenChoiceSink openChoice)
        {
            throw new InvalidOperationException($"'{context.Path}': the value model does not support open choices.");
        }
        var selected = openChoice.EnterOpenChoice(context);
        return String.Equals(context.Node.Path.Value, dataChoice.Value, StringComparison.OrdinalIgnoreCase)
            ? narrow(selected)
            : selected with { Scope = Wrap(selected.Scope) };
    }

    public OpenRepeatAction NextItem(OpenRepeatReadContext context)
        => inner is IOpenRepeatSink openSink ? openSink.NextItem(context) : OpenRepeatAction.Default;

    public IValueSink EnterGroup(GroupContext context) => Wrap(inner.EnterGroup(context));
    public IValueSink EnterItem(RepeatContext context, int index, int count) => Wrap(inner.EnterItem(context, index, count));
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => Wrap(inner.EnterChoice(context, selectedIndex));
    public void ExitGroup(GroupContext context) => inner.ExitGroup(context);
    public void ExitItem(RepeatContext context, int index, int count) => inner.ExitItem(context, index, count);
    public void ExitChoice(ChoiceContext context, int selectedIndex) => inner.ExitChoice(context, selectedIndex);

    private ClientBodySink Wrap(IValueSink scope) => new(scope, dataChoice, narrow);
}
