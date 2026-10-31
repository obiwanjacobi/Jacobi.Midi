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
/// One exchange: a plain state machine that transfers one block of a range. It runs once per block, from its first
/// state; the blocks are cut for its cutting message (the first message it sends that asks for or carries data).
/// It names messages and events only: building and recognising the messages is the content's job.
/// </summary>
public sealed record ExchangeDefinition
{
    /// <summary>A standard name (<see cref="ExchangeNames"/>) or a name of the file's own.</summary>
    public required string Name { get; init; }

    /// <summary>How long a state waits for an event.</summary>
    public required TimeSpan ResponseTimeout { get; init; }

    /// <summary>The least time between two messages sent to the device.</summary>
    public required TimeSpan MessageInterval { get; init; }

    /// <summary>The states; the first one is the start.</summary>
    public required IReadOnlyList<ExchangeState> States { get; init; }

    /// <summary>The state with the name (case-insensitive); null when there is none.</summary>
    public ExchangeState? Find(string state)
        => States.FirstOrDefault(candidate => String.Equals(candidate.Name, state, StringComparison.OrdinalIgnoreCase));

    /// <summary>The messages the exchange names: sent, received and expected.</summary>
    public IEnumerable<SchemaName> Messages
        => States.SelectMany(state => (state.Send is { } send ? [send] : Enumerable.Empty<SchemaName>())
            .Concat(state.Receive)
            .Concat(state.Transitions.Where(transition => transition.Message is not null).Select(transition => transition.Message!.Value)))
            .Distinct();

    public override string ToString() => Name;
}

/// <summary>
/// One state of an exchange: on entry it sends a message (optional), then it waits for the events it has a
/// transition for. While in the state, the data messages it receives are accepted for the block.
/// </summary>
public sealed record ExchangeState
{
    public required string Name { get; init; }

    /// <summary>The message to build for the block and send on entry; null: the state only waits.</summary>
    public SchemaName? Send { get; init; }

    /// <summary>The data messages whose data is accepted for the block and counts towards <see cref="ExchangeEvent.Complete"/>.</summary>
    public IReadOnlyList<SchemaName> Receive { get; init; } = [];

    /// <summary>What happens on an event, in order: the first transition that matches is taken.</summary>
    public IReadOnlyList<ExchangeTransition> Transitions { get; init; } = [];

    public override string ToString() => Name;
}

/// <summary>
/// What a state of an exchange waits for.
/// </summary>
public enum ExchangeEvent
{
    /// <summary>The message of the state was sent.</summary>
    Sent,
    /// <summary>Received data made the block complete.</summary>
    Complete,
    /// <summary>No event within the timeout.</summary>
    Timeout,
    /// <summary>The device sent a message the transition names.</summary>
    Message,
}

/// <summary>
/// A transition of a state: on the event (and for <see cref="ExchangeEvent.Message"/> the message) it goes to another
/// state (<see cref="Goto"/>) or ends the run for the block (<see cref="Result"/>).
/// </summary>
public sealed record ExchangeTransition
{
    public required ExchangeEvent Event { get; init; }

    /// <summary>The message of a <see cref="ExchangeEvent.Message"/> event; null for the other events.</summary>
    public SchemaName? Message { get; init; }

    /// <summary>The next state; null when the transition ends the run.</summary>
    public string? Goto { get; init; }

    /// <summary>How the run for the block ends; null when the transition goes to another state.</summary>
    public BlockStatus? Result { get; init; }
}
