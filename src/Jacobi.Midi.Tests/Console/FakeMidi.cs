using Jacobi.Midi.Core;
using Jacobi.Midi.Messages.V2;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// A MIDI backend for the console tests: endpoints that are added and removed by the test, sessions and connections
/// that record what is sent, and a responder that plays the devices (a SysEx message in, the answer out).
/// </summary>
internal sealed class FakeMidiApi : IMidiApi
{
    private EndpointChangedHandler? _handler;

    public bool IsAvailable { get; init; } = true;

    /// <summary>The endpoints there are now.</summary>
    public List<MidiEndpointInfo> Endpoints { get; } = [];

    /// <summary>The sessions created, in order.</summary>
    public List<FakeMidiSession> Sessions { get; } = [];

    /// <summary>Answers a SysEx message sent on a connection (endpoint id, group, message); null: no answer.</summary>
    public Func<string, byte, byte[], byte[]?>? Responder { get; set; }

    public FakeEndpointObserver? Observer { get; private set; }

    public ulong TimestampFrequency => 1_000_000;

    public MidiTimestamp GetTimestamp() => default;

    public Task<bool> EnsureIsAvailable() => Task.FromResult(IsAvailable);

    public Task<IMidiSession> CreateSession(MidiSessionOptions options, CancellationToken ct)
    {
        var session = new FakeMidiSession(this);
        Sessions.Add(session);
        return Task.FromResult<IMidiSession>(session);
    }

    public Task<IMidiEndpointObserver> WatchMidiEndpoints(EndpointChangedHandler handler, CancellationToken ct)
    {
        _handler = handler;
        Observer = new FakeEndpointObserver(this);
        foreach (var endpoint in Endpoints.ToList())
            handler(new MidiEndpointChangedArgs(MidiEndpointChangeType.Added, endpoint));
        return Task.FromResult<IMidiEndpointObserver>(Observer);
    }

    public static MidiEndpointInfo Endpoint(string name) => new($"id-{name}", name, new MidiEndpointCapabilities());

    public void Add(MidiEndpointInfo endpoint)
    {
        Endpoints.Add(endpoint);
        _handler?.Invoke(new MidiEndpointChangedArgs(MidiEndpointChangeType.Added, endpoint));
    }

    public void Remove(MidiEndpointInfo endpoint)
    {
        Endpoints.Remove(endpoint);
        _handler?.Invoke(new MidiEndpointChangedArgs(MidiEndpointChangeType.Removed, endpoint));
    }

    /// <summary>All connections of all sessions, in the order they were opened.</summary>
    public IReadOnlyList<FakeMidiConnection> Connections => [.. Sessions.SelectMany(session => session.Connections)];
}

/// <summary>The endpoint watch of the <see cref="FakeMidiApi"/>.</summary>
internal sealed class FakeEndpointObserver(FakeMidiApi api) : IMidiEndpointObserver
{
    public bool IsDisposed { get; private set; }

    public IReadOnlyList<MidiEndpointInfo> Endpoints => [.. api.Endpoints];

    public Task WaitForCompletion(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

    public Task Refresh(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A session of the <see cref="FakeMidiApi"/>: connects to the endpoints that are there.</summary>
internal sealed class FakeMidiSession(FakeMidiApi api) : IMidiSession
{
    public List<FakeMidiConnection> Connections { get; } = [];

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<IMidiConnection> OpenConnections => [.. Connections.Where(connection => !connection.IsDisposed)];

    public Task<IMidiConnection> Connect(string endpointId, MidiConnectionOptions options, MidiPacketReceivedHandler? reciever, CancellationToken ct)
    {
        var endpoint = api.Endpoints.FirstOrDefault(endpoint => endpoint.Id == endpointId)
            ?? throw new InvalidOperationException($"No endpoint '{endpointId}'.");
        var connection = new FakeMidiConnection(api, endpoint, reciever);
        lock (Connections)
            Connections.Add(connection);
        return Task.FromResult<IMidiConnection>(connection);
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A connection of the <see cref="FakeMidiApi"/>: assembles the SysEx messages sent per group, records them, and
/// sends the responder's answer back on the same group.
/// </summary>
internal sealed class FakeMidiConnection(FakeMidiApi api, MidiEndpointInfo endpoint, MidiPacketReceivedHandler? receiver) : IMidiConnection
{
    private readonly Dictionary<byte, SysEx7Assembler> _assemblers = [];

    /// <summary>The SysEx messages sent, with their group.</summary>
    public List<(byte Group, byte[] SysEx)> Sent { get; } = [];

    public bool IsDisposed { get; private set; }

    public MidiEndpointInfo EndpointInfo => endpoint;

    public MidiDirections Directions => MidiDirections.Duplex;

    public MidiConnectionState ConnectionState { get; private set; } = MidiConnectionState.Open;

    public Exception? Error { get; private set; }

    public event EventHandler<MidiConnectionStateChangedArgs>? ConnectionStateChanged;

    /// <summary>The connection faults, as when the cable is pulled.</summary>
    public void Fault(string message)
    {
        Error = new IOException(message);
        ConnectionState = MidiConnectionState.Faulted;
        ConnectionStateChanged?.Invoke(this, new MidiConnectionStateChangedArgs(MidiConnectionState.Open, MidiConnectionState.Faulted, Error));
    }

    public ValueTask Send(ReadOnlyMemory<UniversalPacket> packets, CancellationToken ct)
    {
        foreach (var packet in packets.ToArray())
        {
            var group = (byte)((packet[0] >> 24) & 0x0F);
            if (!_assemblers.TryGetValue(group, out var assembler))
                _assemblers[group] = assembler = new SysEx7Assembler(group);
            if (assembler.TryAdd(packet, out var sysEx))
            {
                lock (Sent)
                    Sent.Add((group, sysEx));
                if (api.Responder?.Invoke(endpoint.Id, group, sysEx) is { } answer)
                    Receive(group, answer);
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask SendScheduled(ReadOnlyMemory<StampedPacket> packets, CancellationToken ct) => throw new NotSupportedException();

    private void Receive(byte group, byte[] sysEx)
    {
        var packets = SysEx7Message.FromSystemExclusive(group, sysEx)
            .Select(packet => new StampedPacket(new UniversalPacket([packet[0], packet[1]]), 0))
            .ToArray();
        receiver?.Invoke(packets);
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        ConnectionState = MidiConnectionState.Closed;
        return ValueTask.CompletedTask;
    }
}
