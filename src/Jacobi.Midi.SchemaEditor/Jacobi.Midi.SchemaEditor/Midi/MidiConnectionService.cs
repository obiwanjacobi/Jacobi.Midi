using Jacobi.Midi.Core;

namespace Jacobi.Midi.SchemaEditor.Midi;

/// <summary>
/// Owns the Midi.Core objects the Schema Editor works with: the endpoint observer,
/// the (lazily created) session and the single active connection.
/// </summary>
/// <remarks>
/// This class is UI-agnostic: endpoint and packet callbacks arrive on background threads
/// and must be marshalled by the caller. Calls are not meant to overlap; the caller serializes them.
/// </remarks>
internal sealed class MidiConnectionService(IMidiApi api, string sessionName) : IAsyncDisposable
{
    private IMidiEndpointObserver? _observer;
    private IMidiSession? _session;

    /// <summary>
    /// The active connection, or null when not connected.
    /// </summary>
    public IMidiConnection? Connection { get; private set; }

    /// <summary>
    /// Starts watching MIDI endpoints. Returns false when the MIDI backend is not available.
    /// The handler receives an Added notification for each initial endpoint (on a background thread).
    /// </summary>
    public async Task<bool> StartWatching(EndpointChangedHandler handler, CancellationToken ct)
    {
        if (_observer is not null)
            return true;
        if (!await api.EnsureIsAvailable().ConfigureAwait(false))
            return false;

        _observer = await api.WatchMidiEndpoints(handler, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Completes when endpoint observation stops; faults when observation failed.
    /// </summary>
    public Task WaitForWatchCompletion(CancellationToken ct)
        => _observer?.WaitForCompletion(ct) ?? Task.CompletedTask;

    /// <summary>
    /// Opens a connection to the endpoint in all directions it supports (Duplex when unknown).
    /// The receiver is only used when the connection can receive.
    /// </summary>
    public async Task<IMidiConnection> Connect(MidiEndpointInfo endpoint, MidiPacketReceivedHandler receiver, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Connection is not null)
            throw new InvalidOperationException("A MIDI connection is already open. Disconnect first.");

        _session ??= await api.CreateSession(new MidiSessionOptions { Name = sessionName }, ct).ConfigureAwait(false);

        var directions = GetConnectDirections(endpoint);
        var options = new MidiConnectionOptions { Directions = directions };
        var connectionReceiver = directions.HasFlag(MidiDirections.Receive) ? receiver : null;
        Connection = await _session.Connect(endpoint.Id, options, connectionReceiver, ct).ConfigureAwait(false);
        return Connection;
    }

    /// <summary>
    /// Closes the active connection, if any. The session stays open for the next connection.
    /// </summary>
    public async Task Disconnect()
    {
        var connection = Connection;
        Connection = null;
        if (connection is not null)
            await connection.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Disconnect().ConfigureAwait(false);
        }
        finally
        {
            var session = _session;
            var observer = _observer;
            _session = null;
            _observer = null;
            try
            {
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (observer is not null)
                    await observer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    // Capabilities.Directions is null when the backend does not know; assume Duplex then.
    private static MidiDirections GetConnectDirections(MidiEndpointInfo endpoint)
        => endpoint.Capabilities.Directions is { } directions && (directions & MidiDirections.Duplex) != MidiDirections.None
            ? directions & MidiDirections.Duplex
            : MidiDirections.Duplex;
}
