using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Runtime settings of an exchange that do not belong in the schema.
/// </summary>
public sealed record MessageExchangeOptions
{
    /// <summary>The UMP groups the device is on: the group to send on and the group to receive on.</summary>
    public MidiGroupPair Groups { get; init; }

    /// <summary>How long to wait for a message from the device (a reply to a request, an acknowledgement).</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The exchange protocol to use instead of the one the schemas select (<c>midi.exchange</c> or inferred);
    /// null uses the schemas.
    /// </summary>
    public IExchangeProtocol? Protocol { get; init; }
}

/// <summary>
/// What happened to a block of a transfer.
/// </summary>
public enum BlockStatus
{
    Completed,
    TimedOut,
    /// <summary>The device refused the message (a nak), e.g. because its memory is protected.</summary>
    Rejected,
    Failed,
}

/// <summary>
/// The outcome of one block of a transfer.
/// </summary>
public sealed record BlockResult(AddressBlock Block, BlockStatus Status, Exception? Error = null)
{
    /// <summary>Why the transfer did not complete, as reported by the exchange protocol; null when it completed.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// The outcome of a transfer per block.
/// </summary>
public sealed record TransferResult(IReadOnlyList<BlockResult> Blocks)
{
    public bool IsComplete => Blocks.All(b => b.Status == BlockStatus.Completed);
}
