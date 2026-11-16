using System.Text.Json.Nodes;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Device.PropertyExchange;
using Jacobi.Midi.Tests.Addressing;
using Jacobi.Midi.Tests.Commands;
using Xunit;

namespace Jacobi.Midi.Tests.PropertyExchange;

/// <summary>
/// Tests a device that describes itself with Property Exchange, with the data the General MIDI synthesizer of Windows
/// MIDI Services gives: discovery, the schema generated with the published schemas of the standard resources, and a
/// <see cref="DeviceSession"/> that reads a <c>pe</c> root with a Get.
/// </summary>
public class PropertyExchangeSessionTests
{
    private static readonly Dictionary<string, string> Synthesizer = new()
    {
        ["ResourceList"] = """[{"resource":"DeviceInfo"},{"resource":"ChannelList","canSubscribe":true},{"resource":"ProgramList","requireResId":true}]""",
        ["DeviceInfo"] = """{"manufacturerId":[0,0,65],"manufacturer":"Microsoft","familyId":[11,0],"family":"Windows","modelId":[1,0],"model":"General MIDI Synth","versionId":[1,0,0,0],"version":"1.0.0.0"}""",
        ["ChannelList"] = """[{"title":"Melodic","channel":1,"programTitle":"Piano 1","bankPC":[0,0,0],"links":[{"resource":"ProgramList","resId":"melodic"}]},{"title":"Drums","channel":10,"programTitle":"Standard","bankPC":[0,0,0],"links":[{"resource":"ProgramList","resId":"drums"}]}]""",
        ["ProgramList/melodic"] = """[{"title":"Piano 1","bankPC":[0,0,0]},{"title":"Piano 2","bankPC":[0,0,1]}]""",
        ["ProgramList/drums"] = """[{"title":"Standard","bankPC":[120,0,0]}]""",
    };

    private static async Task<(PropertyExchangeDevice Device, PropertySchema Schema, Serializer Serializer)> Discover(FakeCapabilityInquiry ci)
    {
        var device = await PropertyExchangeDevice.Discover(ci, CancellationToken.None);
        Assert.NotNull(device);
        var schema = await device.GenerateSchema("Synth", CancellationToken.None);
        return (device, schema, TestDevice.CreateSerializer(schema.Document));
    }

    private static DeviceSession CreateSession(Serializer serializer, PropertyExchangeDevice? device)
        => new(serializer, new FakeCommandConnection(), new DeviceValues(), default, properties: device);

    private static PlanRange Whole(Serializer serializer, string root)
        => serializer.GetPlan(new SchemaName($"Synth.{root}")).CreateRange(root);

    private static object? Value(FieldValueStore values, string path, params int[] instance)
        => values.TryGet(path, new InstancePath(instance), out var value) ? value.Value : null;

    [Fact]
    public async Task Discover_ReadsTheResourceList_AndGeneratesTheStandardResources()
    {
        var ci = new FakeCapabilityInquiry(Synthesizer);

        var (device, schema, serializer) = await Discover(ci);

        Assert.Equal(FakeCapabilityInquiry.ResponderMuid, device.Responder);
        Assert.Equal(["DeviceInfo", "ChannelList", "ProgramList"], device.Resources.Select(resource => resource.Name));
        Assert.Equal(["DeviceInfo", "ChannelList", "ProgramList"], schema.Document.Roots.Select(root => root.Name));
        Assert.All(schema.Document.Roots, root => serializer.GetPlan(new SchemaName($"Synth.{root.Name}")));
        // the resIds of ProgramList come from the links in the ChannelList
        var entries = PropertyData.EntriesOf(serializer.GetPlan(new SchemaName("Synth.ProgramList")).Root);
        Assert.Equal(["melodic", "drums"], entries!.Members.Select(member => member.Name));
    }

    [Fact]
    public async Task RequestData_APeRoot_IsReadWithAGet()
    {
        var ci = new FakeCapabilityInquiry(Synthesizer);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);
        var changes = new List<DeviceStateChange>();
        session.StateChanged += (_, args) => changes.Add(args.Change);

        var values = new FieldValueStore();
        var result = await session.RequestData(Whole(serializer, "DeviceInfo"), values, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal("DeviceInfo", ci.Gets[^1]["resource"]!.GetValue<string>());
        Assert.Equal("Microsoft", Value(values, "DeviceInfo.manufacturer"));
        Assert.Equal("General MIDI Synth", Value(values, "DeviceInfo.model"));
        Assert.Equal(65L, Value(values, "DeviceInfo.manufacturerId.item", 2));
        Assert.Equal([DeviceStateChange.Received], changes);
    }

    [Fact]
    public async Task RequestData_AListResource_IsARepeat()
    {
        var (device, _, serializer) = await Discover(new FakeCapabilityInquiry(Synthesizer));
        await using var session = CreateSession(serializer, device);

        var values = new FieldValueStore();
        await session.RequestData(Whole(serializer, "ChannelList"), values, CancellationToken.None);

        Assert.Equal("Melodic", Value(values, "ChannelList.items.title", 0));
        Assert.Equal(10L, Value(values, "ChannelList.items.channel", 1));
        Assert.Equal("Standard", Value(values, "ChannelList.items.programTitle", 1));
    }

    [Fact]
    public async Task RequestData_AResourceWithEntries_IsReadPerResId()
    {
        var ci = new FakeCapabilityInquiry(Synthesizer);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);
        var gets = ci.Gets.Count;

        var values = new FieldValueStore();
        var result = await session.RequestData(Whole(serializer, "ProgramList"), values, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Blocks.Count);
        Assert.Equal(["melodic", "drums"], ci.Gets.Skip(gets).Select(header => header["resId"]!.GetValue<string>()));
        Assert.Equal("Piano 2", Value(values, "ProgramList.resId.melodic.items.title", 1));
        Assert.Equal(120L, Value(values, "ProgramList.resId.drums.items.bankPC.item", 0, 0));
    }

    [Fact]
    public async Task RequestData_ARangeInOneEntry_ReadsOnlyThatEntry()
    {
        var ci = new FakeCapabilityInquiry(Synthesizer);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);
        var gets = ci.Gets.Count;

        var values = new FieldValueStore();
        var range = serializer.GetPlan(new SchemaName("Synth.ProgramList")).CreateRange("ProgramList.resId.drums");
        var result = await session.RequestData(range, values, CancellationToken.None);

        Assert.Equal(BlockStatus.Completed, Assert.Single(result.Blocks).Status);
        Assert.Equal("drums", Assert.Single(ci.Gets.Skip(gets))["resId"]!.GetValue<string>());
        Assert.Equal("Standard", Value(values, "ProgramList.resId.drums.items.title", 0));
        Assert.Null(Value(values, "ProgramList.resId.melodic.items.title", 0));
    }

    [Fact]
    public async Task RequestData_AResourceThatNeedsAnId_OrIsNotThere_IsNotCompleted()
    {
        // no links to the entries of ProgramList: it is a plain root
        var resources = new Dictionary<string, string>(Synthesizer)
        {
            ["ChannelList"] = """[{"title":"Melodic","channel":1,"programTitle":"Piano 1","bankPC":[0,0,0]}]""",
        };
        var (device, _, serializer) = await Discover(new FakeCapabilityInquiry(resources));
        resources.Remove("ChannelList");
        await using var session = CreateSession(serializer, device);

        var program = await session.RequestData(Whole(serializer, "ProgramList"), new FieldValueStore(), CancellationToken.None);
        var channels = await session.RequestData(Whole(serializer, "ChannelList"), new FieldValueStore(), CancellationToken.None);

        Assert.Equal(BlockStatus.Unavailable, Assert.Single(program.Blocks).Status);
        var failed = Assert.Single(channels.Blocks);
        Assert.Equal(BlockStatus.Failed, failed.Status);
        Assert.Contains("404", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestData_Again_IsAnsweredFromTheState_UnlessFromTheDevice()
    {
        var ci = new FakeCapabilityInquiry(Synthesizer);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);
        await session.RequestData(Whole(serializer, "DeviceInfo"), new FieldValueStore(), CancellationToken.None);
        var gets = ci.Gets.Count;

        var values = new FieldValueStore();
        var result = await session.RequestData(Whole(serializer, "DeviceInfo"), values, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal(gets, ci.Gets.Count);
        Assert.Equal("General MIDI Synth", Value(values, "DeviceInfo.model"));
        Assert.Equal(65L, Value(values, "DeviceInfo.manufacturerId.item", 2));

        await session.RequestData(Whole(serializer, "DeviceInfo"), new FieldValueStore(), SessionData.Device, null, CancellationToken.None);
        Assert.Equal(gets + 1, ci.Gets.Count);
    }

    [Fact]
    public async Task RequestData_FromTheState_GivesTheItemCountsOfTheData()
    {
        var (device, _, serializer) = await Discover(new FakeCapabilityInquiry(Synthesizer));
        await using var session = CreateSession(serializer, device);
        await session.RequestData(Whole(serializer, "ProgramList"), new FieldValueStore(), CancellationToken.None);

        var values = new FieldValueStoreWithCounts();
        await session.RequestData(Whole(serializer, "ProgramList"), values, CancellationToken.None);

        Assert.True(values.TryGetCount("ProgramList.resId.melodic.items", InstancePath.Empty, out var melodic));
        Assert.Equal(2, melodic);
        Assert.True(values.TryGetCount("ProgramList.resId.drums.items", InstancePath.Empty, out var drums));
        Assert.Equal(1, drums);
        Assert.True(values.TryGetCount("ProgramList.resId.melodic.items.bankPC", new InstancePath([1]), out var bankPC));
        Assert.Equal(3, bankPC);
        Assert.True(values.TryGet("ProgramList.resId.melodic.items.bankPC.item", new InstancePath([1, 2]), out var program));
        Assert.Equal(1L, program.Value);
    }

    [Fact]
    public async Task Invalidate_AnEntry_GetsOnlyThatEntryAgain()
    {
        var ci = new FakeCapabilityInquiry(Synthesizer);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);
        var whole = Whole(serializer, "ProgramList");
        await session.RequestData(whole, new FieldValueStore(), CancellationToken.None);
        var gets = ci.Gets.Count;

        session.Invalidate(serializer.GetPlan(new SchemaName("Synth.ProgramList")).CreateRange("ProgramList.resId.drums.items"));
        var values = new FieldValueStore();
        var result = await session.RequestData(whole, values, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal("drums", Assert.Single(ci.Gets.Skip(gets))["resId"]!.GetValue<string>());
        Assert.Equal("Piano 2", Value(values, "ProgramList.resId.melodic.items.title", 1));
        Assert.Equal("Standard", Value(values, "ProgramList.resId.drums.items.title", 0));
    }

    [Fact]
    public async Task TheBodyOfAnEntry_IsTheDataOfItsGet()
    {
        var (_, _, serializer) = await Discover(new FakeCapabilityInquiry(Synthesizer));
        var plan = serializer.GetPlan(new SchemaName("Synth.ProgramList"));
        var state = new PropertyDeviceState(plan);
        var drums = (GroupInfo)PropertyData.EntriesOf(plan.Root)!.Members[1];
        var data = System.Text.Json.Nodes.JsonNode.Parse(Synthesizer["ProgramList/drums"]);

        state.Take(drums, data);

        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(data, state.Body(drums)));
        // the pointer is relative to the entry; ProgramList cannot be set (read-only), so a partial body has nothing
        var bank = (FieldInfo)plan.Find(new SchemaPath("ProgramList.resId.drums.items.bankPC.item"))!;
        Assert.Equal("/0/bankPC/0", PropertyData.PointerOf(drums, bank, new InstancePath([0, 0])));
        Assert.Empty(state.PartialBody(drums, state.Whole));
    }

    // resources that can be set: whole (a value, an object) and partially
    private static readonly Dictionary<string, string> Settable = new()
    {
        ["ResourceList"] = """
            [{"resource":"DeviceInfo"},
             {"resource":"X-Tempo","canSet":"full","schema":{"type":"integer","minimum":40,"maximum":240}},
             {"resource":"X-Globset","canSet":"full","schema":{"type":"object","properties":{"deviceName":{"type":"string"},"level":{"type":"integer"}}}},
             {"resource":"X-Edit","canSet":"partial","schema":{"type":"object","properties":{"name":{"type":"string"},"rates":{"type":"array","items":{"type":"integer"}}}}}]
            """,
        ["DeviceInfo"] = Synthesizer["DeviceInfo"],
        ["X-Tempo"] = "120",
        ["X-Globset"] = """{"deviceName":"Synth","level":3}""",
        ["X-Edit"] = """{"name":"Lead","rates":[1,2,3]}""",
    };

    private static PlanRange Field(Serializer serializer, string root, string path, params int[] instance)
        => serializer.GetPlan(new SchemaName($"Synth.{root}")).CreateRange(new SchemaPath(path), new InstancePath(instance), new SchemaPath(path), new InstancePath(instance));

    private static FieldValueStore Source(string path, object value, params int[] instance)
    {
        var source = new FieldValueStore();
        source.Set(path, new InstancePath(instance), new LogicalField(path[(path.LastIndexOf('.') + 1)..], value.GetType(), value));
        return source;
    }

    [Fact]
    public async Task SendData_AResourceSetWhole_GetsWhatItDoesNotHold_AndSetsAllOfIt()
    {
        var ci = new FakeCapabilityInquiry(Settable);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);
        var changes = new List<DeviceStateChange>();
        session.StateChanged += (_, args) => changes.Add(args.Change);
        var gets = ci.Gets.Count;

        var result = await session.SendData(Field(serializer, "XGlobset", "XGlobset.level"), Source("XGlobset.level", 9L), CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal("X-Globset", Assert.Single(ci.Gets.Skip(gets))["resource"]!.GetValue<string>());
        var (header, body) = Assert.Single(ci.Sets);
        Assert.Null(header["setPartial"]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"deviceName":"Synth","level":9}"""), body), body?.ToJsonString());
        Assert.Equal([DeviceStateChange.Received, DeviceStateChange.Sent], changes);
    }

    [Fact]
    public async Task SendData_AValueResource_SetsTheValue()
    {
        var ci = new FakeCapabilityInquiry(Settable);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);

        await session.SendData(Field(serializer, "XTempo", "XTempo.value"), Source("XTempo.value", 130L), CancellationToken.None);

        Assert.True(JsonNode.DeepEquals(JsonValue.Create(130L), Assert.Single(ci.Sets).Body));
    }

    [Fact]
    public async Task SendData_APartialResource_SetsTheRangeByJsonPointer()
    {
        var ci = new FakeCapabilityInquiry(Settable);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);

        var result = await session.SendData(Field(serializer, "XEdit", "XEdit.rates.item", 1), Source("XEdit.rates.item", 20L, 1), CancellationToken.None);

        Assert.True(result.IsComplete);
        var (header, body) = Assert.Single(ci.Sets);
        Assert.True(header["setPartial"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"/rates/1":20}"""), body), body?.ToJsonString());
        // the state has the value that was set
        var values = new FieldValueStore();
        await session.RequestData(Whole(serializer, "XEdit"), values, CancellationToken.None);
        Assert.Equal(20L, Value(values, "XEdit.rates.item", 1));
    }

    [Fact]
    public async Task SendData_AReadOnlyResource_IsNotSent()
    {
        var ci = new FakeCapabilityInquiry(Settable);
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);

        var result = await session.SendData(Field(serializer, "DeviceInfo", "DeviceInfo.model"), Source("DeviceInfo.model", "Other"), CancellationToken.None);

        Assert.Equal(BlockStatus.Unavailable, Assert.Single(result.Blocks).Status);
        Assert.Empty(ci.Sets);
    }

    [Fact]
    public async Task SendData_ASetThatFails_InvalidatesTheBlock()
    {
        var ci = new FakeCapabilityInquiry(Settable) { SetStatus = 400 };
        var (device, _, serializer) = await Discover(ci);
        await using var session = CreateSession(serializer, device);
        var changes = new List<DeviceStateChange>();
        session.StateChanged += (_, args) => changes.Add(args.Change);

        var result = await session.SendData(Field(serializer, "XGlobset", "XGlobset.level"), Source("XGlobset.level", 9L), CancellationToken.None);
        var gets = ci.Gets.Count;
        var values = new FieldValueStore();
        await session.RequestData(Whole(serializer, "XGlobset"), values, CancellationToken.None);

        var failed = Assert.Single(result.Blocks);
        Assert.Equal(BlockStatus.Failed, failed.Status);
        Assert.Contains("400", failed.Reason, StringComparison.Ordinal);
        Assert.Equal(DeviceStateChange.Invalidated, changes[1]);
        // not held anymore: read again, with the device's value
        Assert.Equal(gets + 1, ci.Gets.Count);
        Assert.Equal(3L, Value(values, "XGlobset.level"));
    }

    [Fact]
    public async Task APeRoot_WithoutAPropertyExchangeDevice_HasNoExchange()
    {
        var (_, _, serializer) = await Discover(new FakeCapabilityInquiry(Synthesizer));
        await using var session = CreateSession(serializer, device: null);

        await Assert.ThrowsAsync<ExchangeException>(() => session.RequestData(Whole(serializer, "DeviceInfo"), new FieldValueStore(), CancellationToken.None));
    }
}
