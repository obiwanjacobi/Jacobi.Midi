using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Schema;

/// <summary>
/// The values the value model sees for fields of the Roland data types (their value type):
/// 'roland.level' (0-100) is a byte, 'roland.switch' is a bool. Uses the GP-8 patch that uses both.
/// </summary>
public class RolandValueModelTests
{
    private const string BulkDumpFile = "Schema/gp8-sysex.syx";
    private const int MessageSize = 59;
    // patch 0 "Dig Chorus"
    private const int PatchIndex = 0;

    // roland.level fields of patch 0 with their value
    private const string LevelZero = "DynamicFilter.Sens";
    private const string LevelFull = "Equalizer.OutLevel";
    // roland.switch fields of patch 0 with their value
    private const string SwitchOff = "EffectSwiches1.PhaserOn";
    private const string SwitchOn = "EffectSwiches1.ChorusOn";

    [Theory]
    [InlineData(LevelZero, (byte)0)]
    [InlineData(LevelFull, (byte)100)]
    public void Read_Level_IsByte(string path, byte expected)
    {
        var (_, store) = Read(CreateSerializer(), ReadPatchMessage());

        var value = store.GetPatchField(path);

        Assert.Equal(typeof(byte), value.LogicalType);
        Assert.Equal(expected, Assert.IsType<byte>(value.Value));
    }

    [Theory]
    [InlineData(SwitchOff, false)]
    [InlineData(SwitchOn, true)]
    public void Read_Switch_IsBool(string path, bool expected)
    {
        var (_, store) = Read(CreateSerializer(), ReadPatchMessage());

        var value = store.GetPatchField(path);

        Assert.Equal(typeof(bool), value.LogicalType);
        Assert.Equal(expected, Assert.IsType<bool>(value.Value));
    }

    /// <summary>A value of another type is converted to the value type: the same value writes the same bytes.</summary>
    [Theory]
    [MemberData(nameof(ConvertibleValues))]
    public void Write_ConvertibleValue_IsConvertedToValueType(string path, Type type, object value)
    {
        var serializer = CreateSerializer();
        var original = ReadPatchMessage();
        var (range, store) = Read(serializer, original);

        store.SetPatchField(path, new LogicalField(path, type, value));
        var written = Write(serializer, range, store);

        Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(written));
    }

    public static TheoryData<string, Type, object> ConvertibleValues => new()
    {
        { LevelFull, typeof(int), 100 },
        { LevelFull, typeof(string), "100" },
        { LevelZero, typeof(long), 0L },
        { SwitchOn, typeof(int), 1 },
        { SwitchOn, typeof(string), "True" },
        { SwitchOff, typeof(string), "false" },
    };

    [Theory]
    [InlineData(101)]
    [InlineData(127)]
    public void Write_LevelOutOfRange_Fails(int level)
    {
        var serializer = CreateSerializer();
        var (range, store) = Read(serializer, ReadPatchMessage());

        store.SetPatchField(LevelFull, new LogicalField(LevelFull, typeof(int), level));

        var exception = Assert.ThrowsAny<Exception>(() => Write(serializer, range, store));
        Assert.Contains("out of the range", exception.Message, StringComparison.Ordinal);
    }

    private static Serializer CreateSerializer()
        => new SerializerBuilder()
            .AddSchemas(CreateSchemas())
            .AddProcessors(CreateProcessors())
            .Build();

    private static byte[] ReadPatchMessage()
        => File.ReadAllBytes(PathOf(BulkDumpFile)).AsSpan(PatchIndex * MessageSize, MessageSize).ToArray();

    private static (PlanRange Range, Gp8PatchStore Store) Read(Serializer serializer, byte[] message)
    {
        var range = serializer.GetPlan(new SchemaName("Roland.Message16")).CreateRange("Message16.DT1");
        var store = new Gp8PatchStore(AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap")));
        var read = serializer.Deserialize(range, new ReadOnlySequence<byte>(message), store);
        Assert.Equal(message.Length, read);
        Assert.Equal(PatchIndex, store.PatchIndex);
        return (range, store);
    }

    private static byte[] Write(Serializer serializer, PlanRange range, Gp8PatchStore store)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(range, store, writer);
        return writer.WrittenSpan.ToArray();
    }
}
