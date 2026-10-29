using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;
using static Jacobi.Midi.Tests.Addressing.TestDevice;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The address map on the stand-alone <see cref="TestDevice"/> schemas.
/// </summary>
public class AddressMapTests
{
    private static readonly DeviceValues Device0 = new() { ["deviceId"] = 0 };

    [Fact]
    public void Create_FindsTheMessages()
    {
        var map = CreateMap();

        Assert.Equal(new AddressSpace(2, 7), map.Protocol.AddressSpace);
        var request = Assert.Single(map.Protocol.Requests);
        Assert.Equal("Message.Request", request.Name);
        Assert.Equal(AddressUnit.Range, request.Unit);
        var data = Assert.Single(map.Protocol.DataMessages);
        Assert.Equal("Message.Data", data.Name);
        Assert.Equal(TimeSpan.FromMilliseconds(20), data.Interval);
    }

    [Fact]
    public void Create_DeviceValues()
    {
        var map = CreateMap();

        Assert.Equal(ModelId, map.Device["modelId"]);
        Assert.Equal(["deviceId"], map.RequiredDeviceValues);
    }

    [Fact]
    public void Range_CoversTheMap()
    {
        var map = CreateMap();

        Assert.Equal(new AddressRange(0, MemoryStart + MemoryCount * Stride), map.Range);
    }

    [Fact]
    public void GetAddressRange_Groups()
    {
        var map = CreateMap();

        Assert.Equal(new AddressRange(0, PatchSize), map.GetAddressRange(map.CreateRange("AddressMap.Edit")));
        Assert.Equal(new AddressRange(GlobalStart, GlobalSize), map.GetAddressRange(map.CreateRange("AddressMap.Global")));

        var patch = map.CreateRange("AddressMap.Memory.Patch", new InstancePath([5]));
        Assert.Equal(new AddressRange(PatchAddress(5), PatchSize), map.GetAddressRange(patch));
    }

    [Fact]
    public void GetAddressRange_Fields()
    {
        var map = CreateMap();

        // 0x04-0x05: a 2-byte (14 bit) field
        var time = map.CreateRange("AddressMap.Memory.Patch.Time", new InstancePath([1]));
        Assert.Equal(new AddressRange(PatchAddress(1) + 0x04, 2), map.GetAddressRange(time));

        // 0x07-0x0E: the name
        var name = map.CreateRange("AddressMap.Edit.Name");
        Assert.Equal(new AddressRange(0x07, 8), map.GetAddressRange(name));

        // a bit in a bit-packed byte occupies the whole byte
        var switchB = map.CreateRange("AddressMap.Edit.Switches.B");
        Assert.Equal(new AddressRange(0, 1), map.GetAddressRange(switchB));
    }

    [Fact]
    public void Resolve_OneParameter()
    {
        var map = CreateMap();

        // item 5, offset 0x06
        var match = map.Resolve(new AddressRange(PatchAddress(5) + 0x06, 1));

        Assert.True(match.IsAligned);
        Assert.Empty(match.Unmapped);
        var block = Assert.IsType<AddressBlock>(match.Block);
        Assert.Equal("AddressMap.Memory.Patch.Level", block.Nodes.First.Path.Value);
        Assert.Equal(new InstancePath([5]), block.Nodes.FromInstance);
        Assert.Same(block.Nodes.First, block.Nodes.Last);
    }

    [Fact]
    public void Resolve_WholePatch()
    {
        var map = CreateMap();

        var match = map.Resolve(new AddressRange(PatchAddress(1), PatchSize));

        Assert.True(match.IsAligned);
        var block = Assert.IsType<AddressBlock>(match.Block);
        Assert.Equal(new AddressRange(PatchAddress(1), PatchSize), block.Addresses);
        Assert.Equal("AddressMap.Memory.Patch.Switches.A", block.Nodes.First.Path.Value);
        Assert.Equal("AddressMap.Memory.Patch.Name", block.Nodes.Last.Path.Value);
        Assert.Equal(new InstancePath([1]), block.Nodes.FromInstance);
        Assert.Equal(new InstancePath([1]), block.Nodes.ToInstance);
    }

    [Fact]
    public void Resolve_GapAndPartialField()
    {
        var map = CreateMap();

        // the reserved addresses after the edit patch
        var gap = map.Resolve(new AddressRange(PatchSize, Stride - PatchSize));
        Assert.Null(gap.Block);
        Assert.Equal([new AddressRange(PatchSize, Stride - PatchSize)], gap.Unmapped);

        // the LSB of Time only
        var partial = map.Resolve(new AddressRange(0x05, 1));
        Assert.False(partial.IsAligned);
        Assert.Equal(new AddressRange(0x04, 2), partial.Block!.Addresses);

        // from the last byte of the name into the gap
        var overrun = map.Resolve(new AddressRange(0x0E, 4));
        Assert.False(overrun.IsAligned);
        Assert.Equal([new AddressRange(PatchSize, 3)], overrun.Unmapped);
    }

    [Fact]
    public void WidenToAtomic_ParameterBecomesPatch()
    {
        var map = CreateMap();

        var cutoff = map.CreateRange("AddressMap.Memory.Patch.Filter.Cutoff", new InstancePath([3]));
        var widened = map.WidenToAtomic(cutoff);

        Assert.Equal(new AddressRange(PatchAddress(3), PatchSize), map.GetAddressRange(widened));
    }

    [Fact]
    public void WidenToAtomic_NotAtomic_Unchanged()
    {
        var map = CreateMap();

        var volume = map.CreateRange("AddressMap.Global.Volume");
        var widened = map.WidenToAtomic(volume);

        Assert.Equal(new AddressRange(GlobalStart + 9, 1), map.GetAddressRange(widened));
    }

    [Fact]
    public void Split_RangeMessage_SplitsAtGaps()
    {
        var map = CreateMap();
        var request = map.Protocol.Requests[0];

        // three memory patches: each is followed by reserved addresses
        var range = map.CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));
        var blocks = map.Split(range, request);

        Assert.Equal(
            [
                new AddressRange(PatchAddress(0), PatchSize),
                new AddressRange(PatchAddress(1), PatchSize),
                new AddressRange(PatchAddress(2), PatchSize),
            ],
            blocks.Select(b => b.Addresses));
    }

    [Fact]
    public void Split_FieldMessage_SharesBitPackedBytes()
    {
        var map = CreateMap();
        var perField = map.Protocol.DataMessages[0] with { Unit = AddressUnit.Field };

        var range = map.CreateRange("AddressMap.Edit.Switches", default, "AddressMap.Edit.Filter", default);
        var blocks = map.Split(range, perField);

        // 0x00 (4 bits), 0x01-0x03 (one parameter each)
        Assert.Equal([0L, 1, 2, 3], blocks.Select(b => b.Addresses.Start));
        Assert.All(blocks, b => Assert.Equal(1, b.Addresses.Size));
        Assert.Equal("AddressMap.Edit.Switches.D", blocks[0].Nodes.Last.Path.Value);
    }

    [Fact]
    public void Split_MaxDataSize_AtomicGroupStaysWhole()
    {
        var map = CreateMap();
        var small = map.Protocol.DataMessages[0] with { MaxDataSize = 4 };

        var blocks = map.Split(map.CreateRange("AddressMap.Edit"), small);

        Assert.Equal([new AddressRange(0, PatchSize)], blocks.Select(b => b.Addresses));
    }

    [Fact]
    public void Split_MaxDataSize_NeverInsideAField()
    {
        var map = CreateMap();
        var small = map.Protocol.DataMessages[0] with { MaxDataSize = 9 };

        // Channel (1) + Text (8) fit, Volume (1) does not
        var blocks = map.Split(map.CreateRange("AddressMap.Global"), small);

        Assert.Equal([new AddressRange(GlobalStart, 9), new AddressRange(GlobalStart + 9, 1)], blocks.Select(b => b.Addresses));
    }

    [Fact]
    public void SelectBody_ByModelId()
    {
        var other = Parse("""
            <schema name="OtherDevice">
              <includes>
                <include schema="TestEnvelope" />
              </includes>
              <members>
                <group name="AddressMap" nodeDef="TestEnvelope.body" midi.device.modelId="0x43">
                  <members>
                    <field name="Value" datatype="sys.uint8" />
                  </members>
                </group>
              </members>
            </schema>
            """);
        var map = CreateMap(other);

        Assert.Equal(MapName.FullName, map.Protocol.SelectBody(new DeviceValues { ["deviceId"] = 0, ["modelId"] = ModelId }).FullName);
        Assert.Equal(new SchemaName("OtherDevice.AddressMap").FullName, map.Protocol.SelectBody(new DeviceValues { ["modelId"] = 0x43 }).FullName);
        Assert.Throws<InvalidOperationException>(() => map.Protocol.SelectBody(new DeviceValues { ["modelId"] = 0x44 }));
    }

    [Fact]
    public void CreateRequest_Message()
    {
        var map = CreateMap();
        var device = map.Device.With(Device0);

        var bytes = map.Protocol.CreateRequest(map.Protocol.Requests[0], new AddressRange(PatchAddress(1), PatchSize), device);

        // address 01 20, size 00 0F
        Assert.Equal(Convert.ToHexString(Request(PatchAddress(1), PatchSize)), Convert.ToHexString(bytes));
        Assert.Equal("F07D004201012000", Convert.ToHexString(bytes)[..16]);
    }

    [Fact]
    public void CreateRequest_AddressIsBigEndian()
    {
        var map = CreateMap();
        var device = map.Device.With(Device0);

        var bytes = map.Protocol.CreateRequest(map.Protocol.Requests[0], new AddressRange(1, 0x80), device);

        // address 00 01, size 01 00
        Assert.Equal("F07D0042010001010" + "0", Convert.ToHexString(bytes)[..18]);
        Assert.Equal(Convert.ToHexString(Request(1, 0x80)), Convert.ToHexString(bytes));
    }

    [Fact]
    public void TryReadData_DataMessage()
    {
        var map = CreateMap();

        Assert.True(map.TryReadData(DataMessage(PatchAddress(1), Patch("Patch  1")), Device0, out var data));

        Assert.Equal(PatchAddress(1), data.Address);
        Assert.Equal(ModelId, data.Device["modelId"]);
        Assert.Equal(new AddressRange(PatchAddress(1), PatchSize), data.Block.Addresses);
        Assert.Equal(new InstancePath([1]), data.Block.Nodes.FromInstance);
    }

    [Fact]
    public void TryReadData_Decode()
    {
        var map = CreateMap();
        Assert.True(map.TryReadData(DataMessage(PatchAddress(0), Patch("My Patch", cutoff: 33, time: 300)), Device0, out var data));

        var store = new FieldValueStore();
        data.Decode(store);

        var item = new InstancePath([0]);
        Assert.True(store.TryGet("AddressMap.Memory.Patch.Name", item, out var name));
        Assert.Equal("My Patch", name.Value);
        // 0x04-0x05 = 02 2C: 2 * 128 + 44
        Assert.True(store.TryGet("AddressMap.Memory.Patch.Time", item, out var time));
        Assert.Equal((ushort)300, time.Value);
        Assert.True(store.TryGet("AddressMap.Memory.Patch.Filter.Cutoff", item, out var cutoff));
        Assert.Equal((byte)33, cutoff.Value);
        Assert.True(store.TryGet("AddressMap.Memory.Patch.Switches.A", item, out var switchA));
        Assert.Equal(false, switchA.Value);
    }

    [Fact]
    public void TryReadData_OtherDevice()
    {
        var map = CreateMap();
        var message = DataMessage(PatchAddress(0), Patch("Patch  0"));

        // the message is from device id 0
        Assert.False(map.TryReadData(message, new DeviceValues { ["deviceId"] = 5 }, out _));

        // another model id selects no map
        Assert.False(map.TryReadData(DataMessage(PatchAddress(0), Patch("Patch  0"), modelId: 0x44), Device0, out _));
    }

    [Fact]
    public void TryReadData_CorruptCrc()
    {
        var map = CreateMap();
        var message = DataMessage(PatchAddress(0), Patch("Patch  0"));
        message[^2] ^= 0x01;

        Assert.False(map.TryReadData(message, Device0, out _));
    }

    [Fact]
    public void CreateData_RoundTrip()
    {
        var map = CreateMap();
        var device = map.Device.With(Device0);
        var original = DataMessage(PatchAddress(2), Patch("Patch  2", cutoff: 99, time: 1000, level: 7));
        Assert.True(map.TryReadData(original, device, out var data));
        var store = new FieldValueStore();
        data.Decode(store);

        var bytes = map.CreateData(map.Protocol.DataMessages[0], data.Block, store, device);

        Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(bytes));
    }

    [Fact]
    public void CreateData_FieldsOfAGroup_RoundTrip()
    {
        var map = CreateMap();
        var device = map.Device.With(Device0);
        var item = new InstancePath([2]);
        var store = new FieldValueStore();
        store.Set("AddressMap.Memory.Patch.Filter.Cutoff", item, new LogicalField("Cutoff", typeof(byte), (byte)99));
        store.Set("AddressMap.Memory.Patch.Filter.Resonance", item, new LogicalField("Resonance", typeof(byte), (byte)5));

        // Cutoff and Resonance of the filter: two of its three fields
        var block = map.Resolve(new AddressRange(PatchAddress(2) + 1, 2)).Block!;
        var bytes = map.CreateData(map.Protocol.DataMessages[0], block, store, device);

        Assert.Equal(Convert.ToHexString(DataMessage(PatchAddress(2) + 1, [99, 5])), Convert.ToHexString(bytes));
        Assert.True(map.TryReadData(bytes, device, out var data));
        Assert.Equal(block.Addresses, data.Block.Addresses);
        var read = new FieldValueStore();
        data.Decode(read);
        Assert.Equal(2, read.Count);
        Assert.True(read.TryGet("AddressMap.Memory.Patch.Filter.Resonance", item, out var resonance));
        Assert.Equal(5, Convert.ToInt32(resonance.Value));
    }

    [Fact]
    public void CreateData_BlockWithUnmappedAddresses_Throws()
    {
        var map = CreateMap();
        var device = map.Device.With(Device0);

        // the edit patch, its reserved addresses and the first field of Global
        var block = map.Resolve(new AddressRange(0, GlobalStart + 1)).Block!;

        Assert.Throws<ArgumentException>(() => map.CreateData(map.Protocol.DataMessages[0], block, new FieldValueStore(), device));
    }

    [Fact]
    public void TryReadData_OneField()
    {
        var map = CreateMap();

        Assert.True(map.TryReadData(DataMessage(GlobalStart + 9, [0x33]), Device0, out var data));

        Assert.Equal(new AddressRange(GlobalStart + 9, 1), data.Block.Addresses);
        var store = new FieldValueStore();
        data.Decode(store);
        Assert.True(store.TryGet("AddressMap.Global.Volume", InstancePath.Empty, out var volume));
        Assert.Equal(0x33, Convert.ToInt32(volume.Value));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryReadData_BitPackedByte_HoldsAllItsFields()
    {
        var map = CreateMap();

        Assert.True(map.TryReadData(DataMessage(PatchAddress(1), [0x05]), Device0, out var data));

        var store = new FieldValueStore();
        data.Decode(store);
        Assert.Equal(4, store.Count);
        Assert.True(store.TryGet("AddressMap.Memory.Patch.Switches.A", new InstancePath([1]), out var a));
        Assert.Equal(true, a.Value);
        Assert.True(store.TryGet("AddressMap.Memory.Patch.Switches.B", new InstancePath([1]), out var b));
        Assert.Equal(false, b.Value);
    }

    [Fact]
    public void TryReadData_AGroupAndAField()
    {
        var map = CreateMap();

        // the filter, the time and the level of patch 3
        Assert.True(map.TryReadData(DataMessage(PatchAddress(3) + 1, [10, 20, 1, 2, 44, 100]), Device0, out var data));

        Assert.Equal(new AddressRange(PatchAddress(3) + 1, 6), data.Block.Addresses);
        Assert.Equal("AddressMap.Memory.Patch.Filter.Cutoff", data.Block.Nodes.First.Path.Value);
        Assert.Equal("AddressMap.Memory.Patch.Level", data.Block.Nodes.Last.Path.Value);
    }

    [Fact]
    public void TryReadData_MessagesOfAnotherSize_AreStillRecognised()
    {
        var map = CreateMap();
        var channelAndText = DataMessage(GlobalStart, [3, .. System.Text.Encoding.ASCII.GetBytes("Text 123")]);

        // the first message teaches the size outside the data; the next ones have other sizes
        Assert.True(map.TryReadData(DataMessage(GlobalStart + 9, [0x33]), Device0, out var volume));
        Assert.True(map.TryReadData(channelAndText, Device0, out var global));
        Assert.True(map.TryReadData(DataMessage(PatchAddress(0), Patch("Patch  0")), Device0, out var patch));

        Assert.Equal(new AddressRange(GlobalStart + 9, 1), volume.Block.Addresses);
        Assert.Equal(new AddressRange(GlobalStart, 9), global.Block.Addresses);
        Assert.Equal(new AddressRange(PatchAddress(0), PatchSize), patch.Block.Addresses);
    }

    [Fact]
    public void TryReadData_EndsInsideAField_IsNotRecognised()
    {
        var map = CreateMap();

        // the first byte of the 2 byte time
        Assert.False(map.TryReadData(DataMessage(PatchAddress(0) + 4, [0x02]), Device0, out _));
    }
}
