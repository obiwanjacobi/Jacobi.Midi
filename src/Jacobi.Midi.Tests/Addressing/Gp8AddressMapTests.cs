using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

public class Gp8AddressMapTests
{
    private const int PatchSize = 0x32;
    private const int MemoryStart = 0x2000;   // wire 40 00
    private const int Stride = 0x40;          // wire 00 40

    private static AddressMap CreateMap()
    {
        var schemas = TestHelpers.CreateSchemas();
        var serializer = new SerializerBuilder()
            .AddSchemas(schemas)
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();
        return AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap"));
    }

    [Fact]
    public void Create_FindsTheRolandMessages()
    {
        var map = CreateMap();

        Assert.Equal(new AddressSpace(2, 7), map.Protocol.AddressSpace);
        var request = Assert.Single(map.Protocol.Requests);
        Assert.Equal("Message.RQ1", request.Name);
        Assert.Equal(AddressUnit.Range, request.Unit);
        var data = Assert.Single(map.Protocol.DataMessages);
        Assert.Equal("Message.DT1", data.Name);
        Assert.Equal(TimeSpan.FromMilliseconds(20), data.Interval);
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
    public void GetAddressRange_Fields()
    {
        var map = CreateMap();

        // 0x15-0x16: a 2-byte (14 bit) field
        var time = map.CreateRange("AddressMap.Memory.Patch.DigitalDelay.DTime", new InstancePath([1]));
        Assert.Equal(new AddressRange(MemoryStart + Stride + 0x15, 2), map.GetAddressRange(time));

        // 0x21-0x30: the name
        var name = map.CreateRange("AddressMap.Temporary.Name");
        Assert.Equal(new AddressRange(0x21, 16), map.GetAddressRange(name));

        // a bit in a bit-packed byte occupies the whole byte
        var phaser = map.CreateRange("AddressMap.Temporary.EffectSwiches1.PhaserOn");
        Assert.Equal(new AddressRange(0, 1), map.GetAddressRange(phaser));
    }

    [Fact]
    public void Resolve_OneParameter()
    {
        var map = CreateMap();

        // 0x2145 (wire 42 45): item 5, offset 0x05
        var match = map.Resolve(new AddressRange(0x2145, 1));

        Assert.True(match.IsAligned);
        Assert.Empty(match.Unmapped);
        var block = Assert.IsType<AddressBlock>(match.Block);
        Assert.Equal("AddressMap.Memory.Patch.DynamicFilter.UpDown", block.Nodes.First.Path.Value);
        Assert.Equal(new InstancePath([5]), block.Nodes.FromInstance);
        Assert.Same(block.Nodes.First, block.Nodes.Last);
    }

    [Fact]
    public void Resolve_WholePatch()
    {
        var map = CreateMap();

        var match = map.Resolve(new AddressRange(MemoryStart + Stride, PatchSize));

        Assert.True(match.IsAligned);
        var block = Assert.IsType<AddressBlock>(match.Block);
        Assert.Equal(new AddressRange(MemoryStart + Stride, PatchSize), block.Addresses);
        Assert.Equal("AddressMap.Memory.Patch.EffectSwiches1.PhaserOn", block.Nodes.First.Path.Value);
        Assert.Equal("AddressMap.Memory.Patch.EndOfString", block.Nodes.Last.Path.Value);
        Assert.Equal(new InstancePath([1]), block.Nodes.FromInstance);
        Assert.Equal(new InstancePath([1]), block.Nodes.ToInstance);
    }

    [Fact]
    public void Resolve_GapAndPartialField()
    {
        var map = CreateMap();

        // the unused addresses after the temporary patch
        var gap = map.Resolve(new AddressRange(PatchSize, Stride - PatchSize));
        Assert.Null(gap.Block);
        Assert.Equal([new AddressRange(PatchSize, Stride - PatchSize)], gap.Unmapped);

        // the LSB of DTime only
        var partial = map.Resolve(new AddressRange(0x16, 1));
        Assert.False(partial.IsAligned);
        Assert.Equal(new AddressRange(0x15, 2), partial.Block!.Addresses);

        // from the last parameter into the gap
        var overrun = map.Resolve(new AddressRange(0x31, 4));
        Assert.False(overrun.IsAligned);
        Assert.Equal([new AddressRange(0x32, 3)], overrun.Unmapped);
    }

    [Fact]
    public void WidenToAtomic_ParameterBecomesPatch()
    {
        var map = CreateMap();

        var rate = map.CreateRange("AddressMap.Memory.Patch.Phaser.Rate", new InstancePath([3]));
        var widened = map.WidenToAtomic(rate);

        Assert.Equal(new AddressRange(MemoryStart + 3 * Stride, PatchSize), map.GetAddressRange(widened));
    }

    [Fact]
    public void Split_RangeMessage_SplitsAtGaps()
    {
        var map = CreateMap();
        var request = map.Protocol.Requests[0];

        // three memory patches: each is followed by unused addresses
        var range = map.CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));
        var blocks = map.Split(range, request);

        Assert.Equal(
            [
                new AddressRange(MemoryStart, PatchSize),
                new AddressRange(MemoryStart + Stride, PatchSize),
                new AddressRange(MemoryStart + 2 * Stride, PatchSize),
            ],
            blocks.Select(b => b.Addresses));
    }

    [Fact]
    public void Split_FieldMessage_SharesBitPackedBytes()
    {
        var map = CreateMap();
        var perField = map.Protocol.DataMessages[0] with { Unit = AddressUnit.Field };

        var range = map.CreateRange("AddressMap.Temporary.EffectSwiches1", default, "AddressMap.Temporary.DynamicFilter", default);
        var blocks = map.Split(range, perField);

        // 0x00 (4 bits), 0x01 (4 bits), 0x02-0x05 (one parameter each)
        Assert.Equal([0L, 1, 2, 3, 4, 5], blocks.Select(b => b.Addresses.Start));
        Assert.All(blocks, b => Assert.Equal(1, b.Addresses.Size));
        Assert.Equal("AddressMap.Temporary.EffectSwiches1.ChorusOn", blocks[0].Nodes.Last.Path.Value);
    }

    [Fact]
    public void Split_MaxDataSize()
    {
        var map = CreateMap();
        var small = map.Protocol.DataMessages[0] with { MaxDataSize = 0x10 };

        // the temporary patch is not atomic-split: the patch is atomic, so it stays one block
        var blocks = map.Split(map.CreateRange("AddressMap.Temporary"), small);

        Assert.Equal([new AddressRange(0, PatchSize)], blocks.Select(b => b.Addresses));
    }

    [Fact]
    public void SelectBody_ByModelId()
    {
        var map = CreateMap();

        Assert.Equal(new SchemaName("Gp8.AddressMap").FullName,
            map.Protocol.SelectBody(new DeviceValues { ["deviceId"] = 0, ["modelId"] = 0x13 }).FullName);
        Assert.Throws<InvalidOperationException>(() => map.Protocol.SelectBody(new DeviceValues { ["modelId"] = 0x14 }));
    }

    [Fact]
    public void CreateRequest_Rq1()
    {
        var map = CreateMap();
        var device = map.Device.With(new DeviceValues { ["deviceId"] = 0 });

        var bytes = map.Protocol.CreateRequest(map.Protocol.Requests[0], new AddressRange(MemoryStart + Stride, PatchSize), device);

        // address 40 40, size 00 32: checksum = 128 - ((0x40 + 0x40 + 0x00 + 0x32) % 128) = 0x4E
        Assert.Equal(Convert.FromHexString("F0410013114040003" + "24EF7"), bytes);
    }

    [Fact]
    public void CreateRequest_AddressIsBigEndian()
    {
        var map = CreateMap();
        var device = map.Device.With(new DeviceValues { ["deviceId"] = 0 });

        var bytes = map.Protocol.CreateRequest(map.Protocol.Requests[0], new AddressRange(1, 0x80), device);

        // address 00 01, size 01 00: checksum = 128 - ((0x00 + 0x01 + 0x01 + 0x00) % 128) = 0x7E
        Assert.Equal("F041001311000101007EF7", Convert.ToHexString(bytes));
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
        // padded with spaces to 16 characters
        Assert.Equal(" Dig Chorus     ", name.Value);
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

    [Fact]
    public void CreateData_NotOneGroup_Throws()
    {
        var map = CreateMap();
        var device = map.Device.With(new DeviceValues { ["deviceId"] = 0 });

        // Rate and Depth of the phaser: two of its three fields
        var block = map.Resolve(new AddressRange(0x0D, 2)).Block!;

        Assert.Throws<NotSupportedException>(() => map.CreateData(map.Protocol.DataMessages[0], block, new FieldValueStore(), device));
    }
}
