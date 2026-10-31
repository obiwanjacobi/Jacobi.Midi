using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Xunit;
using static Jacobi.Midi.Tests.Addressing.TestDevice;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The device session on the stand-alone <see cref="TestDevice"/> schemas: the state of the device is kept between
/// transfers, atomic groups are completed from it, and only what it does not hold is requested.
/// </summary>
public class DeviceSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanRange Range(Serializer serializer, string path, params int[] instance)
        => serializer.GetPlan(MapName).CreateRange(path, new InstancePath(instance), path, new InstancePath(instance));

    private static DeviceSession CreateSession(Serializer serializer, FakeDevice device, SessionData data = SessionData.Session, int timeoutMs = 1000)
        => new(new MessageExchange(new AddressMaps(serializer), CreateExchanges(timeoutMs), device, new DeviceValues { ["deviceId"] = 0 }),
            new DeviceSessionOptions { Data = data });

    private static FieldValueStore Cutoff(byte value)
    {
        var values = new FieldValueStore();
        values.Set("AddressMap.Edit.Filter.Cutoff", InstancePath.Empty, new LogicalField("Cutoff", typeof(byte), value));
        return values;
    }

    [Fact]
    public async Task Receive_Twice_TheSecondFromTheSession()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var patch = Range(serializer, "AddressMap.Memory.Patch", 1);

        Assert.True((await session.RequestData(patch, new FieldValueStore(), Ct)).IsComplete);
        var sink = new FieldValueStore();
        var result = await session.RequestData(patch, sink, Ct);

        Assert.True(result.IsComplete);
        Assert.Single(device.Requests);
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([1]), out var name));
        Assert.Equal(PatchName(1), name.Value);
    }

    [Fact]
    public async Task Receive_FromTheDevice_AlwaysRequests()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var patch = Range(serializer, "AddressMap.Memory.Patch", 1);

        await session.RequestData(patch, new FieldValueStore(), Ct);
        await session.RequestData(patch, new FieldValueStore(), SessionData.Device, null, Ct);

        Assert.Equal(2, device.Requests.Count);
    }

    [Fact]
    public async Task RequestData_AFailedBlock_EndsTheTransfer()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Silent = true };
        await using var session = CreateSession(serializer, device, timeoutMs: 50);
        var range = serializer.GetPlan(MapName)
            .CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));

        var result = await session.RequestData(range, new FieldValueStore(), Ct);

        Assert.Single(device.Requests);
        Assert.Equal(3, result.Blocks.Count);
        Assert.All(result.Blocks, block => Assert.Equal(BlockStatus.TimedOut, block.Status));
    }

    [Fact]
    public async Task Execute_AnExchangeThatReceives_AlwaysRequestsAndKeepsTheState()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var patch = Range(serializer, "AddressMap.Memory.Patch", 1);

        await session.RequestData(patch, new FieldValueStore(), Ct);
        var result = await session.Execute(ExchangeNames.RequestData, patch, new ExchangeData { Sink = new FieldValueStore() }, null, Ct);
        await session.RequestData(patch, new FieldValueStore(), Ct);

        // the named exchange requests again; the state holds the patch for the next RequestData
        Assert.True(result.IsComplete);
        Assert.Equal(2, device.Requests.Count);
    }

    [Fact]
    public async Task Execute_AnExchangeThatSends_CompletesTheAtomicGroup()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);

        var result = await session.Execute(ExchangeNames.SendData, Range(serializer, "AddressMap.Edit.Filter.Cutoff"),
            new ExchangeData { Source = Cutoff(77) }, null, Ct);

        Assert.True(result.IsComplete);
        Assert.Single(device.Requests);
        Assert.Equal(Convert.ToHexString(DataMessage(0, Patch(FakeDevice.EditName, cutoff: 77))), Convert.ToHexString(Assert.Single(device.DataSets)));
    }

    [Fact]
    public async Task Probe_RequestsFromTheDevice_TheStateIsNotChanged()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var patch = Range(serializer, "AddressMap.Memory.Patch", 1);

        Assert.True((await session.Probe(patch, Ct)).IsComplete);
        await session.RequestData(patch, new FieldValueStore(), Ct);

        // the probe was not kept: the receive requests the patch again
        Assert.Equal(2, device.Requests.Count);
    }

    [Fact]
    public async Task Send_OneParameterOfAnAtomicGroup_ReadsTheGroupOnce()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var cutoff = Range(serializer, "AddressMap.Edit.Filter.Cutoff");

        Assert.True((await session.SendData(cutoff, Cutoff(77), Ct)).IsComplete);
        Assert.True((await session.SendData(cutoff, Cutoff(78), Ct)).IsComplete);

        // the patch is atomic: it is read once, then completed from the session
        Assert.Single(device.Requests);
        Assert.Equal(2, device.DataSets.Count);
        Assert.Equal(Convert.ToHexString(DataMessage(0, Patch(FakeDevice.EditName, cutoff: 77))), Convert.ToHexString(device.DataSets[0]));
        Assert.Equal(Convert.ToHexString(DataMessage(0, Patch(FakeDevice.EditName, cutoff: 78))), Convert.ToHexString(device.DataSets[1]));
    }

    [Fact]
    public async Task Send_AfterReceive_DoesNotRead()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);

        await session.RequestData(Range(serializer, "AddressMap.Edit"), new FieldValueStore(), Ct);
        var result = await session.SendData(Range(serializer, "AddressMap.Edit.Filter.Cutoff"), Cutoff(77), Ct);

        Assert.True(result.IsComplete);
        Assert.Single(device.Requests);
        Assert.Single(device.DataSets);
    }

    [Fact]
    public async Task Send_FromTheDevice_ReadsEveryTime()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device, SessionData.Device);
        var cutoff = Range(serializer, "AddressMap.Edit.Filter.Cutoff");

        await session.SendData(cutoff, Cutoff(77), Ct);
        await session.SendData(cutoff, Cutoff(78), Ct);

        Assert.Equal(2, device.Requests.Count);
        Assert.Equal(2, device.DataSets.Count);
    }

    [Fact]
    public async Task Send_WholePatch_WithoutReading()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var values = new FieldValueStore();
        var message = DataMessage(PatchAddress(4), Patch("New     ", cutoff: 50));
        Assert.True(AddressMap.Create(serializer, MapName).TryReadData(message, new DeviceValues { ["deviceId"] = 0 }, out var data));
        data.Decode(values);

        var result = await session.SendData(Range(serializer, "AddressMap.Memory.Patch", 4), values, Ct);

        Assert.True(result.IsComplete);
        Assert.Empty(device.Requests);
        Assert.Equal(Convert.ToHexString(message), Convert.ToHexString(Assert.Single(device.DataSets)));
    }

    [Fact]
    public async Task Send_OneFieldOfANonAtomicGroup_WithoutReading()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var values = new FieldValueStore();
        values.Set("AddressMap.Global.Volume", InstancePath.Empty, new LogicalField("Volume", typeof(byte), (byte)0x33));

        var result = await session.SendData(Range(serializer, "AddressMap.Global.Volume"), values, Ct);

        Assert.True(result.IsComplete);
        Assert.Empty(device.Requests);
        Assert.Equal(Convert.ToHexString(DataMessage(GlobalStart + 9, [0x33])), Convert.ToHexString(Assert.Single(device.DataSets)));
    }

    [Fact]
    public async Task Send_GroupCannotBeRead_SendsNothing()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Silent = true };
        await using var session = CreateSession(serializer, device, timeoutMs: 50);

        var result = await session.SendData(Range(serializer, "AddressMap.Edit.Filter.Cutoff"), Cutoff(77), Ct);

        Assert.False(result.IsComplete);
        Assert.Equal(BlockStatus.TimedOut, Assert.Single(result.Blocks).Status);
        Assert.Empty(device.DataSets);
    }

    [Fact]
    public async Task Send_ChangesTheState()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var changes = new List<DeviceStateChange>();
        session.StateChanged += (_, args) => changes.Add(args.Change);

        await session.SendData(Range(serializer, "AddressMap.Edit.Filter.Cutoff"), Cutoff(77), Ct);
        var sink = new FieldValueStore();
        await session.RequestData(Range(serializer, "AddressMap.Edit.Filter.Cutoff"), sink, Ct);

        Assert.Equal([DeviceStateChange.Received, DeviceStateChange.Sent], changes);
        Assert.True(sink.TryGet("AddressMap.Edit.Filter.Cutoff", InstancePath.Empty, out var cutoff));
        Assert.Equal(77, Convert.ToInt32(cutoff.Value));
        Assert.Single(device.Requests);
    }

    [Fact]
    public async Task Invalidate_TheNextReceiveRequests()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var patch = Range(serializer, "AddressMap.Memory.Patch", 2);

        await session.RequestData(patch, new FieldValueStore(), Ct);
        session.Invalidate(Range(serializer, "AddressMap.Memory.Patch.Level", 2));
        await session.RequestData(patch, new FieldValueStore(), Ct);

        Assert.Equal(2, device.Requests.Count);
    }

    [Fact]
    public async Task DataOfTheDevice_UpdatesTheState()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        var patch = Range(serializer, "AddressMap.Memory.Patch", 5);
        await session.RequestData(patch, new FieldValueStore(), Ct);
        var received = 0;
        session.StateChanged += (_, args) => received += args.Change == DeviceStateChange.Received ? 1 : 0;

        // a front panel change: the device sends the patch on its own
        device.Push(DataMessage(PatchAddress(5), Patch("Changed!")));
        await FakeDevice.WaitFor(() => received > 0);
        var sink = new FieldValueStore();
        await session.RequestData(patch, sink, Ct);

        Assert.Single(device.Requests);
        Assert.True(sink.TryGet("AddressMap.Memory.Patch.Name", new InstancePath([5]), out var name));
        Assert.Equal("Changed!", name.Value);
    }

    [Fact]
    public async Task Disconnected_TheSessionCannotBeUsed()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var session = CreateSession(serializer, device);
        MidiConnectionStateChangedArgs? lost = null;
        session.Disconnected += (_, args) => lost = args;

        device.Disconnect();

        Assert.False(session.IsConnected);
        Assert.Equal(MidiConnectionState.Closed, lost?.NewState);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RequestData(Range(serializer, "AddressMap.Edit"), new FieldValueStore(), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendData(Range(serializer, "AddressMap.Edit.Filter.Cutoff"), Cutoff(77), Ct));
    }
}
