using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

/// <summary>
/// Tests the Boss GX-700 schema (Gx700.xml) with the serializer directly: the plan builds and the DT1 messages
/// of the patch and system dumps read and write back to the original bytes.
/// </summary>
public class Gx700SchemaTests
{
    // the patch dump: 14 DT1 messages, one section each
    private const string PatchDumpFile = "Schema/gx700-sysex-patch1.syx";
    // the system dump: 3 DT1 messages, one section each
    private const string SystemDumpFile = "Schema/gx700-sysex-system.syx";

    // the areas of the address map (aa in the address aa pp ss 00)
    private const int PatchArea = 0x00;
    private const int SystemArea = 0x02;

    // the sections of the patch nodeDef, in address order (ss in the address aa pp ss 00)
    private static readonly string[] PatchSections =
    [
        "Common", "Compressor", "Wah", "OverdriveDistortion", "Preamp", "Loop", "Equalizer",
        "SpeakerSimulator", "NoiseSuppressor", "Modulation", "Delay", "Chorus", "TremoloPan", "Reverb"
    ];

    // the sections of the system nodeDef, in address order
    private static readonly string[] SystemSections = ["Common", "ProgramChangeMap1", "ProgramChangeMap2"];

    [Fact]
    public void Gx700_ValidateSchema()
    {
        var serializer = CreateSerializer();

        var plan = serializer.GetPlan(new SchemaName("Gx700.AddressMap"));
        Assert.NotNull(plan);
    }

    [Fact]
    public void Gx700_Patch1_RoundTrip()
    {
        var serializer = CreateSerializer();
        var messages = DumpMessages(PatchDumpFile);
        Assert.Equal(PatchSections.Length, messages.Count);

        for (var section = 0; section < messages.Count; section++)
        {
            var original = messages[section];

            var store = new Gx700SectionStore(AddressMap.Create(serializer, new SchemaName("Gx700.AddressMap")));
            var written = RoundTrip(serializer, original, store);

            Assert.Equal(PatchArea, store.AreaIndex);
            Assert.Equal(0, store.PatchIndex);
            Assert.Equal(section, store.SectionIndex);
            Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(written));
        }
    }

    [Fact]
    public void Gx700_System_RoundTrip()
    {
        var serializer = CreateSerializer();
        var messages = DumpMessages(SystemDumpFile);
        Assert.Equal(SystemSections.Length, messages.Count);

        for (var section = 0; section < messages.Count; section++)
        {
            var original = messages[section];

            var store = new Gx700SectionStore(AddressMap.Create(serializer, new SchemaName("Gx700.AddressMap")));
            var written = RoundTrip(serializer, original, store);

            Assert.Equal(SystemArea, store.AreaIndex);
            Assert.Equal(section, store.SectionIndex);
            Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(written));
        }
    }

    private static Serializer CreateSerializer()
        => new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();

    // the dump files hold one DT1 per section
    private static List<byte[]> DumpMessages(string fileName)
    {
        var file = File.ReadAllBytes(TestHelpers.PathOf(fileName));
        var messages = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < file.Length; i++)
        {
            if (file[i] == 0xF7)
            {
                messages.Add(file[start..(i + 1)]);
                start = i + 1;
            }
        }
        return messages;
    }

    private static byte[] RoundTrip(Serializer serializer, byte[] message, Gx700SectionStore store)
    {
        var plan = serializer.GetPlan(new SchemaName("Roland.Message32"));

        var read = serializer.Deserialize(plan.CreateRange("Message32.DT1"), new ReadOnlySequence<byte>(message), store);
        Assert.Equal(message.Length, read);

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(plan.CreateRange("Message32.DT1"), store, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// A flat value model for one GX-700 DT1 message: keeps the field values read by path and instance and provides
    /// them again for writing. The data open choice selects the patch or system section of the GX-700 address map
    /// at the address read; the map ends its repeats after that section.
    /// </summary>
    private sealed class Gx700SectionStore(AddressMap map) : IOpenChoiceSink, IOpenChoiceSource
    {
        private readonly Dictionary<(string Path, InstancePath Instance), LogicalField> _values = [];

        /// <summary>The area of the address map, from the address of the message (aa); -1 before it was read.</summary>
        public int AreaIndex { get; private set; } = -1;

        /// <summary>The index of the memory patch, from the address of the message (pp); -1 before it was read.</summary>
        public int PatchIndex { get; private set; } = -1;

        /// <summary>The index of the patch section, from the address of the message (ss); -1 before it was read.</summary>
        public int SectionIndex { get; private set; } = -1;

        public void SetField(FieldContext context, LogicalField value)
        {
            _values[(context.Path, context.Instance)] = value;
            if (String.Equals(context.Path, "Message32.DT1.ChecksumScope.Address", StringComparison.OrdinalIgnoreCase))
            {
                // address aa pp ss 00, 7 bits per byte
                var address = Convert.ToInt32(value.Value);
                AreaIndex = (address >> 21) & 0x7F;
                PatchIndex = (address >> 14) & 0x7F;
                SectionIndex = (address >> 7) & 0x7F;
            }
        }

        public SourceResult GetField(FieldContext context)
            => _values.TryGetValue((context.Path, context.Instance), out var value)
                ? SourceResult.Provided(value)
                : SourceResult.NoValue();

        public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
            => new(map.WrapBody((IValueSink)this, SectionPath, SectionInstance), map.Root, SectionPath, SectionInstance);

        OpenChoiceSourceResult IOpenChoiceSource.EnterOpenChoice(OpenChoiceContext context)
            => new(map.WrapBody((IValueSource)this, SectionPath, SectionInstance), map.Root, SectionPath, SectionInstance);

        private string SectionPath => AreaIndex switch
        {
            PatchArea => "AddressMap.Memory.Patch." + PatchSections[SectionIndex],
            SystemArea => "AddressMap.System." + SystemSections[SectionIndex],
            _ => throw new NotSupportedException($"Area 0x{AreaIndex:X2} is not in the GX-700 address map.")
        };

        // a patch section is in the Memory repeat; the system is not in a repeat
        private InstancePath SectionInstance
            => AreaIndex == PatchArea ? new InstancePath([PatchIndex]) : new InstancePath([]);

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
