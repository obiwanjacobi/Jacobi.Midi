using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

/// <summary>
/// Tests the Roland GP-8 address map (Gp8.xml with the 2 byte address messages of Roland.xml)
/// against the bulk dump of the 128 memory patches.
/// </summary>
public class Gp8AddressMapTests
{
    private const int PatchSize = 0x32;
    private const int MemoryStart = 0x2000;   // wire 40 00
    private const int Stride = 0x40;          // wire 00 40

    private static AddressMap CreateMap()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();
        return AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap"));
    }

    [Fact]
    public void Create_FindsTheRolandMessages()
    {
        var map = CreateMap();

        Assert.Equal(new AddressSpace(2, 7), map.Protocol.AddressSpace);
        Assert.True(map.Message(new SchemaName("Roland.Message16.RQ1")).IsRequest);
        Assert.Equal("Message16.DT1", Assert.Single(map.Protocol.DataMessages).Name);
    }

    [Fact]
    public void Create_DeviceValues()
    {
        var map = CreateMap();

        Assert.Equal(0x13, map.Device["modelId"]);
        Assert.Equal(["deviceId"], map.RequiredDeviceValues);
    }

    [Fact]
    public void Range_CoversTemporaryAndMemory()
    {
        var map = CreateMap();

        Assert.Equal(new AddressRange(0, MemoryStart + 128 * Stride), map.Range);
    }

    [Fact]
    public void GetAddressRange_Patch()
    {
        var map = CreateMap();

        var temporary = map.CreateRange("AddressMap.Temporary");
        Assert.Equal(new AddressRange(0, PatchSize), map.GetAddressRange(temporary));

        var patch = map.CreateRange("AddressMap.Memory.Patch", new InstancePath([5]));
        Assert.Equal(new AddressRange(MemoryStart + 5 * Stride, PatchSize), map.GetAddressRange(patch));
    }

    [Fact]
    public void CreateRequest_Rq1()
    {
        var map = CreateMap();
        var device = map.Device.With(new DeviceValues { ["deviceId"] = 0 });

        var bytes = map.Protocol.CreateMessage(map.Message(new SchemaName("Roland.Message16.RQ1")), new AddressRange(MemoryStart + Stride, PatchSize), device);

        // address 40 40, size 00 32: checksum = 128 - ((0x40 + 0x40 + 0x00 + 0x32) % 128) = 0x4E
        Assert.Equal(Convert.FromHexString("F0410013114040003" + "24EF7"), bytes);
    }

    private static byte[] DumpMessage(int index)
    {
        // the bulk dump: 128 DT1 messages of 59 bytes, one memory patch each
        var file = File.ReadAllBytes(TestHelpers.PathOf("Schema/gp8-sysex.syx"));
        return file.AsSpan(index * 59, 59).ToArray();
    }

    [Fact]
    public void TryReadData_DumpMessage()
    {
        var map = CreateMap();

        Assert.True(map.TryReadData(DumpMessage(1), new DeviceValues { ["deviceId"] = 0 }, out var data));

        Assert.Equal(MemoryStart + Stride, data.Address);
        Assert.Equal(0x13, data.Device["modelId"]);
        Assert.Equal(new AddressRange(MemoryStart + Stride, PatchSize), data.Block.Addresses);
        Assert.Equal(new InstancePath([1]), data.Block.Nodes.FromInstance);
    }

    [Fact]
    public void TryReadData_Decode()
    {
        var map = CreateMap();
        Assert.True(map.TryReadData(DumpMessage(0), new DeviceValues { ["deviceId"] = 0 }, out var data));

        var store = new FieldValueStore();
        data.Decode(store);

        var item = new InstancePath([0]);
        Assert.True(store.TryGet("AddressMap.Memory.Patch.Name", item, out var name));
        // padded with spaces to 16 characters: the padding is trimmed (midi.string)
        Assert.Equal(" Dig Chorus", name.Value);
        // 0x15-0x16 = 02 2C: 2 * 128 + 44
        Assert.True(store.TryGet("AddressMap.Memory.Patch.DigitalDelay.DTime", item, out var time));
        Assert.Equal((ushort)300, time.Value);
        Assert.True(store.TryGet("AddressMap.Memory.Patch.EffectSwiches1.ChorusOn", item, out var chorus));
        Assert.Equal(true, chorus.Value);
    }

    [Fact]
    public void TryReadData_OtherDevice()
    {
        var map = CreateMap();

        // the dump is from device id 0
        Assert.False(map.TryReadData(DumpMessage(0), new DeviceValues { ["deviceId"] = 5 }, out _));

        // another model id selects no map
        var other = DumpMessage(0);
        other[3] = 0x14;
        Assert.False(map.TryReadData(other, new DeviceValues { ["deviceId"] = 0 }, out _));
    }

    [Fact]
    public void TryReadData_CorruptChecksum()
    {
        var map = CreateMap();
        var message = DumpMessage(0);
        message[^2] ^= 0x01;

        Assert.False(map.TryReadData(message, new DeviceValues { ["deviceId"] = 0 }, out _));
    }

    [Fact]
    public void CreateData_RoundTrip()
    {
        var map = CreateMap();
        var device = map.Device.With(new DeviceValues { ["deviceId"] = 0 });
        var original = DumpMessage(2);
        Assert.True(map.TryReadData(original, device, out var data));
        var store = new FieldValueStore();
        data.Decode(store);

        var bytes = map.CreateData(map.Protocol.DataMessages[0], data.Block, store, device);

        Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(bytes));
    }
}
