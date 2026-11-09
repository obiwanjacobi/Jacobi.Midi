using Jacobi.BinarySerializer.Schema;
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

/// <summary>The part of an exchange a run is for: the turns per block, or the turns once per call around the blocks.</summary>
internal enum ExchangePart
{
    /// <summary><see cref="ExchangeDefinition.Turns"/>: once per block.</summary>
    Block,
    /// <summary><see cref="ExchangeDefinition.Before"/>: once per call, before the blocks.</summary>
    Before,
    /// <summary><see cref="ExchangeDefinition.After"/>: once per call, after the blocks.</summary>
    After,
}

/// <summary>
/// Runs one part of an <see cref="ExchangeDefinition"/> (<see cref="ExchangePart"/>: the turns of a block, or the
/// turns before or after the blocks): it gets events and answers with the next action. It never touches the connection,
/// timers or message bytes; <see cref="MessageExchange"/> executes the actions. It counts the retries of the transitions
/// (per run, as a machine is used for one run) and writes the reason of a run that does not complete from the event and
/// the turn. A <c>goto</c> stays in the part. Used for one run, from one thread at a time.
/// </summary>
internal sealed class ExchangeMachine(ExchangeDefinition definition, IReadOnlyDictionary<SchemaName, DeviceMessage> messages,
    ExchangePart part = ExchangePart.Block)
{
    private readonly IReadOnlyList<ExchangeTurn> _turns = part switch
    {
        ExchangePart.Before => definition.Before,
        ExchangePart.After => definition.After,
        _ => definition.Turns,
    };
    private ExchangeTurn? _turn;
    private readonly Dictionary<ExchangeTransition, int> _retries = new(ReferenceEqualityComparer.Instance);

    /// <summary>The current turn.</summary>
    public ExchangeTurn Turn => _turn ?? _turns[0];

    /// <summary>The first action: enter the first turn of the part.</summary>
    /// <exception cref="InvalidOperationException">The part has no turns.</exception>
    public ExchangeAction Start()
        => _turns.Count > 0 ? Enter(_turns[0]) : throw new InvalidOperationException($"'{definition.Name}' has no '{PartName}' turns.");

    /// <summary>True when the current turn accepts the data of the message for the block (<c>receive</c>).</summary>
    public bool Receives(DeviceMessage message)
        => Turn.Receive.Any(name => ReferenceEquals(messages[name], message));

    /// <summary>The message of the turn was sent; without a transition for it the turn waits for its other events.</summary>
    public ExchangeAction OnSent()
        => Turn.Transition(ExchangeEvent.Sent) is { } transition
            ? Apply(transition, "'sent'")
            : new WaitForMessage(definition.ResponseTimeout);

    /// <summary>
    /// The turn accepted data of the block (see <see cref="Receives"/>): <c>received</c> when the block is now complete,
    /// else the message itself; without a transition for it the turn waits on, with a new timeout.
    /// </summary>
    public ExchangeAction OnReceived(ExchangeMessage message, bool complete)
    {
        if (complete && Turn.Transition(ExchangeEvent.Received) is { } done)
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
        => Apply(Turn.Transition(ExchangeEvent.Timeout)!, "timeout");

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
            return Enter(Turn);
        }

        if (transition.Goto is { } target)
        {
            // a goto stays in its part
            return Enter(ExchangeDefinition.Find(_turns, target)
                ?? throw new InvalidOperationException($"'{definition.Name}', turn '{Turn.Name}'{PartText}: there is no turn '{target}'."));
        }
        var status = transition.Result ?? BlockStatus.Failed;
        return new Complete(status, status == BlockStatus.Completed ? null : Reason(happened, retries));
    }

    private string Reason(string happened, int retries = 0)
        => retries == 0
            ? $"{happened} in turn '{Turn.Name}'{PartText} of '{definition.Name}'."
            : $"{happened} in turn '{Turn.Name}'{PartText} of '{definition.Name}', after {retries} {(retries == 1 ? "retry" : "retries")}.";

    private string PartName => part == ExchangePart.Before ? "before" : "after";

    // the part in a reason: the turns before or after the blocks are named as such
    private string PartText => part == ExchangePart.Block ? String.Empty : $" ({PartName})";

    private ExchangeTransition? Find(DeviceMessage message)
        => Turn.Transitions.FirstOrDefault(transition => transition.Event == ExchangeEvent.Message
            && transition.Message is { } name && ReferenceEquals(messages[name], message));
}
