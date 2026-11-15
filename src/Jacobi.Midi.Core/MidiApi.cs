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

    /// <summary>
    /// Starts MIDI Capability Inquiry as an initiator on a connection of this API (open or not yet opened), on a UMP
    /// group of the endpoint; the connection stays the caller's.
    /// </summary>
    /// <param name="group">The group the MIDI-CI messages are sent and received on (e.g. the group of a DIN port).</param>
    /// <exception cref="NotSupportedException">The platform has no MIDI-CI.</exception>
    /// <exception cref="ArgumentException">The connection is not of this API.</exception>
    IMidiCapabilityInquiry CreateCapabilityInquiry(IMidiConnection connection, MidiGroup group);
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
    /// <summary>
    /// The directions of the endpoint as a whole: the union over its groups. A group can still be one-way
    /// (see <see cref="GetGroupDirections"/>).
    /// </summary>
    public MidiDirections? Directions { get; init; }
    public bool? SupportsMidi1 { get; init; }
    public bool? SupportsMidi2 { get; init; }
    public MidiProtocol? ActiveProtocol { get; init; }
    public bool IsDiscoveryComplete { get; init; }
    public ImmutableArray<MidiFunctionBlockInfo> FunctionBlocks { get; init; } = [];
    public ImmutableArray<MidiGroupTerminalBlockInfo> GroupTerminalBlocks { get; init; } = [];

    /// <summary>
    /// The name of the blocks that include <paramref name="group"/> in one of <paramref name="directions"/>: the active
    /// function blocks when the endpoint declares function blocks, otherwise its group terminal blocks.
    /// Distinct names are joined with " / ". Null when no such block includes the group.
    /// </summary>
    public string? GetGroupName(MidiGroup group, MidiDirections directions = MidiDirections.Duplex)
    {
        var distinct = DeclaringBlocks(directions)
            .Where(block => block.Includes(group) && !String.IsNullOrWhiteSpace(block.Name))
            .Select(block => block.Name).Distinct().ToArray();
        return distinct.Length > 0 ? String.Join(" / ", distinct) : null;
    }

    /// <summary>
    /// The groups the endpoint declares in one of <paramref name="directions"/>: those of its active function blocks when
    /// the endpoint declares function blocks, otherwise those of its group terminal blocks. In ascending order; empty when
    /// the endpoint declares no such blocks.
    /// </summary>
    public ImmutableArray<MidiGroup> GetDeclaredGroups(MidiDirections directions = MidiDirections.Duplex)
        => [.. Enumerable.Range(0, 16)
            .Select(value => new MidiGroup((byte)value))
            .Where(group => DeclaringBlocks(directions).Any(block => block.Includes(group)))];

    /// <summary>
    /// The directions of the blocks that include <paramref name="group"/>: the active function blocks when the endpoint
    /// declares function blocks, otherwise its group terminal blocks. <see cref="MidiDirections.None"/> when no block
    /// includes the group; null when the endpoint declares no blocks (the directions are unknown).
    /// </summary>
    public MidiDirections? GetGroupDirections(MidiGroup group)
    {
        var blocks = DeclaringBlocks(MidiDirections.Duplex).ToArray();
        if (blocks.Length == 0)
            return null;
        return blocks.Where(block => block.Includes(group))
            .Aggregate(MidiDirections.None, (value, block) => value | block.Directions);
    }

    // function blocks supersede group terminal blocks; only the blocks with one of the directions
    // (Duplex selects all blocks, also those with an unknown direction)
    private IEnumerable<(string Name, MidiDirections Directions, Func<MidiGroup, bool> Includes)> DeclaringBlocks(MidiDirections directions)
        => (FunctionBlocks.Length > 0
            ? FunctionBlocks.Where(block => block.IsActive).Select(block => (block.Name, block.Directions, (Func<MidiGroup, bool>)block.IncludesGroup))
            : GroupTerminalBlocks.Select(block => (block.Name, block.Directions, (Func<MidiGroup, bool>)block.IncludesGroup)))
            .Where(block => directions == MidiDirections.Duplex || (block.Directions & directions) != 0);
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
/// Callbacks are serialized per connection.
/// </summary>
public delegate void MidiPacketReceivedHandler(ReadOnlySpan<StampedPacket> packets);

/// <summary>
/// Session owns all connections. Closing the session closes all connections.
/// </summary>
public interface IMidiSession : IAsyncDisposable
{
    /// <summary>
    /// Creates a connection to the endpoint; it is not open yet. Subscribe to its events, then call
    /// <see cref="IMidiConnection.Open"/>. <see cref="MidiSessionExtensions.Connect"/> creates and opens in one call.
    /// </summary>
    Task<IMidiConnection> CreateConnection(string endpointId, MidiConnectionOptions options, CancellationToken ct);

    /// <summary>
    /// Stable snapshot of the connections of the session, also those not opened yet. Faulted connections remain until
    /// explicitly disposed.
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
    /// The packets received from the endpoint (a connection that receives), raised on a background thread; the calls
    /// are serialized per connection and every subscriber gets the same packets. Packets received while there is no
    /// subscriber are not kept: subscribe before sending what the device answers. An exception of a subscriber faults
    /// the connection.
    /// </summary>
    event MidiPacketReceivedHandler? PacketsReceived;

    /// <summary>
    /// Opens the connection, once; packets are received and sending is possible from then on. Subscribe to the events
    /// before, so nothing is missed. When opening fails the connection is faulted (see <see cref="Error"/>); dispose it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection was opened before.</exception>
    Task Open(CancellationToken ct);

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

/// <summary>
/// Conveniences on <see cref="IMidiSession"/>.
/// </summary>
public static class MidiSessionExtensions
{
    /// <summary>
    /// Creates and opens a connection to the endpoint. Packets received before the caller subscribes to
    /// <see cref="IMidiConnection.PacketsReceived"/> are not kept; use <see cref="IMidiSession.CreateConnection"/> and
    /// <see cref="IMidiConnection.Open"/> to subscribe first. A connection that fails to open is disposed.
    /// </summary>
    public static async Task<IMidiConnection> Connect(this IMidiSession session, string endpointId, MidiConnectionOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        var connection = await session.CreateConnection(endpointId, options, ct).ConfigureAwait(false);
        try
        {
            await connection.Open(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
