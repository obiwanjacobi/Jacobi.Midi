using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Commands;

/// <summary>
/// Tests a <see cref="DeviceSession"/> with a command root: it routes the range to the command messages, keeps the
/// state of the root and cannot request it.
/// </summary>
public class DeviceSessionCommandTests
{
    private const string Schema = """
        <schema name="Synth">
          <members>
            <group name="Commands" midi.role="cmd1">
              <members>
                <field name="Volume" datatype="sys.uint8" midi.cmd="cc#7" />
                <field name="Program" datatype="sys.uint8" midi.cmd="pc" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private static readonly SchemaName Root = new("Synth.Commands");

    private static Serializer CreateSerializer() => TestDevice.CreateSerializer(TestDevice.Parse(Schema));

    private static DeviceSession CreateSession(Serializer serializer, FakeCommandConnection connection, ExchangeDefinitions? exchanges = null)
        => new(serializer, connection, new DeviceValues { ["channel"] = 0 }, MidiGroupPair.Same(new MidiGroup(0)), exchanges);

    private static PlanRange Field(Serializer serializer, string path) => serializer.GetPlan(Root).CreateRange(path, path);

    private static PlanRange Whole(Serializer serializer) => serializer.GetPlan(Root).CreateRange(Root.Name);

    private static FieldValueStore Volume(byte value)
    {
        var values = new FieldValueStore();
        values.Set("Commands.Volume", InstancePath.Empty, new LogicalField("Volume", typeof(byte), value));
        return values;
    }

    [Fact]
    public async Task RequestData_NothingHeld_IsUnavailable()
    {
        var serializer = CreateSerializer();
        await using var session = CreateSession(serializer, new FakeCommandConnection());

        var result = await session.RequestData(Whole(serializer), new FieldValueStore(), CancellationToken.None);

        Assert.Equal([BlockStatus.Unavailable, BlockStatus.Unavailable], result.Blocks.Select(block => block.Status));
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SendData_SendsTheCommand_AndTheStateHoldsIt()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var session = CreateSession(serializer, connection);
        var changes = new List<DeviceStateChange>();
        session.StateChanged += (_, args) => changes.Add(args.Change);

        var sent = await session.SendData(Field(serializer, "Commands.Volume"), Volume(100), CancellationToken.None);
        var values = new FieldValueStore();
        var received = await session.RequestData(Field(serializer, "Commands.Volume"), values, CancellationToken.None);

        Assert.True(sent.IsComplete);
        Assert.Equal(0x20B00764u, Assert.Single(connection.Sent)[0]);
        Assert.Equal([DeviceStateChange.Sent], changes);
        Assert.True(received.IsComplete);
        Assert.True(values.TryGet("Commands.Volume", InstancePath.Empty, out var value));
        Assert.Equal((byte)100, value.Value);
    }

    [Fact]
    public async Task Listening_AValueTheDeviceSends_IsReceived()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var session = CreateSession(serializer, connection);
        var received = new TaskCompletionSource<PlanRange>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, args) =>
        {
            if (args.Change == DeviceStateChange.Received)
                received.TrySetResult(args.Nodes);
        };

        // the first use of the root starts listening to it
        await session.RequestData(Whole(serializer), new FieldValueStore(), CancellationToken.None);
        connection.Receive([new UniversalPacket([0x20C00500])]);
        var nodes = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var values = new FieldValueStore();
        var result = await session.RequestData(Field(serializer, "Commands.Program"), values, CancellationToken.None);

        Assert.Equal("Commands.Program", nodes.First.Path.Value);
        Assert.True(result.IsComplete);
        Assert.True(values.TryGet("Commands.Program", InstancePath.Empty, out var value));
        Assert.Equal((byte)5, value.Value);
    }

    [Fact]
    public async Task Execute_TheStandardExchanges_RunOnACommandRoot_OthersDoNot()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var session = CreateSession(serializer, connection);
        var range = Field(serializer, "Commands.Volume");

        var sent = await session.Execute(ExchangeNames.SendData, range, new ExchangeData { Source = Volume(1) }, null, CancellationToken.None);

        Assert.True(sent.IsComplete);
        Assert.Single(connection.Sent);
        await Assert.ThrowsAsync<ExchangeException>(() => session.Execute("store", range, new ExchangeData(), null, CancellationToken.None));
        await Assert.ThrowsAsync<ExchangeException>(() => session.Probe(range, CancellationToken.None));
    }

    [Fact]
    public async Task AMessageMapRoot_WithoutExchangeFiles_HasNoExchange()
    {
        var serializer = CreateSerializer();
        await using var session = CreateSession(serializer, new FakeCommandConnection());
        var range = serializer.GetPlan(TestDevice.MapName).CreateRange(TestDevice.MapName.Name);

        await Assert.ThrowsAsync<ExchangeException>(() => session.RequestData(range, new FieldValueStore(), CancellationToken.None));
    }
}
