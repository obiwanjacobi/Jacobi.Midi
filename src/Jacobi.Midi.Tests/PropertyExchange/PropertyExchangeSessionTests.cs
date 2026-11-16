using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
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
    public async Task APeRoot_WithoutAPropertyExchangeDevice_HasNoExchange()
    {
        var (_, _, serializer) = await Discover(new FakeCapabilityInquiry(Synthesizer));
        await using var session = CreateSession(serializer, device: null);

        await Assert.ThrowsAsync<ExchangeException>(() => session.RequestData(Whole(serializer, "DeviceInfo"), new FieldValueStore(), CancellationToken.None));
    }
}
