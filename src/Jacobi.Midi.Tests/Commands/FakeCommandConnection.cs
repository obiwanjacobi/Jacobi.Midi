using Jacobi.Midi.Core;
using Jacobi.Midi.Tests.Console;

namespace Jacobi.Midi.Tests.Commands;

/// <summary>An open connection that records the packets sent and raises the packets the test receives.</summary>
internal sealed class FakeCommandConnection : IMidiConnection
{
    public List<UniversalPacket> Sent { get; } = [];

    public MidiEndpointInfo EndpointInfo { get; } = FakeMidiApi.Endpoint("synth");
    public MidiDirections Directions => MidiDirections.Duplex;
    public MidiConnectionState ConnectionState => MidiConnectionState.Open;
    public Exception? Error => null;

    public event EventHandler<MidiConnectionStateChangedArgs>? ConnectionStateChanged { add { } remove { } }
    public event MidiPacketReceivedHandler? PacketsReceived;

    public void Receive(UniversalPacket[] packets)
        => PacketsReceived?.Invoke([.. packets.Select(packet => new StampedPacket(packet, 0))]);

    public Task Open(CancellationToken ct) => Task.CompletedTask;

    public ValueTask Send(ReadOnlyMemory<UniversalPacket> packets, CancellationToken ct)
    {
        Sent.AddRange(packets.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask SendScheduled(ReadOnlyMemory<StampedPacket> packets, CancellationToken ct) => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
