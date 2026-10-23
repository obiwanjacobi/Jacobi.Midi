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

    [Fact]
    public void Gp8_BulkDump_RoundTrip()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();
        var plan = serializer.GetPlan(new SchemaName("Roland.Message16"));

        // the bulk dump: 128 DT1 messages, one memory patch each
        var file = File.ReadAllBytes(TestHelpers.PathOf("Schema/gp8-sysex.syx"));
        Assert.Equal(128 * MessageSize, file.Length);

        for (var index = 0; index < 128; index++)
        {
            var original = file.AsSpan(index * MessageSize, MessageSize).ToArray();

            var store = new Gp8PatchStore();
            var read = serializer.Deserialize(plan.CreateRange("Message16.DT1"), new ReadOnlySequence<byte>(original), store);
            Assert.Equal(original.Length, read);
            Assert.Equal(index, store.PatchIndex);

            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(plan.CreateRange("Message16.DT1"), store, writer);

            Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(writer.WrittenSpan));
        }
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
