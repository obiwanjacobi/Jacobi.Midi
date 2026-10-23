using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

public class Gp8MessageExchangeTests
{
    private const int MessageSize = 59;
    private const int PatchSize = 0x32;
    private const int MemoryStart = 0x2000;
    private const int Stride = 0x40;

    private static Serializer CreateSerializer(params SchemaDocument[] documents)
        => new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas(documents))
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();

    // the client only has the serializer and ranges of its plans: the address map is deduced from them
    private static PlanRange Range(Serializer serializer, string path, params int[] instance)
        => serializer.GetPlan(new SchemaName("Gp8.AddressMap")).CreateRange(path, new InstancePath(instance), path, new InstancePath(instance));

    private static MessageExchange CreateExchange(Serializer serializer, FakeGp8 device, int timeoutMs = 1000, DeviceValues? values = null)
        => new(new AddressMaps(serializer), device, values ?? new DeviceValues { ["deviceId"] = 0 },
            new MessageExchangeOptions { ResponseTimeout = TimeSpan.FromMilliseconds(timeoutMs) });

    [Fact]
    public async Task Receive_MemoryPatch()
    {
        var serializer = CreateSerializer();
        var device = new FakeGp8();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await exchange.Receive(Range(serializer, "AddressMap.Memory.Patch", 1), sink, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal("F0410013114040003" + "24EF7", Convert.ToHexString(Assert.Single(device.Requests)));
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([1]), out var name));
        Assert.Equal(" Dry Rhythm     ", name.Value);
    }

    [Fact]
    public async Task Receive_ThreePatches_OneRequestEach()
    {
        var serializer = CreateSerializer();
        var device = new FakeGp8();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var range = serializer.GetPlan(new SchemaName("Gp8.AddressMap"))
            .CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));
        var result = await exchange.Receive(range, sink, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(3, result.Blocks.Count);
        Assert.Equal(3, device.Requests.Count);
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([2]), out var name));
        Assert.Equal(" Dry Rhythm 2   ", name.Value);
    }

    [Fact]
    public async Task Receive_OneParameter_GetsTheWholePatch()
    {
        var serializer = CreateSerializer();
        var device = new FakeGp8();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await exchange.Receive(Range(serializer, "AddressMap.Memory.Patch.Phaser.Rate", 3), sink, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        // the patch is atomic: the request is for the whole patch
        Assert.Equal(new AddressRange(MemoryStart + 3 * Stride, PatchSize), Assert.Single(result.Blocks).Block.Addresses);
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Phaser.Rate", new InstancePath([3]), out _));
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([3]), out _));
    }

    [Fact]
    public async Task Receive_NoAnswer_TimesOut()
    {
        var serializer = CreateSerializer();
        var device = new FakeGp8 { Silent = true };
        await using var exchange = CreateExchange(serializer, device, timeoutMs: 50);

        var result = await exchange.Receive(Range(serializer, "AddressMap.Temporary"), new FieldValueStore(), TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(AddressBlockStatus.TimedOut, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task Send_OneParameter_WritesTheWholePatchBack()
    {
        var serializer = CreateSerializer();
        var device = new FakeGp8();
        await using var exchange = CreateExchange(serializer, device);
        var source = new FieldValueStore();
        source.Set("AddressMap.Temporary.Phaser.Rate", InstancePath.Empty, new LogicalField("Rate", typeof(byte), (byte)77));

        var result = await exchange.Send(Range(serializer, "AddressMap.Temporary.Phaser.Rate"), source, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        // read first (the patch is atomic), then the whole patch is written back
        Assert.Single(device.Requests);
        var sent = Assert.Single(device.DataSets);
        var expected = FakeGp8.WithAddress(FakeGp8.DumpMessage(0), 0x00, 0x00);
        expected[7 + 0x0D] = 77;
        FakeGp8.UpdateChecksum(expected);
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(sent));
    }

    [Fact]
    public async Task Send_WholePatch_WithoutReading()
    {
        var serializer = CreateSerializer();
        var device = new FakeGp8();
        await using var exchange = CreateExchange(serializer, device);
        var values = new FieldValueStore();
        Assert.True(AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap")).TryReadData(FakeGp8.DumpMessage(4), new DeviceValues { ["deviceId"] = 0 }, out var data));
        data.Decode(values);

        var result = await exchange.Send(Range(serializer, "AddressMap.Memory.Patch", 4), values, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Empty(device.Requests);
        Assert.Equal(Convert.ToHexString(FakeGp8.DumpMessage(4)), Convert.ToHexString(Assert.Single(device.DataSets)));
    }

    [Fact]
    public async Task Listen_UnsolicitedData()
    {
        var serializer = CreateSerializer();
        var device = new FakeGp8();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var listening = exchange.Listen(Range(serializer, "AddressMap.Memory.Patch", 5), sink, cancel.Token);
        device.Push(FakeGp8.DumpMessage(4));   // outside the range
        device.Push(FakeGp8.DumpMessage(5));
        await WaitFor(() => sink.Count > 0);
        cancel.Cancel();
        await listening;

        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([5]), out _));
        Assert.False(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([4]), out _));
    }

    [Fact]
    public async Task Receive_WithoutDeviceId_Throws()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeGp8(), values: new DeviceValues());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => exchange.Receive(Range(serializer, "AddressMap.Temporary"), new FieldValueStore(), TestContext.Current.CancellationToken));
        Assert.Contains("deviceId", exception.Message);
    }

    [Fact]
    public void PrepareAll_FindsAndValidatesTheMaps()
    {
        var serializer = CreateSerializer();

        Assert.Equal(["Gp8.AddressMap", "Gx700.AddressMap"], AddressProtocol.FindMapRoots(serializer.Schemas).Select(r => r.FullName).Order());
        new AddressMaps(serializer).PrepareAll();
    }

    [Fact]
    public void PrepareAll_ReportsAnInvalidMap()
    {
        // two fields with explicit addresses out of plan order
        var invalid = Jacobi.BinarySerializer.Schema.Xml.XmlSerializer.Deserialize("""
            <schema name="Invalid">
              <members>
                <group name="AddressMap" nodeDef="roland.messageBody" midi.device.modelId="0x7E">
                  <members>
                    <field name="A" datatype="sys.uint8" midi.addressStart="0x10" />
                    <field name="B" datatype="sys.uint8" midi.addressStart="0x05" />
                  </members>
                </group>
              </members>
            </schema>
            """);
        var maps = new AddressMaps(CreateSerializer(invalid));

        maps.Prepare(new SchemaName("Gp8.AddressMap"));
        var exception = Assert.Throws<AddressMapException>(maps.PrepareAll);
        Assert.Equal("Invalid.AddressMap", exception.Root);
        Assert.Contains(exception.Errors, e => e.StartsWith("AddressMap.B:", StringComparison.Ordinal));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// A GP-8 for the tests: answers an RQ1 with the whole patches it overlaps (from the bulk dump, the temporary
    /// patch is memory patch 1), on a background thread, and records the messages it receives.
    /// </summary>
    private sealed class FakeGp8 : ISysExPort
    {
        private readonly Dictionary<long, byte[]> _patches = [];

        public FakeGp8()
        {
            _patches[0] = WithAddress(DumpMessage(0), 0x00, 0x00);
            for (var i = 0; i < 128; i++)
            {
                _patches[MemoryStart + i * Stride] = DumpMessage(i);
            }
        }

        public bool Silent { get; init; }
        public List<byte[]> Requests { get; } = [];
        public List<byte[]> DataSets { get; } = [];

        public event SysExReceivedHandler? Received;

        public ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct)
        {
            var bytes = sysEx.ToArray();
            switch (bytes[4])
            {
                case 0x11:
                    Requests.Add(bytes);
                    var request = new AddressRange((bytes[5] << 7) | bytes[6], (bytes[7] << 7) | bytes[8]);
                    var answers = _patches.Where(p => new AddressRange(p.Key, PatchSize).Overlaps(request)).Select(p => p.Value).ToList();
                    if (!Silent)
                    {
                        _ = Task.Run(() => answers.ForEach(answer => Received?.Invoke(answer)), ct);
                    }
                    break;
                case 0x12:
                    DataSets.Add(bytes);
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public void Push(byte[] sysEx) => Received?.Invoke(sysEx);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public static byte[] DumpMessage(int index)
        {
            var file = File.ReadAllBytes(TestHelpers.PathOf("Schema/gp8-sysex.syx"));
            return file.AsSpan(index * MessageSize, MessageSize).ToArray();
        }

        public static byte[] WithAddress(byte[] message, byte msb, byte lsb)
        {
            var copy = message.ToArray();
            copy[5] = msb;
            copy[6] = lsb;
            UpdateChecksum(copy);
            return copy;
        }

        // checksum = (128 - ((address + data) % 128)) % 128
        public static void UpdateChecksum(byte[] message)
        {
            var sum = message.AsSpan(5, message.Length - 7).ToArray().Sum(b => b);
            message[^2] = (byte)((128 - sum % 128) % 128);
        }
    }
}
