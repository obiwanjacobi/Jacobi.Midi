using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Rigs;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The device state with the GP-8 bulk dump (128 DT1 messages of 59 bytes, one memory patch each, device id 0):
/// loading the dump, the change notifications, and saving ranges as the same messages.
/// </summary>
public class DeviceStateTests
{
    private const int MessageSize = 59;
    private const string Patch = "AddressMap.Memory.Patch";

    private static readonly byte[] Dump = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Schema", "gp8-sysex.syx"));

    private static DeviceState CreateState(long deviceId = 0)
        => new(new DeviceInstance
        {
            Alias = "gp8",
            MapRoot = "Gp8.AddressMap",
            DeviceValues = new Dictionary<string, long> { ["deviceId"] = deviceId },
        }, ConsoleLibrary.BuiltInLibrary);

    private static InstancePath Item(int index) => new([index]);

    private static PlanRange Patches(DeviceState state, int first, int last)
        => state.Plan.CreateRange(Patch, Item(first), Patch, Item(last));

    [Fact]
    public void LoadDump_DecodesEveryPatch()
    {
        var state = CreateState();

        var result = state.LoadDump(Dump);

        Assert.Equal(new DumpLoadResult(128, 128, []), result with { Warnings = [] });
        Assert.Empty(result.Warnings);
        Assert.True(state.TryGetValue($"{Patch}.Name", Item(0), out var name));
        Assert.Equal(" Dig Chorus", name.Value);
        Assert.True(state.TryGetValue(new ParameterReference("gp8", $"{Patch}.DigitalDelay.DTime", Item(0)), out var time));
        Assert.Equal((ushort)300, time.Value);
    }

    [Fact]
    public void LoadDump_ReportsTheValuesWithTheirOrigin()
    {
        var state = CreateState();
        var changes = new List<ValueChangedEventArgs>();
        state.ValueChanged += (_, change) => changes.Add(change);

        state.LoadDump(Dump);

        Assert.Equal(state.Count, changes.Count);
        Assert.All(changes, change => Assert.Equal(ValueOrigin.Dump, change.Origin));
        Assert.Contains(changes, change => change.Path == $"{Patch}.Name" && change.Instance == Item(127));
    }

    [Fact]
    public void LoadDump_OfAnotherDevice_DecodesNothing()
    {
        var state = CreateState(deviceId: 5);

        var result = state.LoadDump(Dump);

        Assert.Equal(0, result.Decoded);
        Assert.Equal(128, result.Warnings.Count);
        Assert.Contains("Message 1 (59 bytes) is not data of 'Gp8.AddressMap' for this device", result.Warnings[0]);
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void LoadDump_SkipsOtherMessages()
    {
        var state = CreateState();
        byte[] identityRequest = [0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7];

        var result = state.LoadDump([.. Dump.AsSpan(0, MessageSize), .. identityRequest]);

        Assert.Equal(2, result.Messages);
        Assert.Equal(1, result.Decoded);
        Assert.Contains("Message 2 (6 bytes)", Assert.Single(result.Warnings));
    }

    [Fact]
    public void SaveDump_OfWhatWasLoaded_GivesTheSameMessages_ExceptValuesTheSchemaDoesNotDescribe()
    {
        var state = CreateState();
        state.LoadDump(Dump);

        var saved = state.SaveDump(Patches(state, 0, 127));

        // These patches hold values that are not options of their data type: DynamicFilter.UpDown 0x3F and 0x02
        // (patches 76 and 121, options 0 and 100) and Compressor.Turbo 0x4E and 0x20 (patches 85 and 102, switch100).
        // They are read as the optionsDefault, which is written as 0: the dump is not reproduced for them.
        int[] notDescribed = [76, 85, 102, 121];
        Assert.Equal(Dump.Length, saved.Length);
        var differing = Enumerable.Range(0, 128)
            .Where(index => !Dump.AsSpan(index * MessageSize, MessageSize).SequenceEqual(saved.AsSpan(index * MessageSize, MessageSize)));
        Assert.Equal(notDescribed, differing);
    }

    [Fact]
    public void SaveDump_OfOnePatch_GivesItsMessage()
    {
        var state = CreateState();
        state.LoadDump(Dump);

        var saved = state.SaveDump(Patches(state, 5, 5));

        Assert.Equal(Convert.ToHexString(Dump.AsSpan(5 * MessageSize, MessageSize)), Convert.ToHexString(saved));
    }

    [Fact]
    public void SaveDump_HasTheValuesTheUserSet()
    {
        var state = CreateState();
        state.LoadDump(Dump.AsSpan(0, 3 * MessageSize).ToArray());
        state.SetValue($"{Patch}.Name", Item(2), new LogicalField("Name", typeof(string), "My Patch"), ValueOrigin.User);

        var other = CreateState();
        other.LoadDump(state.SaveDump(Patches(state, 2, 2)));

        Assert.True(other.TryGetValue($"{Patch}.Name", Item(2), out var name));
        Assert.Equal("My Patch", name.Value);
    }

    [Fact]
    public void SaveDump_WithUnknownValues_IsRejected()
    {
        var state = CreateState();

        Assert.Throws<InvalidOperationException>(() => state.SaveDump(Patches(state, 0, 0)));
    }

    [Fact]
    public void SetValue_OfTheKnownValue_ReportsNothing()
    {
        var state = CreateState();
        var name = new LogicalField("Name", typeof(string), "Same");
        state.SetValue($"{Patch}.Name", Item(0), name, ValueOrigin.User);
        var changes = 0;
        state.ValueChanged += (_, _) => changes++;

        state.SetValue($"{Patch}.Name", Item(0), name with { }, ValueOrigin.Device);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void TryGetValue_OfAnotherDevice_IsRejected()
    {
        var state = CreateState();

        Assert.Throws<ArgumentException>(() => state.TryGetValue(new ParameterReference("synth", $"{Patch}.Name", Item(0)), out _));
    }

    [Fact]
    public void DeviceState_NeedsAnAddressMap()
    {
        var device = new DeviceInstance { Alias = "x", MapRoot = "Roland.Message16" };

        Assert.Throws<ArgumentException>(() => new DeviceState(device, ConsoleLibrary.BuiltInLibrary));
    }

    [Fact]
    public void LoadDump_WithoutTheRequiredDeviceValues_IsRejected()
    {
        var state = new DeviceState(new DeviceInstance { Alias = "gp8", MapRoot = "Gp8.AddressMap" }, ConsoleLibrary.BuiltInLibrary);

        var exception = Assert.Throws<InvalidOperationException>(() => state.LoadDump(Dump));

        Assert.Contains("deviceId", exception.Message);
    }
}
