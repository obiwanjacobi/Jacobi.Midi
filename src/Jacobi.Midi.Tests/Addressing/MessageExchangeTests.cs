using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using System.Diagnostics;
using Xunit;
using static Jacobi.Midi.Tests.Addressing.TestDevice;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The message exchange (one way) on the stand-alone <see cref="TestDevice"/> schemas and its exchange file.
/// </summary>
public class MessageExchangeTests
{
    // the client only has the serializer and ranges of its plans: the address map is deduced from them
    private static PlanRange Range(Serializer serializer, string path, params int[] instance)
        => serializer.GetPlan(MapName).CreateRange(path, new InstancePath(instance), path, new InstancePath(instance));

    private static MessageExchange CreateExchange(Serializer serializer, FakeDevice device, int timeoutMs = 1000, DeviceValues? values = null,
        ExchangeDefinitions? exchanges = null)
        => new(new AddressMaps(serializer), exchanges ?? CreateExchanges(timeoutMs), device, values ?? new DeviceValues { ["deviceId"] = 0 });

    private static Task<TransferResult> RequestData(MessageExchange exchange, PlanRange range, IValueSink sink)
        => exchange.Execute(ExchangeNames.RequestData, range, new ExchangeData { Sink = sink }, TestContext.Current.CancellationToken);

    private static Task<TransferResult> SendData(MessageExchange exchange, PlanRange range, IValueSource source)
        => exchange.Execute(ExchangeNames.SendData, range, new ExchangeData { Source = source }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Receive_MemoryPatch()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await RequestData(exchange, Range(serializer, "AddressMap.Memory.Patch", 1), sink);

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
        var result = await RequestData(exchange, range, sink);

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

        var result = await RequestData(exchange, Range(serializer, "AddressMap.Memory.Patch.Filter.Cutoff", 3), sink);

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

        var result = await RequestData(exchange, Range(serializer, "AddressMap.Edit"), new FieldValueStore());

        Assert.False(result.IsComplete);
        var block = Assert.Single(result.Blocks);
        Assert.Equal(BlockStatus.TimedOut, block.Status);
        Assert.Equal("timeout in state 'request' of 'requestData'.", block.Reason);
    }

    [Fact]
    public async Task Receive_AFailedBlock_EndsTheTransfer()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Silent = true };
        await using var exchange = CreateExchange(serializer, device, timeoutMs: 50);
        var range = serializer.GetPlan(MapName)
            .CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));

        var result = await RequestData(exchange, range, new FieldValueStore());

        // the first block times out: the others are not requested, and reported with the same status
        Assert.Single(device.Requests);
        Assert.Equal(3, result.Blocks.Count);
        Assert.All(result.Blocks, block => Assert.Equal(BlockStatus.TimedOut, block.Status));
        Assert.StartsWith("Not transferred", result.Blocks[2].Reason);
    }

    [Fact]
    public async Task Send_OneParameterOfAnAtomicGroup_WithoutTheOtherValues_Fails()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var source = new FieldValueStore();
        source.Set("AddressMap.Edit.Filter.Cutoff", InstancePath.Empty, new LogicalField("Cutoff", typeof(byte), (byte)77));

        var result = await SendData(exchange, Range(serializer, "AddressMap.Edit.Filter.Cutoff"), source);

        // the exchange does not complete the patch (a session does): the source must hold the whole patch
        Assert.Equal(BlockStatus.Failed, Assert.Single(result.Blocks).Status);
        Assert.Empty(device.Requests);
        Assert.Empty(device.DataSets);
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

        var result = await SendData(exchange, Range(serializer, "AddressMap.Memory.Patch", 4), values);

        Assert.True(result.IsComplete);
        Assert.Empty(device.Requests);
        Assert.Equal(Convert.ToHexString(message), Convert.ToHexString(Assert.Single(device.DataSets)));
    }

    [Fact]
    public async Task Send_OneFieldOfANonAtomicGroup_WritesOnlyThatField()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var source = new FieldValueStore();
        source.Set("AddressMap.Global.Volume", InstancePath.Empty, new LogicalField("Volume", typeof(byte), (byte)0x33));

        var result = await SendData(exchange, Range(serializer, "AddressMap.Global.Volume"), source);

        Assert.True(result.IsComplete);
        Assert.Empty(device.Requests);
        Assert.Equal(Convert.ToHexString(DataMessage(GlobalStart + 9, [0x33])), Convert.ToHexString(Assert.Single(device.DataSets)));
    }

    [Fact]
    public async Task Send_KeepsTheMessageIntervalBetweenMessages()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        var exchanges = new ExchangeDefinitions();
        exchanges.Add(ExchangeDefinitionReader.ReadXml(ExchangeXml.Replace("messageInterval=\"20\"", "messageInterval=\"60\""), "test"));
        await using var exchange = CreateExchange(serializer, device, exchanges: exchanges);
        var values = new FieldValueStore();
        var range = serializer.GetPlan(MapName)
            .CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));
        Assert.True((await RequestData(exchange, range, values)).IsComplete);

        var watch = Stopwatch.StartNew();
        var result = await SendData(exchange, range, values);

        Assert.True(result.IsComplete);
        Assert.Equal(3, device.DataSets.Count);
        // 60 ms after the last request, and between the three data messages
        Assert.True(watch.ElapsedMilliseconds >= 3 * 60 - 10, $"{watch.ElapsedMilliseconds} ms");
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
        await FakeDevice.WaitFor(() => sink.Count > 0);
        cancel.Cancel();
        await listening;

        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([5]), out _));
        Assert.False(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([4]), out _));
    }

    [Fact]
    public async Task Listen_OneField()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var listening = exchange.Listen(Range(serializer, "AddressMap.Global"), sink, cancel.Token);
        device.Push(DataMessage(GlobalStart + 9, [0x33]));
        await FakeDevice.WaitFor(() => sink.Count > 0);
        cancel.Cancel();
        await listening;

        Assert.True(sink.TryGet("AddressMap.Global.Volume", InstancePath.Empty, out var volume));
        Assert.Equal(0x33, Convert.ToInt32(volume.Value));
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task Receive_WithoutDeviceId_Throws()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeDevice(), values: new DeviceValues());

        var exception = await Assert.ThrowsAsync<ExchangeException>(
            () => RequestData(exchange, Range(serializer, "AddressMap.Edit"), new FieldValueStore()));
        Assert.Contains("deviceId", exception.Message);
    }

    [Fact]
    public async Task Execute_AnExchangeTheFileDoesNotHave_Throws()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);

        var exception = await Assert.ThrowsAsync<ExchangeException>(() => exchange.Execute(
            "storeData", Range(serializer, "AddressMap.Edit"), new ExchangeData(), TestContext.Current.CancellationToken));

        Assert.Equal("storeData", exception.Exchange);
        Assert.Contains("has no exchange 'storeData'", Assert.Single(exception.Problems));
        Assert.Empty(device.Requests);
    }

    [Fact]
    public async Task Execute_WithoutAnExchangeFile_Throws()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeDevice(), exchanges: new ExchangeDefinitions());

        var exception = await Assert.ThrowsAsync<ExchangeException>(() => RequestData(exchange, Range(serializer, "AddressMap.Edit"), new FieldValueStore()));

        Assert.Contains("There is no exchange file for 'TestDevice.AddressMap'", Assert.Single(exception.Problems));
    }

    [Fact]
    public async Task Validate_ReportsTheProblems()
    {
        var serializer = CreateSerializer();
        var exchanges = new ExchangeDefinitions();
        exchanges.Add(ExchangeDefinitionReader.ReadXml("""
            <exchanges for="TestDevice.AddressMap">
              <exchange name="unknownMessage">
                <state name="request">
                  <send message="TestEnvelope.Message.Unknown" />
                  <on event="sent" result="completed" />
                </state>
              </exchange>
              <exchange name="twoCuts">
                <state name="request">
                  <send message="TestEnvelope.Message.Request" />
                  <on event="sent" goto="write" />
                </state>
                <state name="write">
                  <send message="TestEnvelope.Message.Data" />
                  <on event="sent" result="completed" />
                </state>
              </exchange>
            </exchanges>
            """, "test"));
        await using var exchange = CreateExchange(serializer, new FakeDevice(), exchanges: exchanges, values: new DeviceValues());
        var edit = Range(serializer, "AddressMap.Edit");

        Assert.Contains("There is no message 'TestEnvelope.Message.Unknown'", Assert.Single(exchange.Validate("unknownMessage", edit)));
        var problems = exchange.Validate("twoCuts", edit, new ExchangeData());
        Assert.Contains(problems, problem => problem.Contains("more than one message that asks for or carries data", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("deviceId", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("it needs a source", StringComparison.Ordinal));
    }

    [Fact]
    public void DataMessages_AreFoundByTheirDataRole()
    {
        var map = CreateMap();

        Assert.Equal("TestEnvelope.Message.Data", Assert.Single(map.Protocol.DataMessages).FullName);
        var request = map.Message(new SchemaName("TestEnvelope.Message.Request"));
        Assert.True(request.IsRequest);
        Assert.False(request.IsData);
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
                    <field name="A" datatype="sys.uint8" midi.address="0x10" />
                    <field name="B" datatype="sys.uint8" midi.address="0x05" />
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

    [Fact]
    public void PrepareAll_ReportsInvalidRepeatCounts()
    {
        // a repeat without midi.repeat or count, an invalid midi.repeat, and midi.repeat on a field
        var invalid = Parse("""
            <schema name="Invalid">
              <includes>
                <include schema="TestEnvelope" />
              </includes>
              <members>
                <group name="AddressMap" nodeDef="TestEnvelope.body" midi.device.modelId="0x7E">
                  <members>
                    <field name="A" datatype="sys.uint8" midi.repeat="2" />
                    <repeat name="B" count="2" midi.repeat="0">
                      <members>
                        <field name="Value" datatype="sys.uint8" />
                      </members>
                    </repeat>
                    <repeat name="C">
                      <members>
                        <field name="Value" datatype="sys.uint8" />
                      </members>
                    </repeat>
                  </members>
                </group>
              </members>
            </schema>
            """);
        var maps = new AddressMaps(CreateSerializer(invalid));

        var exception = Assert.Throws<AddressMapException>(maps.PrepareAll);
        Assert.Contains(exception.Errors, e => e.StartsWith("AddressMap.A:", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, e => e.StartsWith("AddressMap.B:", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, e => e.StartsWith("AddressMap.C:", StringComparison.Ordinal));
    }
}
