using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

/// <summary>
/// Tests the Boss GX-700 address map (Gx700.xml with the 4 byte address messages of Roland.xml)
/// against the bulk dumps of one patch and of the system.
/// </summary>
public class Gx700AddressMapTests
{
    private static readonly DeviceValues Device = new() { ["deviceId"] = 0 };

    private static AddressMap CreateMap()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();
        return AddressMap.Create(serializer, new SchemaName("Gx700.AddressMap"));
    }

    // the dump files hold one DT1 per section
    private static List<byte[]> DumpMessages(string fileName)
    {
        var file = File.ReadAllBytes(TestHelpers.PathOf("Schema/" + fileName));
        var messages = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < file.Length; i++)
        {
            if (file[i] == 0xF7)
            {
                messages.Add(file[start..(i + 1)]);
                start = i + 1;
            }
        }
        return messages;
    }

    private static FieldValueStore Decode(AddressMap map, byte[] message)
    {
        Assert.True(map.TryReadData(message, Device, out var data));
        var store = new FieldValueStore();
        data.Decode(store);
        return store;
    }

    private static object? Value(FieldValueStore store, string path, params int[] instance)
    {
        Assert.True(store.TryGet(path, new InstancePath(instance), out var value), path);
        return value.Value;
    }

    [Fact]
    public void Create_FindsTheRolandMessages()
    {
        var map = CreateMap();

        Assert.Equal(new AddressSpace(4, 7), map.Protocol.AddressSpace);
        Assert.Equal("Message32.RQ1", Assert.Single(map.Protocol.Requests).Name);
        Assert.Equal("Message32.DT1", Assert.Single(map.Protocol.DataMessages).Name);
    }

    [Theory]
    [InlineData("gx700-sysex-patch1.syx", 14)]
    [InlineData("gx700-sysex-system.syx", 3)]
    public void TryReadData_EverySection_RoundTrips(string fileName, int sectionCount)
    {
        var map = CreateMap();
        var messages = DumpMessages(fileName);
        Assert.Equal(sectionCount, messages.Count);

        foreach (var message in messages)
        {
            Assert.True(map.TryReadData(message, Device, out var data), Convert.ToHexString(message));
            var store = new FieldValueStore();
            data.Decode(store);

            var written = map.CreateData(data.Message, data.Block, store, data.Device);
            Assert.Equal(Convert.ToHexString(message), Convert.ToHexString(written));
        }
    }

    [Fact]
    public void TryReadData_Patch_Decode()
    {
        var map = CreateMap();
        var messages = DumpMessages("gx700-sysex-patch1.syx");

        var common = Decode(map, messages[0x00]);
        Assert.Equal("VINTAGE TWIN", Value(common, "AddressMap.Memory.Patch.Common.Name", 0));

        var preamp = Decode(map, messages[0x04]);
        Assert.Equal(true, Value(preamp, "AddressMap.Memory.Patch.Preamp.On", 0));
        Assert.Equal("Clean Twin", Value(preamp, "AddressMap.Memory.Patch.Preamp.Type", 0));

        // flanger resonance 07 08 = 7 * 16 + 8
        var modulation = Decode(map, messages[0x09]);
        Assert.Equal((byte)120, Value(modulation, "AddressMap.Memory.Patch.Modulation.Resonance", 0));

        var delay = Decode(map, messages[0x0A]);
        // tempo 04 06 = 4 * 16 + 6 (= 120 BPM)
        Assert.Equal((byte)70, Value(delay, "AddressMap.Memory.Patch.Delay.Tempo", 0));
        // time 02 31 = 2 * 128 + 49
        Assert.Equal((ushort)305, Value(delay, "AddressMap.Memory.Patch.Delay.TimeCenter", 0));
    }

    [Fact]
    public void TryReadData_System_Decode()
    {
        var map = CreateMap();
        var messages = DumpMessages("gx700-sysex-system.syx");

        var map1 = Decode(map, messages[1]);
        Assert.Equal((byte)1, Value(map1, "AddressMap.System.ProgramChangeMap1.Program.Patch", 0));
        Assert.Equal((byte)64, Value(map1, "AddressMap.System.ProgramChangeMap1.Program.Patch", 63));
        var map2 = Decode(map, messages[2]);
        Assert.Equal((byte)128, Value(map2, "AddressMap.System.ProgramChangeMap2.Program.Patch", 63));
    }
}
