using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

public class Gp8SchemaTests
{
    private const int MessageSize = 59;

    [Fact]
    public void Gp8_ValidateSchema()
    {
        var schemaSet = TestHelpers.CreateSchemas();
        var processors = TestHelpers.CreateProcessors();

        var serializer = new SerializerBuilder()
            .AddSchemas(schemaSet)
            .AddProcessors(processors)
            .Build();

        var plan = serializer.GetPlan(new SchemaName("Gp8.AddressMap"));
        Assert.NotNull(plan);
    }

    // the bulk dump: 128 DT1 messages, one memory patch each
    private const string BulkDumpFile = "Schema/gp8-sysex.syx";
    // patch 0 "Dig Chorus": all 0/100 mapped fields hold a valid value (UpDown = 100, Turbo = 100)
    private const int ValidPatchIndex = 0;
    // offset of the patch data in a DT1 message: F0 41 dev 13 12 aa aa
    private const int DataOffset = 7;

    [Fact]
    public void Gp8_BulkDump_ReadAll()
    {
        var serializer = CreateSerializer();
        var plan = serializer.GetPlan(new SchemaName("Roland.Message16"));

        var file = File.ReadAllBytes(TestHelpers.PathOf(BulkDumpFile));
        Assert.Equal(128 * MessageSize, file.Length);

        for (var index = 0; index < 128; index++)
        {
            var message = file.AsSpan(index * MessageSize, MessageSize).ToArray();

            var store = new Gp8PatchStore(AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap")));
            var read = serializer.Deserialize(plan.CreateRange("Message16.DT1"), new ReadOnlySequence<byte>(message), store);
            Assert.Equal(message.Length, read);
            Assert.Equal(index, store.PatchIndex);
        }
    }

    [Fact]
    public void Gp8_Patch_RoundTrip()
    {
        var serializer = CreateSerializer();
        var original = ReadPatchMessage(ValidPatchIndex);

        var written = RoundTrip(serializer, original);

        Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(written));
    }

    /// <summary>
    /// The 0/100 mapped fields (upDown, switch100) only know the values 0 and 100.
    /// Any other value is read as the map default and written back as 0.
    /// </summary>
    [Theory]
    [InlineData(0x05, 63)]  // DynamicFilter.UpDown
    [InlineData(0x05, 2)]
    [InlineData(0x0A, 78)]  // TurboOverdrive.Turbo
    [InlineData(0x0A, 32)]
    [InlineData(0x1F, 50)]  // ExtControlOut1
    [InlineData(0x20, 99)]  // ExtControlOut2
    public void Gp8_Patch_IllegalMappedValue_Corrected(int parameter, byte illegalValue)
    {
        var serializer = CreateSerializer();
        var valid = ReadPatchMessage(ValidPatchIndex);

        var illegal = WithParameter(valid, parameter, illegalValue);
        var expected = WithParameter(valid, parameter, 0);

        var written = RoundTrip(serializer, illegal);

        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(written));
    }

    private static Serializer CreateSerializer()
        => new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();

    private static byte[] ReadPatchMessage(int index)
    {
        var file = File.ReadAllBytes(TestHelpers.PathOf(BulkDumpFile));
        return file.AsSpan(index * MessageSize, MessageSize).ToArray();
    }

    private static byte[] RoundTrip(Serializer serializer, byte[] message)
    {
        var plan = serializer.GetPlan(new SchemaName("Roland.Message16"));

        var store = new Gp8PatchStore(AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap")));
        var read = serializer.Deserialize(plan.CreateRange("Message16.DT1"), new ReadOnlySequence<byte>(message), store);
        Assert.Equal(message.Length, read);

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(plan.CreateRange("Message16.DT1"), store, writer);
        return writer.WrittenSpan.ToArray();
    }

    // Returns a copy of the DT1 message with the patch parameter set to the value and the Roland checksum
    // (over address and data) recalculated.
    private static byte[] WithParameter(byte[] message, int parameter, byte value)
    {
        var copy = message.ToArray();
        copy[DataOffset + parameter] = value;

        var checksumIndex = copy.Length - 2;
        var sum = 0;
        for (var i = DataOffset - 2; i < checksumIndex; i++)
            sum += copy[i];
        copy[checksumIndex] = (byte)((0x80 - (sum % 0x80)) % 0x80);
        return copy;
    }
}
