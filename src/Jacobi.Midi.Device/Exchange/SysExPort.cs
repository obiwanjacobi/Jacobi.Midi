using Jacobi.Midi.Core;
using Jacobi.Midi.Messages.V2;

namespace Jacobi.Midi.Device.Exchange;

internal delegate void SysExReceivedHandler(ReadOnlyMemory<byte> sysEx);

/// <summary>
/// Sends and receives complete SysEx messages (F0 … F7). Implemented over the exchange's own
/// <see cref="IMidiConnection"/>, and by a fake in tests.
/// </summary>
internal interface ISysExPort : IAsyncDisposable
{
    ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct);

    /// <summary>Raised for each complete SysEx message received; may be raised on a background thread.</summary>
    event SysExReceivedHandler? Received;

    /// <summary>The state of the connection to the device.</summary>
    MidiConnectionState State { get; }

    /// <summary>Raised when the connection state changes (e.g. the cable is pulled); may be raised on a background thread.</summary>
    event EventHandler<MidiConnectionStateChangedArgs>? StateChanged;
}

/// <summary>
/// A SysEx port over a MIDI 2.0 connection: splits outgoing messages into SysEx7 packets on the send group of
/// <paramref name="groups"/> and assembles the incoming packets of its receive group into complete messages. <see cref="OnPacketsReceived"/> is the
/// receive handler of the connection; the connection is attached once it is open.
/// </summary>
internal sealed class MidiConnectionSysExPort(MidiGroupPair groups) : ISysExPort
{
    private readonly SysEx7Assembler _assembler = new(groups.Receive.Value);
    private readonly Lock _lock = new();
    private IMidiConnection? _connection;

    public IMidiConnection? Connection
    {
        get => _connection;
        set
        {
            if (_connection is not null)
            {
                _connection.ConnectionStateChanged -= OnConnectionStateChanged;
            }
            _connection = value;
            if (_connection is not null)
            {
                _connection.ConnectionStateChanged += OnConnectionStateChanged;
            }
        }
    }

    public event SysExReceivedHandler? Received;

    public MidiConnectionState State => _connection?.ConnectionState ?? MidiConnectionState.Closed;

    public event EventHandler<MidiConnectionStateChangedArgs>? StateChanged;

    private void OnConnectionStateChanged(object? sender, MidiConnectionStateChangedArgs args)
        => StateChanged?.Invoke(this, args);

    public async ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct)
    {
        var connection = Connection ?? throw new InvalidOperationException("The port has no connection.");
        var packets = SysEx7Message.FromSystemExclusive(groups.Send.Value, sysEx.Span)
            .Select(packet => new UniversalPacket([packet[0], packet[1]]))
            .ToArray();
        await connection.Send(packets, ct);
    }

    // called by the connection, serialized per connection, on a background thread
    public void OnPacketsReceived(ReadOnlySpan<StampedPacket> packets)
    {
        var messages = new List<byte[]>();
        lock (_lock)
        {
            foreach (var packet in packets)
            {
                if (packet.Packet.HasValue && _assembler.TryAdd(packet.Packet, out var sysEx))
                {
                    messages.Add(sysEx);
                }
            }
        }
        foreach (var message in messages)
        {
            Received?.Invoke(message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection is { } connection)
        {
            Connection = null;
            await connection.DisposeAsync();
        }
    }
}
