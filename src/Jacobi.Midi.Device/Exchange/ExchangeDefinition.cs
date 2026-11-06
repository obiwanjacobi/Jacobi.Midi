using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// The central names of the most important exchange patterns. An app asks for a pattern by its name, whatever the
/// device; the exchange file of the device (<see cref="ExchangeDefinitionSet"/>) says how the pattern goes for it.
/// </summary>
public static class ExchangeNames
{
    /// <summary>Get the data of a range from the device.</summary>
    public const string RequestData = "requestData";

    /// <summary>Give the data of a range to the device.</summary>
    public const string SendData = "sendData";
}

/// <summary>
/// One exchange file: the exchanges of one map root (<see cref="For"/>), each a pattern of messages for that device.
/// The file refers to the schemas (the root and the messages); a schema never refers to a file.
/// </summary>
public sealed class ExchangeDefinitionSet
{
    /// <summary>The schema name of the map root whose ranges these exchanges transfer, e.g. 'Gx700.AddressMap'.</summary>
    public required SchemaName For { get; init; }

    public required IReadOnlyList<ExchangeDefinition> Exchanges { get; init; }

    /// <summary>Where the file was read from (a resource or a path), to report problems.</summary>
    public required string Source { get; init; }

    /// <summary>The exchange with the name (case-insensitive); null when the file has none.</summary>
    public ExchangeDefinition? Find(string name)
        => Exchanges.FirstOrDefault(exchange => String.Equals(exchange.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One exchange: a plain state machine of turns that transfers one block of a range. It runs once per block, from its
/// first turn; the root's message map splits the range into blocks for its split message (the first message it sends,
/// else the first data message it receives).
/// It names messages and events only: building and recognising the messages is the content's job.
/// </summary>
public sealed record ExchangeDefinition
{
    /// <summary>A standard name (<see cref="ExchangeNames"/>) or a name of the file's own.</summary>
    public required string Name { get; init; }

    /// <summary>How long a turn waits for an event.</summary>
    public required TimeSpan ResponseTimeout { get; init; }

    /// <summary>The least time between two messages sent to the device.</summary>
    public required TimeSpan MessageInterval { get; init; }

    /// <summary>The turns; the first one is the start.</summary>
    public required IReadOnlyList<ExchangeTurn> Turns { get; init; }

    /// <summary>The turn with the name (case-insensitive); null when there is none.</summary>
    public ExchangeTurn? Find(string turn)
        => Turns.FirstOrDefault(candidate => String.Equals(candidate.Name, turn, StringComparison.OrdinalIgnoreCase));

    /// <summary>The messages the exchange names: sent, received and expected.</summary>
    public IEnumerable<SchemaName> Messages
        => Turns.SelectMany(turn => (turn.Send is { } send ? [send] : Enumerable.Empty<SchemaName>())
            .Concat(turn.Receive)
            .Concat(turn.Transitions.Where(transition => transition.Message is not null).Select(transition => transition.Message!.Value)))
            .Distinct();

    public override string ToString() => Name;
}

/// <summary>
/// One turn of an exchange, a turn in the conversation with the device: on entry it sends a message (optional), then
/// it waits for the events it has a transition for. While in the turn, the data messages it receives are accepted for
/// the block.
/// </summary>
public sealed record ExchangeTurn
{
    public required string Name { get; init; }

    /// <summary>The message to build for the block and send on entry; null: the turn only waits.</summary>
    public SchemaName? Send { get; init; }

    /// <summary>The data messages whose data is accepted for the block and counts towards <see cref="ExchangeEvent.Received"/>.</summary>
    public IReadOnlyList<SchemaName> Receive { get; init; } = [];

    /// <summary>
    /// What happens on an event, in order: the first transition that matches is taken. Without one for
    /// <see cref="ExchangeEvent.Sent"/>, <see cref="ExchangeEvent.Received"/> or <see cref="ExchangeEvent.Timeout"/> the
    /// default applies (see <see cref="Transition"/>).
    /// </summary>
    public IReadOnlyList<ExchangeTransition> Transitions { get; init; } = [];

    /// <summary>True when the turn waits for nothing after its send: it has no <see cref="Receive"/> and no transitions.</summary>
    public bool WaitsForNothing => Receive.Count == 0 && Transitions.Count == 0;

    /// <summary>
    /// The transition for an event other than a message: the turn's own, else the default. The defaults:
    /// <list type="bullet">
    ///   <item><see cref="ExchangeEvent.Sent"/>: <see cref="BlockStatus.Completed"/>, only for a turn that sends and
    ///   waits for nothing (<see cref="WaitsForNothing"/>); else the turn waits for its other events (e.g. an ack).</item>
    ///   <item><see cref="ExchangeEvent.Received"/>: <see cref="BlockStatus.Completed"/>, for a turn with a <see cref="Receive"/>.</item>
    ///   <item><see cref="ExchangeEvent.Timeout"/>: <see cref="BlockStatus.TimedOut"/>.</item>
    /// </list>
    /// Null when there is no transition: the turn waits on.
    /// </summary>
    public ExchangeTransition? Transition(ExchangeEvent @event)
    {
        if (@event == ExchangeEvent.Message)
        {
            throw new ArgumentException("A message transition is found by its message.", nameof(@event));
        }
        return Transitions.FirstOrDefault(transition => transition.Event == @event)
            ?? @event switch
            {
                ExchangeEvent.Sent when Send is not null && WaitsForNothing => ExchangeTransition.SentCompleted,
                ExchangeEvent.Received when Receive.Count > 0 => ExchangeTransition.ReceivedCompleted,
                ExchangeEvent.Timeout => ExchangeTransition.TimedOut,
                _ => null,
            };
    }

    public override string ToString() => Name;
}

/// <summary>
/// What a turn of an exchange waits for.
/// </summary>
public enum ExchangeEvent
{
    /// <summary>The message of the turn was sent.</summary>
    Sent,
    /// <summary>Data accepted by a <c>receive</c> of the turn made the block complete.</summary>
    Received,
    /// <summary>No event within the timeout.</summary>
    Timeout,
    /// <summary>The device sent a message the transition names.</summary>
    Message,
}

/// <summary>
/// A transition of a turn: on the event (and for <see cref="ExchangeEvent.Message"/> the message) it enters its turn
/// again while it has retries left (<see cref="Retry"/>), then it goes to another turn (<see cref="Goto"/>) or ends the
/// run for the block (<see cref="Result"/>).
/// </summary>
public sealed record ExchangeTransition
{
    /// <summary>The default for <see cref="ExchangeEvent.Sent"/> in a turn that waits for nothing (see <see cref="ExchangeTurn.Transition"/>).</summary>
    internal static readonly ExchangeTransition SentCompleted = new() { Event = ExchangeEvent.Sent, Result = BlockStatus.Completed };

    /// <summary>The default for <see cref="ExchangeEvent.Received"/> (see <see cref="ExchangeTurn.Transition"/>).</summary>
    internal static readonly ExchangeTransition ReceivedCompleted = new() { Event = ExchangeEvent.Received, Result = BlockStatus.Completed };

    /// <summary>The default for <see cref="ExchangeEvent.Timeout"/> (see <see cref="ExchangeTurn.Transition"/>).</summary>
    internal static readonly ExchangeTransition TimedOut = new() { Event = ExchangeEvent.Timeout, Result = BlockStatus.TimedOut };

    public required ExchangeEvent Event { get; init; }

    /// <summary>The message of a <see cref="ExchangeEvent.Message"/> event; null for the other events.</summary>
    public SchemaName? Message { get; init; }

    /// <summary>
    /// The most times the transition enters its turn again (sending the turn's message again), per block; after that
    /// <see cref="Goto"/> or <see cref="Result"/> is taken. 0: no retries.
    /// </summary>
    public int Retry { get; init; }

    /// <summary>The next turn; null when the transition ends the run.</summary>
    public string? Goto { get; init; }

    /// <summary>How the run for the block ends; null when the transition goes to another turn.</summary>
    public BlockStatus? Result { get; init; }
}
