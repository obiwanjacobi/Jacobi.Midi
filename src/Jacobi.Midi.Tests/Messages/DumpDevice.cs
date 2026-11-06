using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Exchange;

namespace Jacobi.Midi.Tests.Messages;

/// <summary>
/// Plays a dump device for the tests (manufacturer id 7D): answers a request with function code 10
/// (<c>F0 7D dd 10 F7</c>) with <see cref="Reply"/>, records what it gets, and sends a message on its own with
/// <see cref="Transmit"/>.
/// </summary>
internal sealed class DumpDevice : ISysExPort
{
    public byte[]? Reply { get; init; }
    public List<byte[]> Sent { get; } = [];

    public event SysExReceivedHandler? Received;

    public MidiConnectionState State => MidiConnectionState.Open;

    public event EventHandler<MidiConnectionStateChangedArgs>? StateChanged { add { } remove { } }

    public void Transmit(byte[] sysEx) => Received?.Invoke(sysEx);

    public ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct)
    {
        var bytes = sysEx.ToArray();
        Sent.Add(bytes);
        if (bytes[3] == 0x10 && Reply is { } reply)
        {
            _ = Task.Run(() => Received?.Invoke(reply), ct);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
