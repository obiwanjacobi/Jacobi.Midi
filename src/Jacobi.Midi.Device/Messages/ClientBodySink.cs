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
        => String.Equals(context.Node.Path.Value, dataChoice.Value, StringComparison.OrdinalIgnoreCase)
            ? narrow(ValueModels.EnterOpenChoice(inner, context, scope => scope))
            : ValueModels.EnterOpenChoice(inner, context, Wrap);

    public OpenRepeatSinkResult EnterOpenRepeat(OpenRepeatReadContext context) => ValueModels.EnterOpenRepeat(inner, context, Wrap);
    public OpenRepeatAction NextItem(OpenRepeatReadContext context, int index) => ValueModels.NextItem(inner, context, index);

    public IValueSink EnterGroup(GroupContext context) => Wrap(inner.EnterGroup(context));
    public void ExitGroup(GroupContext context) => inner.ExitGroup(context);
    public IValueSink EnterRepeat(RepeatContext context, int count) => Wrap(inner.EnterRepeat(context, count));
    public void ExitRepeat(RepeatContext context, int count) => inner.ExitRepeat(context, count);
    public IValueSink EnterItem(RepeatContext context, int index, int count) => Wrap(inner.EnterItem(context, index, count));
    public void ExitItem(RepeatContext context, int index, int count) => inner.ExitItem(context, index, count);
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => Wrap(inner.EnterChoice(context, selectedIndex));
    public void ExitChoice(ChoiceContext context, int selectedIndex) => inner.ExitChoice(context, selectedIndex);

    private ClientBodySink Wrap(IValueSink scope) => new(scope, dataChoice, narrow);
}
