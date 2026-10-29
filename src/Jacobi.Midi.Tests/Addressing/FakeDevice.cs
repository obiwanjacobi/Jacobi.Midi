using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using static Jacobi.Midi.Tests.Addressing.TestDevice;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// A <see cref="TestDevice"/> for the exchange and session tests: answers a request with the whole patches it overlaps,
/// on a background thread, records the messages it receives, and can lose its connection (<see cref="Disconnect"/>).
/// </summary>
internal sealed class FakeDevice : ISysExPort
{
    public const string EditName = "Edit    ";

    private readonly Dictionary<long, byte[]> _patches = [];

    public FakeDevice()
    {
        _patches[0] = DataMessage(0, Patch(EditName));
        for (var i = 0; i < MemoryCount; i++)
        {
            _patches[PatchAddress(i)] = MemoryPatch(i);
        }
    }

    public bool Silent { get; init; }
    public List<byte[]> Requests { get; } = [];
    public List<byte[]> DataSets { get; } = [];

    public event SysExReceivedHandler? Received;

    public MidiConnectionState State { get; private set; } = MidiConnectionState.Open;

    public event EventHandler<MidiConnectionStateChangedArgs>? StateChanged;

    public static byte[] MemoryPatch(int item) => DataMessage(PatchAddress(item), Patch(PatchName(item)));

    public ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct)
    {
        var bytes = sysEx.ToArray();
        switch (bytes[4])
        {
            case 0x01:
                Requests.Add(bytes);
                var request = new AddressRange((bytes[5] << 7) | bytes[6], (bytes[7] << 7) | bytes[8]);
                var answers = _patches.Where(p => new AddressRange(p.Key, PatchSize).Overlaps(request)).Select(p => p.Value).ToList();
                if (!Silent)
                {
                    _ = Task.Run(() => answers.ForEach(answer => Received?.Invoke(answer)), ct);
                }
                break;
            case 0x02:
                DataSets.Add(bytes);
                break;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Plays data the device sends on its own.</summary>
    public void Push(byte[] sysEx) => Received?.Invoke(sysEx);

    /// <summary>Plays a lost connection (e.g. the cable is pulled).</summary>
    public void Disconnect()
    {
        var old = State;
        State = MidiConnectionState.Closed;
        StateChanged?.Invoke(this, new MidiConnectionStateChangedArgs(old, State));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Waits (up to a second) until the condition holds, e.g. until data the device pushed has arrived.</summary>
    public static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
