using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>A message from the device, recognised for the running exchange: the message it matched and, for a data message, its data.</summary>
internal readonly record struct ExchangeMessage(AddressMessage Message, ReceivedData? Data = null);

/// <summary>Action: build the message for the block and send it; then <see cref="ExchangeMachine.OnSent"/> is called.</summary>
internal readonly record struct SendMessage(AddressMessage Message);

/// <summary>Action: wait for a message from the device until the timeout (a new deadline).</summary>
internal readonly record struct WaitForMessage(TimeSpan Timeout);

/// <summary>Action: the message is not the state's business; keep waiting until the earlier deadline.</summary>
internal readonly record struct Ignore;

/// <summary>Action: the run for the block is finished with this status.</summary>
internal readonly record struct Complete(BlockStatus Status, string? Reason = null);

/// <summary>
/// What the message exchange does next for a block, as decided by the <see cref="ExchangeMachine"/>.
/// </summary>
internal union ExchangeAction(SendMessage, WaitForMessage, Ignore, Complete);

/// <summary>
/// Runs an <see cref="ExchangeDefinition"/> for one block: it gets events and answers with the next action. It never
/// touches the connection, timers or message bytes; <see cref="MessageExchange"/> executes the actions. It writes the
/// reason of a run that does not complete from the event and the state. Used for one block, from one thread at a time.
/// </summary>
internal sealed class ExchangeMachine(ExchangeDefinition definition, IReadOnlyDictionary<SchemaName, AddressMessage> messages)
{
    private ExchangeState _state = definition.States[0];

    /// <summary>The current state.</summary>
    public ExchangeState State => _state;

    /// <summary>The first action: enter the first state.</summary>
    public ExchangeAction Start() => Enter(definition.States[0]);

    /// <summary>True when the current state accepts the data of the message for the block (<c>receive</c>).</summary>
    public bool Receives(AddressMessage message)
        => _state.Receive.Any(name => ReferenceEquals(messages[name], message));

    /// <summary>The message of the state was sent.</summary>
    public ExchangeAction OnSent()
        => Find(ExchangeEvent.Sent) is { } transition
            ? Apply(transition, "'sent'")
            : new WaitForMessage(definition.ResponseTimeout);

    /// <summary>
    /// The state accepted data of the block (see <see cref="Receives"/>): <c>complete</c> when the block is now complete,
    /// else the message itself; without a transition for it the state waits on, with a new timeout.
    /// </summary>
    public ExchangeAction OnReceived(ExchangeMessage message, bool complete)
    {
        if (complete && Find(ExchangeEvent.Complete) is { } done)
        {
            return Apply(done, "'complete'");
        }
        return Find(ExchangeEvent.Message, message.Message) is { } transition
            ? Apply(transition, $"'{message.Message.FullName}'")
            : new WaitForMessage(definition.ResponseTimeout);
    }

    /// <summary>A message from the device that the state does not receive as data.</summary>
    public ExchangeAction OnMessage(ExchangeMessage message)
        => Find(ExchangeEvent.Message, message.Message) is { } transition
            ? Apply(transition, $"'{message.Message.FullName}'")
            : new Ignore();

    /// <summary>The timeout elapsed; without a transition for it the run ends as timed out.</summary>
    public ExchangeAction OnElapsed()
        => Find(ExchangeEvent.Timeout) is { } transition
            ? Apply(transition, "timeout")
            : new Complete(BlockStatus.TimedOut, Reason("timeout"));

    private ExchangeAction Enter(ExchangeState state)
    {
        _state = state;
        return state.Send is { } send
            ? new SendMessage(messages[send])
            : new WaitForMessage(definition.ResponseTimeout);
    }

    private ExchangeAction Apply(ExchangeTransition transition, string happened)
    {
        if (transition.Goto is { } target)
        {
            return Enter(definition.Find(target)
                ?? throw new InvalidOperationException($"'{definition.Name}', state '{_state.Name}': there is no state '{target}'."));
        }
        var status = transition.Result ?? BlockStatus.Failed;
        return new Complete(status, status == BlockStatus.Completed ? null : Reason(happened));
    }

    private string Reason(string happened) => $"{happened} in state '{_state.Name}' of '{definition.Name}'.";

    private ExchangeTransition? Find(ExchangeEvent @event, AddressMessage? message = null)
        => _state.Transitions.FirstOrDefault(transition => transition.Event == @event
            && (message is null || (transition.Message is { } name && ReferenceEquals(messages[name], message))));
}
