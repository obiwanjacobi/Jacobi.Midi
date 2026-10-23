using System.Collections.Immutable;

namespace Jacobi.Midi.Core;

public interface IMidiApi
{
    Task<bool> EnsureIsAvailable();

    /// <summary>
    /// Number of ticks per second in this API's monotonic timestamp domain.
    /// </summary>
    ulong TimestampFrequency { get; }

    MidiTimestamp GetTimestamp();

    Task<IMidiSession> CreateSession(MidiSessionOptions options, CancellationToken ct);

    /// <summary>
    /// Returns after initial enumeration. Initial endpoints also produce Added notifications.
    /// The handler may run before this method returns. Cancellation only cancels startup.
    /// </summary>
    Task<IMidiEndpointObserver> WatchMidiEndpoints(EndpointChangedHandler handler, CancellationToken ct);
}

public sealed class MidiSessionOptions
{
    public required string Name { get; init; }
}

public delegate void EndpointChangedHandler(MidiEndpointChangedArgs args);

public interface IMidiEndpointObserver : IAsyncDisposable
{
    /// <summary>
    /// Stable snapshot of the latest inventory, updated before change notifications.
    /// </summary>
    IReadOnlyList<MidiEndpointInfo> Endpoints { get; }

    /// <summary>
    /// Waits for shutdown or observation/callback failure. Cancellation cancels only the wait,
    /// not observation; dispose the observer to stop it.
    /// </summary>
    Task WaitForCompletion(CancellationToken ct);

    /// <summary>
    /// Refreshes the snapshot from the platform's endpoint inventory without sending MIDI inquiry messages.
    /// </summary>
    Task Refresh(CancellationToken ct);
}

[Flags]
public enum MidiDirections
{
    None = 0,
    Receive = 1,
    Send = 2,
    Duplex = Receive | Send
}

public enum MidiProtocol
{
    Midi1 = 1,
    Midi2 = 2
}

/// <summary>
/// Describes an addressable endpoint, not necessarily an identified physical instrument.
/// Directions are from the application's perspective; IDs are opaque backend identifiers.
/// </summary>
public sealed record MidiEndpointInfo(
    string Id,
    string Name,
    MidiEndpointCapabilities Capabilities)
{
    public string? Transport { get; init; }
}

public sealed record MidiEndpointCapabilities
{
    public MidiDirections? Directions { get; init; }
    public bool? SupportsMidi1 { get; init; }
    public bool? SupportsMidi2 { get; init; }
    public MidiProtocol? ActiveProtocol { get; init; }
    public bool IsDiscoveryComplete { get; init; }
    public ImmutableArray<MidiFunctionBlockInfo> FunctionBlocks { get; init; } = [];
    public ImmutableArray<MidiGroupTerminalBlockInfo> GroupTerminalBlocks { get; init; } = [];

    /// <summary>
    /// The name of the blocks that include <paramref name="group"/>: the active function blocks when the endpoint
    /// declares function blocks, otherwise its group terminal blocks. Distinct names are joined with " / ".
    /// Null when no block includes the group.
    /// </summary>
    public string? GetGroupName(MidiGroup group)
    {
        var distinct = DeclaringBlocks()
            .Where(block => block.Includes(group) && !String.IsNullOrWhiteSpace(block.Name))
            .Select(block => block.Name).Distinct().ToArray();
        return distinct.Length > 0 ? String.Join(" / ", distinct) : null;
    }

    /// <summary>
    /// The groups the endpoint declares: those of its active function blocks when the endpoint declares function blocks,
    /// otherwise those of its group terminal blocks. In ascending order; empty when the endpoint declares no blocks.
    /// </summary>
    public ImmutableArray<MidiGroup> GetDeclaredGroups()
        => [.. Enumerable.Range(0, 16)
            .Select(value => new MidiGroup((byte)value))
            .Where(group => DeclaringBlocks().Any(block => block.Includes(group)))];

    // function blocks supersede group terminal blocks
    private IEnumerable<(string Name, Func<MidiGroup, bool> Includes)> DeclaringBlocks()
        => FunctionBlocks.Length > 0
            ? FunctionBlocks.Where(block => block.IsActive).Select(block => (block.Name, (Func<MidiGroup, bool>)block.IncludesGroup))
            : GroupTerminalBlocks.Select(block => (block.Name, (Func<MidiGroup, bool>)block.IncludesGroup));
}

/// <summary>
/// A MIDI 2.0 function block declared by the endpoint itself (through UMP endpoint discovery).
/// </summary>
public sealed record MidiFunctionBlockInfo(
    byte Number,
    string Name,
    bool IsActive,
    MidiDirections Directions,
    MidiGroup FirstGroup,
    byte GroupCount)
{
    public bool IncludesGroup(MidiGroup group)
        => group.Value >= FirstGroup.Value && group.Value < FirstGroup.Value + GroupCount;
}

/// <summary>
/// A group terminal block declared by the transport (e.g. the USB descriptors), such as a MIDI 1.0 port mapped to a group.
/// Function blocks, when the endpoint declares them, supersede group terminal blocks.
/// </summary>
public sealed record MidiGroupTerminalBlockInfo(
    byte Number,
    string Name,
    MidiDirections Directions,
    MidiGroup FirstGroup,
    byte GroupCount)
{
    public bool IncludesGroup(MidiGroup group)
        => group.Value >= FirstGroup.Value && group.Value < FirstGroup.Value + GroupCount;
}

public enum MidiEndpointChangeType
{
    Added,
    Removed,
    Updated
}

public sealed class MidiEndpointChangedArgs(
    MidiEndpointChangeType changeType, MidiEndpointInfo endpoint) : EventArgs
{
    public MidiEndpointChangeType ChangeType { get; } = changeType;
    public MidiEndpointInfo Endpoint { get; } = endpoint;
}

public sealed class MidiConnectionOptions
{
    public MidiDirections Directions { get; init; } = MidiDirections.Duplex;
}

/// <summary>
/// Packet spans are valid only during the synchronous callback. Copy packets to retain them.
/// Callbacks are serialized per connection and may start before Connect returns.
/// </summary>
public delegate void MidiPacketReceivedHandler(ReadOnlySpan<StampedPacket> packets);

/// <summary>
/// Session owns all connections. Closing the session closes all connections.
/// </summary>
public interface IMidiSession : IAsyncDisposable
{
    Task<IMidiConnection> Connect(
        string endpointId, MidiConnectionOptions options,
        MidiPacketReceivedHandler? reciever, CancellationToken ct);

    /// <summary>
    /// Stable snapshot. Faulted connections remain until explicitly disposed.
    /// </summary>
    IReadOnlyList<IMidiConnection> OpenConnections { get; }
}

public enum MidiConnectionState
{
    Open,
    Closed,
    Faulted,
}

public sealed class MidiConnectionStateChangedArgs(
    MidiConnectionState oldState, MidiConnectionState newState, Exception? error = null) : EventArgs
{
    public MidiConnectionState OldState { get; } = oldState;
    public MidiConnectionState NewState { get; } = newState;
    public Exception? Error { get; } = error;
}

public interface IMidiConnection : IAsyncDisposable
{
    MidiEndpointInfo EndpointInfo { get; }
    MidiDirections Directions { get; }
    MidiConnectionState ConnectionState { get; }
    Exception? Error { get; }
    event EventHandler<MidiConnectionStateChangedArgs>? ConnectionStateChanged;

    /// <summary>
    /// Sends immediately. Completion means acceptance, not device acknowledgement.
    /// Cancellation or failure can leave a partially submitted batch; do not blindly retry.
    /// </summary>
    ValueTask Send(ReadOnlyMemory<UniversalPacket> packets, CancellationToken ct);

    /// <summary>
    /// Submits a batch in nondecreasing timestamp order using the API clock.
    /// Zero means immediately. Cancellation does not retract accepted packets.
    /// </summary>
    ValueTask SendScheduled(ReadOnlyMemory<StampedPacket> packets, CancellationToken ct);
}

