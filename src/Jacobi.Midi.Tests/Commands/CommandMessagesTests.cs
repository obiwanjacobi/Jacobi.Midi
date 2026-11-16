using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Commands;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Jacobi.Midi.Tests.Console;
using Xunit;

namespace Jacobi.Midi.Tests.Commands;

/// <summary>
/// Tests the command roots (<c>midi.role="cmd1"|"cmd2"</c>, <c>midi.cmd</c>): their validation, and sending and
/// receiving their values as channel messages with <see cref="CommandMessages"/>.
/// </summary>
public class CommandMessagesTests
{
    private const string Schema = """
        <schema name="Synth">
          <dataTypeDefs>
            <dataTypeDef name="pan" basedOn="sys.uint8" min="-64" max="63" shift="-64" />
          </dataTypeDefs>
          <members>
            <group name="Mono" midi.role="cmd1">
              <members>
                <field name="Volume" datatype="sys.uint8" midi.cmd="cc#7" />
                <field name="Pan" datatype="pan" midi.cmd="cc#10" />
                <group name="Filter">
                  <members>
                    <field name="Cutoff" datatype="sys.uint16" midi.cmd="cc#1" />
                    <field name="Resonance" datatype="sys.uint16" midi.cmd="nrpn#3/12" />
                  </members>
                </group>
                <field name="BendRange" datatype="sys.uint8" midi.cmd="rpn#0" />
                <field name="Program" datatype="sys.uint8" midi.cmd="pc" />
              </members>
            </group>
            <group name="Poly" midi.role="cmd2" midi.device.channel="2">
              <members>
                <field name="Volume" datatype="sys.uint32" midi.cmd="cc#7" />
                <field name="Cutoff" datatype="sys.uint32" midi.cmd="nrpn#0x0101" />
                <field name="Program" datatype="sys.uint8" midi.cmd="pc" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private static readonly MidiGroupPair Group0 = MidiGroupPair.Same(new MidiGroup(0));
    private static readonly MidiGroupPair Group1 = MidiGroupPair.Same(new MidiGroup(1));

    private static readonly SchemaName Mono = new("Synth.Mono");
    private static readonly SchemaName Poly = new("Synth.Poly");

    private static Serializer CreateSerializer(string schema = Schema)
        => TestDevice.CreateSerializer(TestDevice.Parse(schema));

    private static PlanRange Whole(Serializer serializer, SchemaName root)
        => serializer.GetPlan(root).CreateRange(root.Name);

    private static PlanRange Field(Serializer serializer, SchemaName root, string path)
        => serializer.GetPlan(root).CreateRange(path, path);

    private static FieldValueStore Values(params (string Path, object Value)[] values)
    {
        var store = new FieldValueStore();
        foreach (var (path, value) in values)
        {
            store.Set(path, InstancePath.Empty, new LogicalField(path[(path.LastIndexOf('.') + 1)..], value.GetType(), value));
        }
        return store;
    }

    private static object? Value(FieldValueStore store, string path)
        => store.TryGet(path, InstancePath.Empty, out var value) ? value.Value : null;

    private static string Problems(Serializer serializer, SchemaName root)
    {
        var problems = new MessageMapProblems();
        CommandMap.TryCreate(serializer, serializer.GetPlan(root), problems);
        return String.Join(Environment.NewLine, problems.Where(problem => problem.Severity == MessageMapProblemSeverity.Error));
    }

    // a schema with one cmd1 root of the given members
    private static Serializer WithMembers(string members, string role = "cmd1")
        => CreateSerializer($"""
            <schema name="Synth">
              <members>
                <group name="Root" midi.role="{role}">
                  <members>
                    {members}
                  </members>
                </group>
              </members>
            </schema>
            """);

    private static readonly SchemaName Root = new("Synth.Root");

    [Theory]
    [InlineData("cc#74", "cc#74")]
    [InlineData("CC#0x4A", "cc#74")]
    [InlineData("nrpn#3/12", "nrpn#396")]
    [InlineData("rpn#0", "rpn#0")]
    [InlineData("nrpn#16383", "nrpn#16383")]
    [InlineData("pc", "pc")]
    public void Command_TryParse_Valid(string text, string expected)
    {
        Assert.True(Command.TryParse(text, out var command, out var error), error);
        Assert.Equal(expected, command.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("cc")]
    [InlineData("cc#128")]
    [InlineData("cc#1/2")]
    [InlineData("nrpn#16384")]
    [InlineData("nrpn#128/0")]
    [InlineData("pc#1")]
    [InlineData("note#60")]
    public void Command_TryParse_Invalid(string text)
        => Assert.False(Command.TryParse(text, out _, out _));

    [Fact]
    public void Validate_TheTestRoots_HaveNoProblems()
    {
        var serializer = CreateSerializer();

        Assert.Equal(String.Empty, Problems(serializer, Mono));
        Assert.Equal(String.Empty, Problems(serializer, Poly));
        Assert.DoesNotContain(MessageMapValidator.Validate(serializer), problem => problem.Document == "Synth");
    }

    [Theory]
    [InlineData("""<field name="A" datatype="sys.uint8" />""", "needs midi.cmd")]
    [InlineData("""<field name="A" datatype="sys.uint8" midi.cmd="cc#300" />""", "controller number")]
    [InlineData("""<field name="A" datatype="sys.uint32" midi.cmd="cc#1" />""", "must be a 1-byte")]
    [InlineData("""<field name="A" datatype="sys.uint16" midi.cmd="pc" />""", "1-byte unsigned integer (7 bits: the program)")]
    [InlineData("""<field name="A" datatype="sys.uint8" midi.cmd="cc#6" />""", "part of an RPN/NRPN")]
    [InlineData("""<field name="A" datatype="sys.uint8" midi.cmd="cc#121" />""", "channel mode")]
    [InlineData("""<field name="A" datatype="sys.uint16" midi.cmd="cc#40" />""", "from 0 to 31")]
    [InlineData("""<field name="A" datatype="sys.uint16" midi.cmd="cc#1" /><field name="B" datatype="sys.uint8" midi.cmd="cc#33" />""", "also used by")]
    [InlineData("""<field name="A" datatype="sys.uint8" value="1" midi.cmd="cc#1" />""", "fixed value")]
    [InlineData("""<field name="A" datatype="sys.uint8" midi.cmd="cc#1" midi.role="address" />""", "does not apply in a command root")]
    [InlineData("""<repeat name="R" count="2"><members><field name="A" datatype="sys.uint8" midi.cmd="cc#1" /></members></repeat>""", "cannot have a repeat")]
    public void Validate_ARuleOfACommandRoot_IsAnError(string members, string expected)
        => Assert.Contains(expected, Problems(WithMembers(members), Root), StringComparison.Ordinal);

    [Fact]
    public void Validate_ACmd2Root_NeedsA32BitTypeAndNoBankSelect()
    {
        Assert.Contains("4-byte", Problems(WithMembers("""<field name="A" datatype="sys.uint8" midi.cmd="cc#1" />""", "cmd2"), Root), StringComparison.Ordinal);
        Assert.Contains("bank select", Problems(WithMembers("""<field name="A" datatype="sys.uint32" midi.cmd="cc#0" />""", "cmd2"), Root), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TheValidator_ReportsTheProblemsOfACommandRoot()
    {
        var problems = MessageMapValidator.Validate(WithMembers("""<field name="A" datatype="sys.uint8" />"""));

        Assert.Contains(problems, problem => problem.Path.Value == "Root.A" && problem.Message.Contains("needs midi.cmd", StringComparison.Ordinal));
    }

    [Fact]
    public void MessageMaps_ACommandRoot_HasNoMessageMap()
    {
        var serializer = CreateSerializer();

        var exception = Assert.Throws<InvalidOperationException>(() => new MessageMaps(serializer).Info(Mono));
        Assert.Contains("command root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_Cmd1_WritesMidi1ChannelMessages()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues { ["channel"] = 3 }, Group0);

        await commands.Send(Whole(serializer, Mono), Values(
            ("Mono.Volume", (byte)100), ("Mono.Pan", -10m), ("Mono.Filter.Cutoff", (ushort)1000),
            ("Mono.Filter.Resonance", (ushort)300), ("Mono.BendRange", (byte)2), ("Mono.Program", (byte)5)),
            CancellationToken.None);

        Assert.Equal<uint>(
        [
            0x20B30764,                                     // cc 7: 100
            0x20B30A36,                                     // cc 10: -10 + 64
            0x20B30107, 0x20B32168,                         // cc 1/33: 1000 = 7 << 7 | 104
            0x20B36303, 0x20B3620C, 0x20B30602, 0x20B3262C, // nrpn 3/12: 300 = 2 << 7 | 44
            0x20B36500, 0x20B36400, 0x20B30602,             // rpn 0/0: 2 (7 bits: data entry MSB only)
            0x20C30500,                                     // program 5
        ], connection.Sent.Select(packet => packet[0]));
    }

    [Fact]
    public async Task Send_Cmd2_WritesMidi2ChannelMessages_WithTheChannelOfTheRoot()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues(), Group1);

        await commands.Send(Whole(serializer, Poly), Values(
            ("Poly.Volume", 0x80000000u), ("Poly.Cutoff", 5u), ("Poly.Program", (byte)9)), CancellationToken.None);

        Assert.Equal<(uint, uint)>(
        [
            (0x41B20700, 0x80000000),   // cc 7
            (0x41320201, 5),            // assignable controller 2/1
            (0x41C20000, 0x09000000),   // program 9, no bank
        ], connection.Sent.Select(packet => (packet[0], packet[1])));
    }

    [Fact]
    public async Task Send_TheClientsChannel_OverridesTheRoots()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues { ["channel"] = 5 }, Group1);

        await commands.Send(Field(serializer, Poly, "Poly.Program"), Values(("Poly.Program", (byte)1)), CancellationToken.None);

        Assert.Equal(0x41C50000u, Assert.Single(connection.Sent)[0]);
    }

    [Fact]
    public async Task Send_AMissingValue_SendsNothing()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues { ["channel"] = 0 }, Group0);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => commands.Send(Whole(serializer, Mono), Values(("Mono.Volume", (byte)1)), CancellationToken.None));

        Assert.Contains("Mono.Pan", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Mono.Program", exception.Message, StringComparison.Ordinal);
        Assert.Empty(connection.Sent);
    }

    [Fact]
    public async Task Send_WithoutAChannel_IsAnError()
    {
        var serializer = CreateSerializer();
        await using var commands = new CommandMessages(serializer, new FakeCommandConnection(), new DeviceValues(), Group0);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => commands.Send(Field(serializer, Mono, "Mono.Volume"), Values(("Mono.Volume", (byte)1)), CancellationToken.None));
        Assert.Contains("channel", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listen_Cmd1_AssemblesMidi1Sequences_AndIgnoresOtherChannels()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues { ["channel"] = 3 }, Group0);

        var values = await Listen(commands, serializer, Whole(serializer, Mono), connection, 6,
            0x20B40701,                                     // cc 7 on channel 4: ignored
            0x20B30764,                                     // cc 7: 100
            0x20B30A36,                                     // cc 10: 54 = -10
            0x20B30107, 0x20B32168,                         // cc 1/33: 1000
            0x20B36303, 0x20B3620C, 0x20B30602, 0x20B3262C, // nrpn 3/12: 300
            0x20B36500, 0x20B36400, 0x20B30602,             // rpn 0/0: 2 << 7 as 14 bits, the 7 bits of the field: 2
            0x20C30500);                                    // program 5

        Assert.Equal((byte)100, Value(values, "Mono.Volume"));
        Assert.Equal(-10m, Value(values, "Mono.Pan"));
        Assert.Equal((ushort)1000, Value(values, "Mono.Filter.Cutoff"));
        Assert.Equal((ushort)300, Value(values, "Mono.Filter.Resonance"));
        Assert.Equal((byte)2, Value(values, "Mono.BendRange"));
        Assert.Equal((byte)5, Value(values, "Mono.Program"));
    }

    [Fact]
    public async Task Listen_Cmd1_ScalesMidi2MessagesDown()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues { ["channel"] = 3 }, Group0);

        // cc 7 = 0x80000000 as MIDI 2.0: 64 in 7 bits
        var values = await Listen(commands, serializer, Field(serializer, Mono, "Mono.Volume"), connection, 1,
            new UniversalPacket([0x40B30700, 0x80000000]));

        Assert.Equal((byte)64, Value(values, "Mono.Volume"));
    }

    [Fact]
    public async Task Listen_Cmd2_ScalesMidi1MessagesUp_AndOnlyTheRange()
    {
        var serializer = CreateSerializer();
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues(), Group1);

        // group 1, channel 2: cc 7 = 64 (the center: 0x80000000) and a program change outside the range
        var values = await Listen(commands, serializer, Field(serializer, Poly, "Poly.Volume"), connection, 1,
            new UniversalPacket([0x21C20900]), new UniversalPacket([0x21B20740]));

        Assert.Equal(0x80000000u, Value(values, "Poly.Volume"));
        Assert.Null(Value(values, "Poly.Program"));
    }

    [Fact]
    public async Task Gx700_TheProgram_IsAProgramChange()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(Jacobi.Midi.Tests.Schema.TestHelpers.CreateSchemas())
            .AddProcessors(Jacobi.Midi.Tests.Schema.TestHelpers.CreateProcessors())
            .Build();
        var root = new SchemaName("Gx700.Commands");
        var connection = new FakeCommandConnection();
        await using var commands = new CommandMessages(serializer, connection, new DeviceValues { ["channel"] = 0 }, Group0);

        Assert.Empty(commands.Validate(root));
        // program 5 (1-128) is program change 4
        await commands.Send(Whole(serializer, root), Values(("Commands.Program", 5m)), CancellationToken.None);

        Assert.Equal(0x20C00400u, Assert.Single(connection.Sent)[0]);
    }

    [Theory]
    [InlineData(10u, 0x1400u)]
    [InlineData(64u, 0x8000u)]
    [InlineData(87u, 0xAEBAu)]
    [InlineData(127u, 0xFFFFu)]
    public void Scale_SevenToSixteenBits_AsTheSpecificationExamples(uint value, uint expected)
    {
        Assert.Equal(expected, CommandWire.Scale(value, 7, 16));
        Assert.Equal(value, CommandWire.Scale(expected, 16, 7));
    }

    // listens to the range, receives the packets and returns the values once the sink has the expected number
    private static Task<FieldValueStore> Listen(CommandMessages commands, Serializer serializer, PlanRange range,
        FakeCommandConnection connection, int expected, params uint[] words)
        => Listen(commands, serializer, range, connection, expected, [.. words.Select(word => new UniversalPacket([word]))]);

    private static async Task<FieldValueStore> Listen(CommandMessages commands, Serializer serializer, PlanRange range,
        FakeCommandConnection connection, int expected, params UniversalPacket[] packets)
    {
        var values = new FieldValueStore();
        using var cancel = new CancellationTokenSource();
        var listening = commands.Listen(range, new CountingSink(values), cancel.Token);
        connection.Receive(packets);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (CountingSink.Count(values) < expected && DateTime.UtcNow < deadline && !listening.IsCompleted)
        {
            await Task.Delay(10);
        }
        await cancel.CancelAsync();
        await listening;
        return values;
    }

    // a sink that locks the store, as the values arrive on the flow of Listen
    private sealed class CountingSink(FieldValueStore store) : IValueSink
    {
        public static int Count(FieldValueStore store)
        {
            lock (store)
            {
                return store.Count;
            }
        }

        public void SetField(FieldContext context, LogicalField value)
        {
            lock (store)
            {
                store.SetField(context, value);
            }
        }

        public IValueSink EnterGroup(GroupContext context) => this;
        public IValueSink EnterRepeat(RepeatContext context, int count) => this;
        public void ExitRepeat(RepeatContext context, int count) { }
        public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
        public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
        public void ExitGroup(GroupContext context) { }
        public void ExitItem(RepeatContext context, int index, int count) { }
        public void ExitChoice(ChoiceContext context, int selectedIndex) { }
    }
}
