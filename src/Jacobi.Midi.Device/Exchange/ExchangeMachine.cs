using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>A message from the device, recognised for the running exchange: the message it matched and, for a data message, its data.</summary>
internal readonly record struct ExchangeMessage(DeviceMessage Message, ReceivedData? Data = null);

/// <summary>Action: build the message for the block and send it; then <see cref="ExchangeMachine.OnSent"/> is called.</summary>
internal readonly record struct SendMessage(DeviceMessage Message);

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
/// touches the connection, timers or message bytes; <see cref="MessageExchange"/> executes the actions. It counts the
/// retries of the transitions (per block, as a machine is used for one block) and writes the reason of a run that does
/// not complete from the event and the turn. Used for one block, from one thread at a time.
/// </summary>
internal sealed class ExchangeMachine(ExchangeDefinition definition, IReadOnlyDictionary<SchemaName, DeviceMessage> messages)
{
    private ExchangeTurn _turn = definition.Turns[0];
    private readonly Dictionary<ExchangeTransition, int> _retries = new(ReferenceEqualityComparer.Instance);

    /// <summary>The current turn.</summary>
    public ExchangeTurn Turn => _turn;

    /// <summary>The first action: enter the first turn.</summary>
    public ExchangeAction Start() => Enter(definition.Turns[0]);

    /// <summary>True when the current turn accepts the data of the message for the block (<c>receive</c>).</summary>
    public bool Receives(DeviceMessage message)
        => _turn.Receive.Any(name => ReferenceEquals(messages[name], message));

    /// <summary>The message of the turn was sent; without a transition for it the turn waits for its other events.</summary>
    public ExchangeAction OnSent()
        => _turn.Transition(ExchangeEvent.Sent) is { } transition
            ? Apply(transition, "'sent'")
            : new WaitForMessage(definition.ResponseTimeout);

    /// <summary>
    /// The turn accepted data of the block (see <see cref="Receives"/>): <c>received</c> when the block is now complete,
    /// else the message itself; without a transition for it the turn waits on, with a new timeout.
    /// </summary>
    public ExchangeAction OnReceived(ExchangeMessage message, bool complete)
    {
        if (complete && _turn.Transition(ExchangeEvent.Received) is { } done)
        {
            return Apply(done, "'received'");
        }
        return Find(message.Message) is { } transition
            ? Apply(transition, $"'{message.Message.FullName}'")
            : new WaitForMessage(definition.ResponseTimeout);
    }

    /// <summary>A message from the device that the turn does not receive as data.</summary>
    public ExchangeAction OnMessage(ExchangeMessage message)
        => Find(message.Message) is { } transition
            ? Apply(transition, $"'{message.Message.FullName}'")
            : new Ignore();

    /// <summary>The timeout elapsed; without a transition of its own the turn's default ends the run as timed out.</summary>
    public ExchangeAction OnElapsed()
        => Apply(_turn.Transition(ExchangeEvent.Timeout)!, "timeout");

    private ExchangeAction Enter(ExchangeTurn turn)
    {
        _turn = turn;
        return turn.Send is { } send
            ? new SendMessage(messages[send])
            : new WaitForMessage(definition.ResponseTimeout);
    }

    // a retry left: enter the turn again; else the goto or the result
    private ExchangeAction Apply(ExchangeTransition transition, string happened)
    {
        var retries = _retries.GetValueOrDefault(transition);
        if (retries < transition.Retry)
        {
            _retries[transition] = retries + 1;
            return Enter(_turn);
        }

        if (transition.Goto is { } target)
        {
            return Enter(definition.Find(target)
                ?? throw new InvalidOperationException($"'{definition.Name}', turn '{_turn.Name}': there is no turn '{target}'."));
        }
        var status = transition.Result ?? BlockStatus.Failed;
        return new Complete(status, status == BlockStatus.Completed ? null : Reason(happened, retries));
    }

    private string Reason(string happened, int retries = 0)
        => retries == 0
            ? $"{happened} in turn '{_turn.Name}' of '{definition.Name}'."
            : $"{happened} in turn '{_turn.Name}' of '{definition.Name}', after {retries} {(retries == 1 ? "retry" : "retries")}.";

    private ExchangeTransition? Find(DeviceMessage message)
        => _turn.Transitions.FirstOrDefault(transition => transition.Event == ExchangeEvent.Message
            && transition.Message is { } name && ReferenceEquals(messages[name], message));
}
