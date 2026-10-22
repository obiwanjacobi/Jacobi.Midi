using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Runtime settings of an exchange that do not belong in the schema.
/// </summary>
public sealed record MessageExchangeOptions
{
    /// <summary>The UMP group the device is on.</summary>
    public byte Group { get; init; }

    /// <summary>How long to wait for the data of a request.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// What happened to a block of a transfer.
/// </summary>
public enum AddressBlockStatus
{
    Completed,
    TimedOut,
    Failed,
}

/// <summary>
/// The outcome of one block of a transfer.
/// </summary>
public sealed record AddressBlockResult(AddressBlock Block, AddressBlockStatus Status, Exception? Error = null);

/// <summary>
/// The outcome of a transfer per block.
/// </summary>
public sealed record AddressTransferResult(IReadOnlyList<AddressBlockResult> Blocks)
{
    public bool IsComplete => Blocks.All(b => b.Status == AddressBlockStatus.Completed);
}
