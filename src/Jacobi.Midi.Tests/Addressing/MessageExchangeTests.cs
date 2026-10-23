using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Xunit;
using static Jacobi.Midi.Tests.Addressing.TestDevice;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The message exchange (one way) on the stand-alone <see cref="TestDevice"/> schemas.
/// </summary>
public class MessageExchangeTests
{
    private const string EditName = "Edit    ";

    // the client only has the serializer and ranges of its plans: the address map is deduced from them
    private static PlanRange Range(Serializer serializer, string path, params int[] instance)
        => serializer.GetPlan(MapName).CreateRange(path, new InstancePath(instance), path, new InstancePath(instance));

    private static MessageExchange CreateExchange(Serializer serializer, FakeDevice device, int timeoutMs = 1000, DeviceValues? values = null)
        => new(new AddressMaps(serializer), device, values ?? new DeviceValues { ["deviceId"] = 0 },
            new MessageExchangeOptions { ResponseTimeout = TimeSpan.FromMilliseconds(timeoutMs) });

    [Fact]
    public async Task Receive_MemoryPatch()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await exchange.Receive(Range(serializer, "AddressMap.Memory.Patch", 1), sink, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(Convert.ToHexString(Request(PatchAddress(1), PatchSize)), Convert.ToHexString(Assert.Single(device.Requests)));
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([1]), out var name));
        Assert.Equal(PatchName(1), name.Value);
    }

    [Fact]
    public async Task Receive_ThreePatches_OneRequestEach()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var range = serializer.GetPlan(MapName)
            .CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));
        var result = await exchange.Receive(range, sink, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(3, result.Blocks.Count);
        Assert.Equal(3, device.Requests.Count);
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([2]), out var name));
        Assert.Equal(PatchName(2), name.Value);
    }

    [Fact]
    public async Task Receive_OneParameter_GetsTheWholePatch()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await exchange.Receive(Range(serializer, "AddressMap.Memory.Patch.Filter.Cutoff", 3), sink, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        // the patch is atomic: the request is for the whole patch
        Assert.Equal(new AddressRange(PatchAddress(3), PatchSize), Assert.Single(result.Blocks).Block.Addresses);
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Filter.Cutoff", new InstancePath([3]), out _));
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([3]), out _));
    }

    [Fact]
    public async Task Receive_NoAnswer_TimesOut()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Silent = true };
        await using var exchange = CreateExchange(serializer, device, timeoutMs: 50);

        var result = await exchange.Receive(Range(serializer, "AddressMap.Edit"), new FieldValueStore(), TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(AddressBlockStatus.TimedOut, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task Send_OneParameter_WritesTheWholePatchBack()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var source = new FieldValueStore();
        source.Set("AddressMap.Edit.Filter.Cutoff", InstancePath.Empty, new LogicalField("Cutoff", typeof(byte), (byte)77));

        var result = await exchange.Send(Range(serializer, "AddressMap.Edit.Filter.Cutoff"), source, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        // read first (the patch is atomic), then the whole patch is written back
        Assert.Single(device.Requests);
        var sent = Assert.Single(device.DataSets);
        Assert.Equal(Convert.ToHexString(DataMessage(0, Patch(EditName, cutoff: 77))), Convert.ToHexString(sent));
    }

    [Fact]
    public async Task Send_WholePatch_WithoutReading()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var values = new FieldValueStore();
        var message = DataMessage(PatchAddress(4), Patch("New     ", cutoff: 50));
        Assert.True(AddressMap.Create(serializer, MapName).TryReadData(message, new DeviceValues { ["deviceId"] = 0 }, out var data));
        data.Decode(values);

        var result = await exchange.Send(Range(serializer, "AddressMap.Memory.Patch", 4), values, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Empty(device.Requests);
        Assert.Equal(Convert.ToHexString(message), Convert.ToHexString(Assert.Single(device.DataSets)));
    }

    [Fact]
    public async Task Listen_UnsolicitedData()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var listening = exchange.Listen(Range(serializer, "AddressMap.Memory.Patch", 5), sink, cancel.Token);
        device.Push(FakeDevice.MemoryPatch(4));   // outside the range
        device.Push(FakeDevice.MemoryPatch(5));
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
        await using var exchange = CreateExchange(serializer, new FakeDevice(), values: new DeviceValues());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => exchange.Receive(Range(serializer, "AddressMap.Edit"), new FieldValueStore(), TestContext.Current.CancellationToken));
        Assert.Contains("deviceId", exception.Message);
    }

    [Fact]
    public void Protocol_WithoutReplies_IsOneWay()
    {
        Assert.Equal("oneWay", CreateMap().Protocol.Exchange);
    }

    [Fact]
    public void PrepareAll_FindsAndValidatesTheMaps()
    {
        var serializer = CreateSerializer();

        Assert.Equal([MapName.FullName], AddressProtocol.FindMapRoots(serializer.Schemas).Select(r => r.FullName));
        new AddressMaps(serializer).PrepareAll();
    }

    [Fact]
    public void PrepareAll_ReportsAnInvalidMap()
    {
        // two fields with explicit addresses out of plan order
        var invalid = Parse("""
            <schema name="Invalid">
              <includes>
                <include schema="TestEnvelope" />
              </includes>
              <members>
                <group name="AddressMap" nodeDef="TestEnvelope.body" midi.device.modelId="0x7E">
                  <members>
                    <field name="A" datatype="sys.uint8" midi.addressStart="0x10" />
                    <field name="B" datatype="sys.uint8" midi.addressStart="0x05" />
                  </members>
                </group>
              </members>
            </schema>
            """);
        var maps = new AddressMaps(CreateSerializer(invalid));

        maps.Prepare(MapName);
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
    /// A <see cref="TestDevice"/> for the tests: answers a request with the whole patches it overlaps, on a background
    /// thread, and records the messages it receives.
    /// </summary>
    private sealed class FakeDevice : ISysExPort
    {
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

        public void Push(byte[] sysEx) => Received?.Invoke(sysEx);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
