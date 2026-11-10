using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Runtime settings of an exchange that do not belong in the schema or the exchange file.
/// </summary>
public sealed record MessageExchangeOptions
{
    /// <summary>The UMP groups the device is on: the group to send on and the group to receive on.</summary>
    public MidiGroupPair Groups { get; init; }
}

/// <summary>
/// What an exchange may need from the app: the values of data that is sent and the value model for data that is
/// received. Which of the two an exchange needs follows from its definition; one exchange can use both.
/// </summary>
public sealed record ExchangeData
{
    /// <summary>The values of the data that is sent (paths and instances of the map plan).</summary>
    public IValueSource? Source { get; init; }

    /// <summary>Receives the data that is received (paths and instances of the map plan).</summary>
    public IValueSink? Sink { get; init; }
}

/// <summary>
/// An exchange that cannot run with a range: there is no definition for it, a message it names does not exist or
/// does not fit the map, a device value or the source or sink it needs is missing. Thrown before any message is sent;
/// all problems found are listed.
/// </summary>
public sealed class ExchangeException(string exchange, string root, IReadOnlyList<string> problems)
    : InvalidOperationException($"The exchange '{exchange}' cannot run for '{root}':{Environment.NewLine}{String.Join(Environment.NewLine, problems)}")
{
    /// <summary>The name of the exchange.</summary>
    public string Exchange { get; } = exchange;

    /// <summary>The map root of the range.</summary>
    public string Root { get; } = root;

    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// What happened to a block of a transfer.
/// </summary>
public enum BlockStatus
{
    Completed,
    TimedOut,
    /// <summary>The device refused the message (e.g. a nak), e.g. because its memory is protected.</summary>
    Rejected,
    Failed,
}

/// <summary>
/// The outcome of one block of a transfer: the nodes one message asks for or carries (a range of the plan of the
/// transferred range).
/// </summary>
public sealed record BlockResult(PlanRange Block, BlockStatus Status, Exception? Error = null)
{
    /// <summary>
    /// Why the transfer did not complete, as written by the exchange (with the first of <see cref="Unaccepted"/>, when
    /// there are any); null when it completed.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The messages of the device that arrived during the block but were not accepted, each with why: a near miss (its
    /// envelope and device match, but it cannot be read) or data of the device outside the block. Other traffic is not
    /// listed.
    /// </summary>
    public IReadOnlyList<string> Unaccepted { get; init; } = [];

    /// <summary>A block that was not transferred because the transfer ended at <paramref name="ended"/>; it gets the same status.</summary>
    internal static BlockResult NotTransferred(PlanRange block, BlockResult ended)
        => new(block, ended.Status) { Reason = $"Not transferred: the transfer ended at {Describe(ended.Block)}: {ended.Reason}" };

    /// <summary>The nodes of a block as text, e.g. 'AddressMap.Memory.Patch.Level[3]'..'AddressMap.Memory.Patch.Name[3]'.</summary>
    internal static string Describe(PlanRange block)
        => $"'{block.First.Path}{block.FromInstance}'..'{block.Last.Path}{block.ToInstance}'";
}

/// <summary>
/// The outcome of a transfer per block. The first block that does not complete ends the transfer: the blocks after it
/// are reported with the same status, as not transferred. The turns once per call of the exchange (its 'before' and
/// 'after') have a result of their own, for the whole range; they count in <see cref="IsComplete"/>.
/// </summary>
public sealed record TransferResult(IReadOnlyList<BlockResult> Blocks)
{
    /// <summary>The run of the exchange's turns before the blocks; null when it has none. When it does not complete, no block is transferred.</summary>
    public BlockResult? Before { get; init; }

    /// <summary>The run of the exchange's turns after the blocks; null when it has none, or when not all blocks completed (it did not run).</summary>
    public BlockResult? After { get; init; }

    /// <summary>True when every block, and the turns before and after them, completed.</summary>
    public bool IsComplete => Blocks.All(b => b.Status == BlockStatus.Completed)
        && Before?.Status is null or BlockStatus.Completed
        && After?.Status is null or BlockStatus.Completed;
}
