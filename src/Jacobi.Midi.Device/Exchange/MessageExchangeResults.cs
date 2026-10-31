using Jacobi.BinarySerializer;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;

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
/// The outcome of one block of a transfer.
/// </summary>
public sealed record BlockResult(AddressBlock Block, BlockStatus Status, Exception? Error = null)
{
    /// <summary>Why the transfer did not complete, as written by the exchange; null when it completed.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// The outcome of a transfer per block. The first block that does not complete ends the transfer: the blocks after it
/// are reported with the same status, as not transferred.
/// </summary>
public sealed record TransferResult(IReadOnlyList<BlockResult> Blocks)
{
    public bool IsComplete => Blocks.All(b => b.Status == BlockStatus.Completed);
}
