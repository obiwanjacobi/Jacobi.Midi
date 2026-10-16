using Jacobi.Midi.Core;
using Native = Windows.Devices.Midi2;
using Enumeration = Windows.Devices.Midi2.Enumeration;

namespace Jacobi.Midi.Windows;

internal sealed class WindowsMidiSession(Native.MidiSession native) : IMidiSession
{
    private readonly object _gate = new();
    private readonly object _nativeGate = new();
    private readonly SemaphoreSlim _opening = new(1, 1);
    private readonly List<WindowsMidiConnection> _connections = [];
    private Task? _disposeTask;

    public IReadOnlyList<IMidiConnection> OpenConnections
    {
        get
        {
            lock (_gate)
                return Array.AsReadOnly<IMidiConnection>(_connections.ToArray());
        }
    }

    public async Task<IMidiConnection> Connect(
        string endpointId, MidiConnectionOptions options, MidiPackedReceivedHandler? reciever, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Directions == MidiDirections.None || (options.Directions & ~MidiDirections.Duplex) != 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Specify Receive, Send, or Duplex.");
        if ((options.Directions & MidiDirections.Receive) != 0)
            ArgumentNullException.ThrowIfNull(reciever);

        await _opening.WaitAsync(ct).ConfigureAwait(false);
        WindowsMidiConnection? connection = null;
        Native.MidiEndpointConnection? nativeConnection = null;
        var succeeded = false;
        try
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            ct.ThrowIfCancellationRequested();
            var endpoint = Enumeration.MidiEndpointDeviceInformation.CreateFromEndpointDeviceId(endpointId)
                ?? throw new MidiException($"MIDI endpoint '{endpointId}' is no longer available.");
            var info = WindowsMidiEndpointMapper.Map(endpoint);
            if (info.Capabilities.Directions is { } available && (available & options.Directions) != options.Directions)
                throw new MidiException($"MIDI endpoint '{info.Name}' does not support the requested directions ({options.Directions}).");

            lock (_nativeGate)
                nativeConnection = native.CreateEndpointConnection(endpointId, new Native.MidiEndpointConnectionSettings(false, false))
                    ?? throw new MidiException($"Could not create a connection to MIDI endpoint '{info.Name}'.");
            connection = new WindowsMidiConnection(nativeConnection, info, options.Directions, reciever, Remove);
            connection.Open(ct);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
                ct.ThrowIfCancellationRequested();
                _connections.Add(connection);
            }
            succeeded = true;
            return connection;
        }
        finally
        {
            try
            {
                if (!succeeded && connection is not null)
                    await connection.DisposeAsync().ConfigureAwait(false);
                else if (!succeeded && nativeConnection is not null)
                {
                    lock (_nativeGate)
                        native.DisconnectEndpointConnection(nativeConnection.ConnectionId);
                }
            }
            finally
            {
                _opening.Release();
            }
        }
    }

    private void Remove(WindowsMidiConnection connection, Guid id)
    {
        try
        {
            lock (_nativeGate)
                native.DisconnectEndpointConnection(id);
        }
        finally
        {
            lock (_gate)
                _connections.Remove(connection);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCore));
    }

    private async Task DisposeCore()
    {
        await _opening.WaitAsync().ConfigureAwait(false);
        try
        {
            WindowsMidiConnection[] connections;
            lock (_gate)
                connections = _connections.ToArray();
            try
            {
                await Task.WhenAll(connections.Select(connection => connection.DisposeAsync().AsTask())).ConfigureAwait(false);
            }
            finally
            {
                lock (_nativeGate)
                    native.Dispose();
            }
        }
        finally
        {
            _opening.Release();
        }
    }
}
