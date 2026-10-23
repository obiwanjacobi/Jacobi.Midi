using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

public class Gp8SchemaTests
{
    private const int MessageSize = 59;
    private const int MemoryStart = 0x2000;   // wire 40 00
    private const int Stride = 0x40;          // wire 00 40

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

            var store = new Gp8PatchStore();
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

        var store = new Gp8PatchStore();
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

    /// <summary>
    /// A flat value model for one GP-8 DT1 message: keeps the field values read by path and instance and provides
    /// them again for writing. The data open choice selects the memory patch of the GP-8 address map at the address read.
    /// </summary>
    private sealed class Gp8PatchStore : IOpenChoiceSink, IOpenChoiceSource
    {
        private readonly Dictionary<(string Path, InstancePath Instance), LogicalField> _values = [];

        /// <summary>The index of the memory patch, from the address of the message; -1 before it was read.</summary>
        public int PatchIndex { get; private set; } = -1;

        public void SetField(FieldContext context, LogicalField value)
        {
            _values[(context.Path, context.Instance)] = value;
            if (String.Equals(context.Path, "Message16.DT1.ChecksumScope.Address", StringComparison.OrdinalIgnoreCase))
            {
                PatchIndex = (Convert.ToInt32(value.Value) - MemoryStart) / Stride;
            }
        }

        public SourceResult GetField(FieldContext context)
            => _values.TryGetValue((context.Path, context.Instance), out var value)
                ? SourceResult.Provided(value)
                : SourceResult.NoValue();

        public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
            => new(this, new SchemaName("Gp8.AddressMap"), "AddressMap.Memory.Patch", new InstancePath([PatchIndex]));

        OpenChoiceSourceResult IOpenChoiceSource.EnterOpenChoice(OpenChoiceContext context)
            => new(this, new SchemaName("Gp8.AddressMap"), "AddressMap.Memory.Patch", new InstancePath([PatchIndex]));

        // the store is flat: every scope is the store itself
        public IValueSink EnterGroup(GroupContext context) => this;
        public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
        public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
        public void ExitGroup(GroupContext context) { }
        public void ExitItem(RepeatContext context, int index, int count) { }
        public void ExitChoice(ChoiceContext context, int selectedIndex) { }

        IValueSource IValueSource.EnterGroup(GroupContext context) => this;
        public int GetCount(RepeatContext context) => -1;
        public IValueSource EnterItem(RepeatContext context, int index) => this;
        public int GetSelectedIndex(ChoiceContext context)
            => throw new NotSupportedException($"'{context.Path}': choices are not supported.");
        IValueSource IValueSource.EnterChoice(ChoiceContext context) => this;
    }
}
