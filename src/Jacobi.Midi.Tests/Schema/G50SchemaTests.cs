using System.Buffers;
using System.Text;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Schema;

public class G50SchemaTests
{
    private const string MemoryPath = "Message.OneMemoryBulk.ChecksumScope.Content.Memory";

    private static readonly string[] MemoryParameters =
    [
        "PlayingStyle", "NoteOnLevel", "NoteOffLevel", "Velocity", "Chromatic", "Transpose", "PitchBendRange",
        "ProgramNumber", "ProgramBankMsb", "ProgramBankLsb", "Volume", "Pan",
        "AssignableControlNumber1", "AssignableControlValue1", "AssignableControlNumber2", "AssignableControlValue2",
        "Split", "SplitMemory2Number", "SplitMemory2Location", "PickingPositionControl",
        "PickingFrontPositionValue", "PickingRearPositionValue", "TouchControl", "TouchControlSensitivity",
        "SustainHoldPedal",
    ];

    private static readonly string[] SystemParameters =
    [
        "MidiCh", "Tuning", "E1StringSens", "B2StringSens", "G3StringSens", "D4StringSens", "A5StringSens",
        "E6StringSens", "G1DFunction", "Hold",
    ];

    private readonly ITestOutputHelper _output;

    public G50SchemaTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void G50_MessagePlan()
    {
        var serializer = CreateSerializer();

        var plan = serializer.GetPlan(new SchemaName("G50.Message"));
        Assert.NotNull(plan);
    }

    [Fact]
    public void G50_OneMemoryBulk_Deserialize()
    {
        var serializer = CreateSerializer();
        var values = MemoryValues(0);
        var message = BulkDump("OM", 0, Params(values));

        // the byte count (76) is a constant: a mismatch fails the read
        var sink = Deserialize(serializer, "Message.OneMemoryBulk", message);

        for (var i = 0; i < MemoryParameters.Length; i++)
        {
            Assert.Equal(values[i], sink.Single($"{MemoryPath}.{MemoryParameters[i]}"));
        }
    }

    [Fact]
    public void G50_OneMemoryBulk_Serialize()
    {
        var serializer = CreateSerializer();
        var values = MemoryValues(0);
        var source = new ValueSource();
        for (var i = 0; i < MemoryParameters.Length; i++)
        {
            source.Values[$"{MemoryPath}.{MemoryParameters[i]}"] = values[i];
        }

        var bytes = Serialize(serializer, "Message.OneMemoryBulk", source);

        Assert.Equal(BulkDump("OM", 0, Params(values)), bytes);
    }

    [Fact]
    public void G50_AllMemoryBulk_Memories_Deserialize()
    {
        var serializer = CreateSerializer();
        var data = Enumerable.Range(0, 8).SelectMany(memory => Params(MemoryValues(memory))).ToArray();
        var message = BulkDump("MA", 3, data);

        var sink = Deserialize(serializer, "Message.AllMemoryBulk", message);

        const string path = "Message.AllMemoryBulk.ChecksumScope.Content";
        Assert.Equal((ushort)426, sink.Single("Message.AllMemoryBulk.ByteCount"));
        Assert.Equal((byte)3, sink.Single($"{path}.Block.Number"));
        for (var memory = 0; memory < 8; memory++)
        {
            var values = MemoryValues(memory);
            for (var i = 0; i < MemoryParameters.Length; i++)
            {
                Assert.Equal(values[i], sink.All($"{path}.Data.Memories.Memory.{MemoryParameters[i]}")[memory]);
            }
        }
    }

    [Fact]
    public void G50_AllMemoryBulk_System_Deserialize()
    {
        var serializer = CreateSerializer();
        var values = SystemValues();
        var message = BulkDump("MA", 16, Params(values));

        var sink = Deserialize(serializer, "Message.AllMemoryBulk", message);

        const string path = "Message.AllMemoryBulk.ChecksumScope.Content";
        Assert.Equal((byte)16, sink.Single($"{path}.Block.Number"));
        for (var i = 0; i < SystemParameters.Length; i++)
        {
            Assert.Equal(values[i], sink.Single($"{path}.Data.System.{SystemParameters[i]}"));
        }
    }

    [Fact]
    public void G50_SystemBulk_RoundTrip()
    {
        var serializer = CreateSerializer();
        var values = SystemValues();
        var message = BulkDump("SY", 0, Params(values));
        const string path = "Message.SystemBulk.ChecksumScope.Content.System";

        var sink = Deserialize(serializer, "Message.SystemBulk", message);

        var source = new ValueSource();
        for (var i = 0; i < SystemParameters.Length; i++)
        {
            Assert.Equal(values[i], sink.Single($"{path}.{SystemParameters[i]}"));
            source.Values[$"{path}.{SystemParameters[i]}"] = values[i];
        }
        Assert.Equal(message, Serialize(serializer, "Message.SystemBulk", source));
    }

    [Fact]
    public void G50_BulkDump_ChecksumMismatch_Throws()
    {
        var serializer = CreateSerializer();
        var message = BulkDump("SY", 0, Params(SystemValues()));
        message[^2] ^= 0x01;

        Assert.ThrowsAny<Exception>(() => Deserialize(serializer, "Message.SystemBulk", message));
    }

    [Fact]
    public void G50_OneMemoryDumpRequest_Serialize()
    {
        var serializer = CreateSerializer();
        var source = new ValueSource();
        source.Values["Message.OneMemoryDumpRequest.Memory.Number"] = (byte)127;

        var bytes = Serialize(serializer, "Message.OneMemoryDumpRequest", source);

        Assert.Equal(DumpRequest("OM", 127), bytes);
    }

    [Theory]
    [InlineData("AllMemoryDumpRequest", "MA")]
    [InlineData("SystemDumpRequest", "SY")]
    public void G50_DumpRequest_Serialize(string request, string dumpType)
    {
        var serializer = CreateSerializer();

        var bytes = Serialize(serializer, $"Message.{request}", new ValueSource());

        Assert.Equal(DumpRequest(dumpType, 0), bytes);
    }

    [Fact]
    public void G50_ParameterChange_RoundTrip()
    {
        var serializer = CreateSerializer();
        // System (3), Tuning (1) - the MSB carries bit 7 of the data
        byte[] message = [0xF0, 0x43, 0x10, 0x56, 0x03, 0x01, 0x01, 0x05, 0xF7];

        var sink = Deserialize(serializer, "Message.ParameterChange", message);

        Assert.Equal((byte)3, sink.Single("Message.ParameterChange.ParameterGroup"));
        Assert.Equal((byte)1, sink.Single("Message.ParameterChange.ParameterNumber"));
        Assert.Equal((ushort)0x85, sink.Single("Message.ParameterChange.Data"));

        var source = new ValueSource();
        source.Values["Message.ParameterChange.ParameterGroup"] = (byte)3;
        source.Values["Message.ParameterChange.ParameterNumber"] = (byte)1;
        source.Values["Message.ParameterChange.Data"] = (ushort)0x85;
        Assert.Equal(message, Serialize(serializer, "Message.ParameterChange", source));
    }

    private static Serializer CreateSerializer()
        => new SerializerBuilder()
            .AddSchemas(CreateSchemas())
            .AddProcessors(CreateProcessors())
            .Build();

    private OrderedSink Deserialize(Serializer serializer, string message, byte[] bytes)
    {
        var plan = serializer.GetPlan(new SchemaName("G50.Message"));
        var sink = new OrderedSink();
        var read = serializer.Deserialize(plan.CreateRange(message), new ReadOnlySequence<byte>(bytes), sink);
        Assert.Equal(bytes.Length, read);

        foreach (var (path, value) in sink.Values)
        {
            _output.WriteLine($"{path} = {value}");
        }
        return sink;
    }

    private static byte[] Serialize(Serializer serializer, string message, IValueSource source)
    {
        var plan = serializer.GetPlan(new SchemaName("G50.Message"));
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(plan.CreateRange(message), source, writer);
        return writer.WrittenSpan.ToArray();
    }

    // Distinct values per memory, including values above 127 (bit 7 in the MSB byte).
    private static ushort[] MemoryValues(int memory)
        => [.. Enumerable.Range(0, MemoryParameters.Length).Select(i => (ushort)((memory * 31 + i * 11) % 130))];

    private static ushort[] SystemValues()
        => [17, 5, 0, 49, 25, 12, 33, 48, 2, 1];

    // Each parameter is 2 bytes: 0000000d 0ddddddd.
    private static byte[] Params(ushort[] values)
        => [.. values.SelectMany(value => new[] { (byte)(value >> 7), (byte)(value & 0x7F) })];

    // F0 43 00 7A bb bb "LM  0256xx" <15 x 00> number data cs F7
    // Byte count and checksum cover the header and the data.
    private static byte[] BulkDump(string dumpType, byte number, byte[] data)
    {
        byte[] content = [.. Header(dumpType, number), .. data];
        var sum = content.Sum(b => b);
        var checksum = (byte)((0x80 - (sum % 0x80)) % 0x80);
        return [0xF0, 0x43, 0x00, 0x7A, (byte)(content.Length >> 7), (byte)(content.Length & 0x7F), .. content, checksum, 0xF7];
    }

    // F0 43 20 7A "LM  0256xx" <15 x 00> number F7
    private static byte[] DumpRequest(string dumpType, byte number)
        => [0xF0, 0x43, 0x20, 0x7A, .. Header(dumpType, number), 0xF7];

    private static byte[] Header(string dumpType, byte number)
        => [.. Encoding.ASCII.GetBytes("LM  0256" + dumpType), .. new byte[15], number];
}

/// <summary>
/// A flat value sink that keeps every field value in read order, so the values of repeat items can be told apart.
/// </summary>
internal sealed class OrderedSink : IValueSink
{
    public List<(string Path, object? Value)> Values { get; } = [];

    public object? Single(string path)
        => Values.Single(value => String.Equals(value.Path, path, StringComparison.OrdinalIgnoreCase)).Value;

    public IReadOnlyList<object?> All(string path)
        => [.. Values.Where(value => String.Equals(value.Path, path, StringComparison.OrdinalIgnoreCase)).Select(value => value.Value)];

    public void SetField(FieldContext context, LogicalField value)
        => Values.Add((context.Path, value.Value));

    public IValueSink EnterGroup(GroupContext context) => this;
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }
}
