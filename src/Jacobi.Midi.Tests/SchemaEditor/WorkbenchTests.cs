using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.SchemaEditor.Workbench;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class WorkbenchTests
{
    // a Roland style DT1 message: header, address, 4 data bytes, checksum
    private static byte[] Message(byte address, byte d0, byte d1, byte d2, byte d3)
    {
        byte[] body = [0x00, 0x00, address, 0x00, d0, d1, d2, d3];
        var checksum = (byte)((128 - body.Sum(value => value) % 128) % 128);
        return [0xF0, 0x41, 0x00, 0x79, 0x12, .. body, checksum, 0xF7];
    }

    private static Capture Dump(string label, params byte[][] messages) => new(label, messages);

    // ---- hex text and splitting ----

    [Theory]
    [InlineData("F0 41 F7")]
    [InlineData("f0,41,f7")]
    [InlineData("0xF0 0x41 0xF7")]
    [InlineData("F041F7")]
    [InlineData("F0\r\n41\tF7")]
    public void HexText_ParsesSeparatorsAndPrefixes(string text)
    {
        Assert.True(HexText.TryParse(text, out var bytes, out var error));
        Assert.Null(error);
        Assert.Equal(new byte[] { 0xF0, 0x41, 0xF7 }, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F0 4G")]
    [InlineData("F04")]
    public void HexText_RejectsInvalidText(string text)
    {
        Assert.False(HexText.TryParse(text, out var bytes, out var error));
        Assert.Empty(bytes);
        Assert.NotNull(error);
    }

    [Fact]
    public void HexText_FormatsSpaceSeparatedPairs()
    {
        Assert.Equal("F0 0A F7", HexText.Format([0xF0, 0x0A, 0xF7]));
        Assert.Equal(String.Empty, HexText.Format([]));
    }

    [Fact]
    public void SysExSplitter_SplitsMessagesAndSkipsBytesBetweenThem()
    {
        var messages = SysExSplitter.Split([0xF0, 0x01, 0xF7, 0xFE, 0xF0, 0x02, 0x03, 0xF7, 0xF0, 0x04]);

        Assert.Equal(3, messages.Count);
        Assert.Equal(new byte[] { 0xF0, 0x01, 0xF7 }, messages[0]);
        Assert.Equal(new byte[] { 0xF0, 0x02, 0x03, 0xF7 }, messages[1]);
        // unterminated: kept as it is
        Assert.Equal(new byte[] { 0xF0, 0x04 }, messages[2]);
    }

    [Fact]
    public void SysExSplitter_KeepsDataWithoutF0AsOneMessage()
    {
        var messages = SysExSplitter.Split([0x01, 0x02, 0xF7]);

        Assert.Equal(new byte[] { 0x01, 0x02, 0xF7 }, Assert.Single(messages));
    }

    // ---- comparison ----

    [Fact]
    public void Comparison_MarksTheDifferingBytesAndTheirChangedBits()
    {
        var baseline = Dump("base", Message(0, 0x00, 0x10, 0x20, 0x30), Message(1, 0, 0, 0, 0));
        var variant = Dump("reverb", Message(0, 0x00, 0x10, 0x28, 0x30), Message(1, 0, 0, 0, 0));

        var comparison = CaptureComparison.Compare(baseline, [variant], focus: null);

        Assert.Equal(2, comparison.Slots.Count);
        var slot = comparison.Slots[0];
        Assert.True(slot.HasDifference);
        Assert.False(comparison.Slots[1].HasDifference);
        // the data byte at offset 11 and the checksum at offset 13
        Assert.Equal([11, 13], Enumerable.Range(0, slot.States.Length).Where(offset => slot.States[offset].IsDifferent));
        Assert.Equal(0x08, slot.States[11].ChangedBits);
        Assert.Equal(1.0, slot.States[11].Tint);
        Assert.Equal(0.0, slot.States[10].Tint);
        Assert.Equal(2, comparison.GetDifferenceCount(variant));
        Assert.Empty(comparison.Remarks);
    }

    [Fact]
    public void Comparison_TintsByTheShareOfVariantsOrOnlyTheFocusedVariant()
    {
        var baseline = Dump("base", [0xF0, 0x01, 0x02, 0xF7]);
        var first = Dump("first", [0xF0, 0x05, 0x02, 0xF7]);
        var second = Dump("second", [0xF0, 0x01, 0x06, 0xF7]);

        var all = CaptureComparison.Compare(baseline, [first, second], focus: null);
        Assert.Equal(0.5, all.Slots[0].States[1].Tint);
        Assert.Equal(0.5, all.Slots[0].States[2].Tint);

        var focused = CaptureComparison.Compare(baseline, [first, second], focus: second);
        Assert.Equal(0.0, focused.Slots[0].States[1].Tint);
        Assert.Equal(1.0, focused.Slots[0].States[2].Tint);
        // the count still includes all variants; the changed bits only the focused one
        Assert.Equal(1, focused.Slots[0].States[1].DifferenceCount);
        Assert.Equal(0, focused.Slots[0].States[1].ChangedBits);
        Assert.Equal(0x04, focused.Slots[0].States[2].ChangedBits);
    }

    [Fact]
    public void Comparison_MarksTheChecksumAsDerivedWhenDifferentParametersChanged()
    {
        var baseline = Dump("base", Message(0, 0x00, 0x10, 0x20, 0x30));
        var reverb = Dump("reverb", Message(0, 0x00, 0x10, 0x28, 0x30));
        var gain = Dump("gain", Message(0, 0x03, 0x10, 0x20, 0x30));

        var states = CaptureComparison.Compare(baseline, [reverb, gain], focus: null).Slots[0].States;

        Assert.True(states[13].IsDerived);
        Assert.False(states[11].IsDerived);
        Assert.False(states[9].IsDerived);
    }

    [Fact]
    public void Comparison_DoesNotMarkDerivedBytesWhenTheSameParameterChanged()
    {
        var baseline = Dump("base", Message(0, 0x00, 0x10, 0x20, 0x30));
        var reverb40 = Dump("reverb 40", Message(0, 0x00, 0x10, 0x28, 0x30));
        var reverb80 = Dump("reverb 80", Message(0, 0x00, 0x10, 0x50, 0x30));

        var states = CaptureComparison.Compare(baseline, [reverb40, reverb80], focus: null).Slots[0].States;

        Assert.DoesNotContain(states, state => state.IsDerived);
    }

    [Fact]
    public void Comparison_ReportsLengthChangesMissingAndExtraMessages()
    {
        var baseline = Dump("base", [0xF0, 0x01, 0x02, 0xF7], [0xF0, 0x03, 0xF7]);
        var longer = Dump("longer", [0xF0, 0x01, 0x02, 0x00, 0xF7], [0xF0, 0x03, 0xF7], [0xF0, 0x09, 0xF7]);
        var shorter = Dump("shorter", [0xF0, 0x01, 0x02, 0xF7]);

        var comparison = CaptureComparison.Compare(baseline, [longer, shorter], focus: null);

        Assert.Equal("length 4 → 5", comparison.Slots[0].LengthText);
        Assert.Equal("missing in 1", comparison.Slots[1].LengthText);
        Assert.True(comparison.Slots[1].HasDifference);
        Assert.Contains(comparison.Remarks, remark => remark.StartsWith("longer: 3 messages", StringComparison.Ordinal));
        Assert.Contains(comparison.Remarks, remark => remark.StartsWith("shorter: 1 messages", StringComparison.Ordinal));
        // 'longer': F7 → 00 at offset 3, one extra byte and the 3 bytes of the extra message
        Assert.Equal(5, comparison.GetDifferenceCount(longer));
        // 'shorter': the whole second message is missing
        Assert.Equal(3, comparison.GetDifferenceCount(shorter));
    }

    // ---- interpretations ----

    [Fact]
    public void Interpretations_OfOneByte()
    {
        var rows = ByteInterpretations.Interpret([[0x50], [0x28], null]);

        Assert.Equal(["80", "40", ByteInterpretations.NoValue], Row(rows, "uint8"));
        Assert.Equal(["-48", "40", ByteInterpretations.NoValue], Row(rows, "int7 (7-bit signed)"));
        Assert.Equal(["1010000", "0101000", ByteInterpretations.NoValue], Row(rows, "bits"));
        Assert.Equal("hex", rows[^1].Name);
    }

    [Fact]
    public void Interpretations_OfTwoBytes()
    {
        var rows = ByteInterpretations.Interpret([[0x01, 0x00], [0x0A, 0x05]]);

        Assert.Equal(["128", "1285"], Row(rows, "7-bit, MSB first"));
        Assert.Equal(["1", "650"], Row(rows, "7-bit, LSB first"));
        Assert.Equal(["16", "165"], Row(rows, "nibbles, high first"));
        Assert.Equal(["256", "2565"], Row(rows, "uint16 big endian"));
        Assert.DoesNotContain(rows, row => row.Name == "uint8");
    }

    [Fact]
    public void Interpretations_PutAsciiFirstWhenPrintable()
    {
        var rows = ByteInterpretations.Interpret([[0x56, 0x49, 0x4E, 0x54, 0x41, 0x47, 0x45]]);

        Assert.Equal("ascii", rows[0].Name);
        Assert.Equal("VINTAGE", rows[0].Values[0]);
        Assert.Equal("hex", rows[^1].Name);
    }

    private static IReadOnlyList<string> Row(IReadOnlyList<Interpretation> rows, string name)
        => Assert.Single(rows, row => row.Name == name).Values;

    // ---- find a value ----

    [Theory]
    [InlineData("70", 70)]
    [InlineData("-12", -12)]
    [InlineData("0x46", 70)]
    [InlineData("$46", 70)]
    [InlineData("46h", 70)]
    [InlineData("300", 300)]
    public void ValueFinder_ParsesDecimalAndHexNumbers(string text, long expected)
    {
        Assert.True(ValueFinder.TryParse(text, out var query, out _));
        Assert.Equal(expected, query!.Value);
        Assert.Null(query.Sequence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("seventy")]
    [InlineData("70000")]
    [InlineData("46 0G")]
    public void ValueFinder_RejectsInvalidText(string text)
    {
        Assert.False(ValueFinder.TryParse(text, out var query, out var error));
        Assert.Null(query);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValueFinder_FindsAByteSequence()
    {
        Assert.True(ValueFinder.TryParse("46 00", out var query, out _));
        var capture = Dump("patch", [0xF0, 0x46, 0x00, 0x46, 0x01, 0xF7]);

        var match = Assert.Single(ValueFinder.Find(capture, null, query!));

        Assert.Equal(new ByteRange(0, 1, 2), match.Range);
    }

    [Fact]
    public void ValueFinder_FindsOneAndTwoByteValues_ChangedFirst()
    {
        var baseline = Dump("base", [0xF0, 0x46, 0x00, 0x10, 0x04, 0x06, 0xF7], [0xF0, 0x02, 0x2C, 0xF7]);
        var variant = Dump("sustain 70", [0xF0, 0x46, 0x00, 0x46, 0x04, 0x06, 0xF7], [0xF0, 0x02, 0x2C, 0xF7]);

        Assert.True(ValueFinder.TryParse("70", out var seventy, out _));
        var matches = ValueFinder.Find(variant, baseline, seventy!);
        // the byte that was changed first, then the others in message order
        Assert.Equal(new ValueMatch(new ByteRange(0, 3, 1), "uint8", true), matches[0]);
        Assert.Equal(new ValueMatch(new ByteRange(0, 1, 1), "uint8", false), matches[1]);
        Assert.Contains(new ValueMatch(new ByteRange(0, 4, 2), "nibbles, high first", false), matches);
        Assert.Equal(3, matches.Count);

        // 300 = 0x12C: 7-bit packed 02 2C
        Assert.True(ValueFinder.TryParse("300", out var large, out _));
        Assert.Equal(new ValueMatch(new ByteRange(1, 1, 2), "7-bit, MSB first", false), Assert.Single(ValueFinder.Find(variant, baseline, large!)));

        // -12 = 0x74 as a 7-bit signed byte
        Assert.True(ValueFinder.TryParse("-12", out var negative, out _));
        Assert.Empty(ValueFinder.Find(variant, baseline, negative!));
        Assert.Equal("int7 (7-bit signed)", Assert.Single(ValueFinder.Find(Dump("x", [0xF0, 0x74, 0xF7]), null, negative!)).Interpretation);
    }

    [Fact]
    public void Find_SearchesTheFocusedVariantAndSelectsTheMatches()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", Message(0, 0x00, 0x10, 0x20, 0x30)));
        var variant = Dump("again", Message(0, 0x00, 0x10, 0x46, 0x30));
        workbench.AddCapture(variant);

        workbench.FindText = "70";
        workbench.FindCommand.Execute(null);

        Assert.Equal(new ByteRange(0, 11, 1), workbench.Selection);
        Assert.True(workbench.Matches[0].IsChanged);
        Assert.Equal(workbench.Matches.Select(match => match.Range), workbench.MatchRanges);

        // the baseline has no 70
        workbench.ClearFocusCommand.Execute(null);
        Assert.Empty(workbench.Matches);
        Assert.Contains("not found in patch1", workbench.FindStatus);

        workbench.FindText = "seventy";
        workbench.FindCommand.Execute(null);
        Assert.Empty(workbench.Matches);
        Assert.Contains("is not a value", workbench.FindStatus);
    }

    [Fact]
    public void SlotNote_IsKeptPerSlotAndSaved()
    {
        var storage = new FakeStorage();
        var workbench = CreateWorkbench(storage);
        workbench.AddCapture(Dump("patch1", Message(0, 1, 2, 3, 4), Message(1, 1, 2, 3, 4)));
        Assert.False(workbench.HasNoteSlot);

        workbench.SelectedSlot = workbench.Slots[1];
        Assert.Equal("SLOT 2 NOTE", workbench.SlotNoteCaption);
        workbench.SlotNoteText = "probably the compressor";

        Assert.Equal("probably the compressor", workbench.Slots[1].Note);
        Assert.True(workbench.Slots[1].HasNote);
        Assert.Equal("probably the compressor", workbench.SlotNotes[1]);
        workbench.SelectedSlot = workbench.Slots[0];
        Assert.Equal(String.Empty, workbench.SlotNoteText);

        var read = WorkbenchSessionFile.Deserialize(WorkbenchSessionFile.Serialize(workbench.ToSession()));
        Assert.Equal("probably the compressor", read.SlotNotes[1]);

        workbench.SelectedSlot = workbench.Slots[1];
        workbench.SlotNoteText = " ";
        Assert.False(workbench.Slots[1].HasNote);
        Assert.Empty(workbench.SlotNotes);
    }

    // ---- session file ----

    [Fact]
    public void SessionFile_RoundTrips()
    {
        var baseline = Dump("patch1", [0xF0, 0x41, 0xF7], [0xF0, 0x42, 0xF7]);
        var variant = new Capture("Received 12:00:00", [new byte[] { 0xF0, 0x43, 0xF7 }]) { ChangeNote = "reverb 40", IsHidden = true };
        var session = new WorkbenchSession("GX-700", [variant, baseline], 1,
            [new Annotation(new ByteRange(1, 1, 1), "tone A/B?")]);

        var read = WorkbenchSessionFile.Deserialize(WorkbenchSessionFile.Serialize(session));

        Assert.Equal("GX-700", read.Title);
        Assert.Equal(1, read.Baseline);
        Assert.Equal(2, read.Captures.Count);
        Assert.Equal("reverb 40", read.Captures[0].ChangeNote);
        Assert.True(read.Captures[0].IsHidden);
        Assert.Equal("patch1", read.Captures[1].Label);
        Assert.Equal(new byte[] { 0xF0, 0x42, 0xF7 }, read.Captures[1].Messages[1]);
        var annotation = Assert.Single(read.Annotations);
        Assert.Equal(new ByteRange(1, 1, 1), annotation.Range);
        Assert.Equal("tone A/B?", annotation.Text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"version\": 99 }")]
    [InlineData("{ \"captures\": [ { \"messages\": [ \"F0 XX\" ] } ] }")]
    public void SessionFile_RejectsInvalidFiles(string json)
    {
        Assert.Throws<FormatException>(() => WorkbenchSessionFile.Deserialize(json));
    }

    // ---- view model ----

    private sealed class FakeStorage
    {
        public (string Name, byte[] Bytes)? SysEx { get; set; }
        public (SessionFile File, string Text)? Session { get; set; }
        public string? SavedText { get; private set; }

        public WorkbenchStorage Create() => new()
        {
            OpenSysEx = () => Task.FromResult(SysEx),
            OpenSession = () => Task.FromResult(Session),
            SaveSession = (file, name, text) =>
            {
                SavedText = text;
                return Task.FromResult<SessionFile?>(file ?? new SessionFile(name, null));
            }
        };
    }

    // the schema documents of the editor: the open documents and the embedded schemas (Midi) of the processor assemblies
    private sealed class FakeSchemaHost
    {
        private static readonly AssemblyWorkspace Assemblies = WorkbenchSchemaTests.CreateProcessors();

        public List<SchemaWorkspaceDocument> Documents { get; } = [];

        public WorkbenchSchemaHost Create() => new()
        {
            FindDocument = name => Documents.FirstOrDefault(document => String.Equals(document.Schema.Name, name, StringComparison.OrdinalIgnoreCase)),
            CreateDocument = name =>
            {
                var document = new SchemaWorkspaceDocument(WorkbenchSchema.CreateEmpty(name), SchemaFormat.Xml);
                Documents.Add(document);
                return document;
            },
            Compile = document => SchemaInstantiator.Create(document, Documents.Select(open => open.Schema)
                .Where(schema => !String.Equals(schema.Name, document.Name, StringComparison.OrdinalIgnoreCase))
                .Concat(Assemblies.Assemblies.SelectMany(assembly => assembly.Schemas).Select(schema => schema.Document.Schema))),
            Processors = Assemblies.Processors
        };
    }

    private static WorkbenchViewModel CreateWorkbench(FakeStorage? storage = null, FakeSchemaHost? schemas = null)
    {
        var workbench = new WorkbenchViewModel((storage ?? new FakeStorage()).Create(), new MidiBarViewModel(null), (schemas ?? new FakeSchemaHost()).Create());
        workbench.Load(new WorkbenchSession("GX-700", [], -1, []), file: null);
        return workbench;
    }

    [Fact]
    public void Field_DefinedForTheSelection_CreatesTheSchemaAndShowsItsValues()
    {
        var schemas = new FakeSchemaHost();
        var workbench = CreateWorkbench(schemas: schemas);
        workbench.AddCapture(Dump("patch1", Message(0, 0x00, 0x10, 0x20, 0x30)));
        var variant = Dump("again", Message(0, 0x00, 0x10, 0x28, 0x30));
        workbench.AddCapture(variant);
        variant.ChangeNote = "reverb 40";
        Assert.Contains("not open", workbench.SchemaText);

        // the F0 cannot be a field
        workbench.Selection = new ByteRange(0, 0, 1);
        Assert.Equal(FieldMode.None, workbench.Field.Mode);

        workbench.Selection = new ByteRange(0, 11, 1);
        var field = workbench.Field;
        Assert.Equal(FieldMode.Define, field.Mode);
        Assert.Equal("sys.uint8", field.DataType);
        // the preview: the field is read from every capture before it is defined
        Assert.Equal(["patch1", "reverb 40"], field.Values.Select(value => value.Column));
        Assert.Equal(["32", "40"], field.Values.Select(value => value.Value));
        Assert.Empty(schemas.Documents);

        field.Name = "Reverb";
        field.DefineCommand.Execute(null);

        var document = Assert.Single(schemas.Documents);
        Assert.Equal("GX-700", document.Schema.Name);
        var layout = Assert.IsType<MessageLayout>(workbench.Layouts[0]);
        Assert.Equal("Message1", layout.Root.Name);
        Assert.Equal(["Wip_1", "Reverb", "Wip_12"], layout.Spans.Select(span => span.Field.Name));
        Assert.Equal(FieldMode.Edit, field.Mode);
        Assert.Equal("Reverb", field.Name);
        Assert.Equal(["32", "40"], field.Values.Select(value => value.Value));
        Assert.Contains("Field Reverb", workbench.DescribeByte(0, 11));

        field.RemoveCommand.Execute(null);
        Assert.Equal(FieldMode.Define, field.Mode);
        Assert.Equal(["Wip_1"], workbench.Layouts[0]!.Spans.Select(span => span.Field.Name));
    }

    [Fact]
    public void Field_With7BitProcessor_IncludesTheMidiSchema()
    {
        var schemas = new FakeSchemaHost();
        var workbench = CreateWorkbench(schemas: schemas);
        workbench.AddCapture(Dump("patch1", Message(0, 0x01, 0x00, 0x20, 0x30)));

        workbench.Selection = new ByteRange(0, 9, 2);
        var field = workbench.Field;
        Assert.Equal("sys.uint16", field.DataType);
        Assert.Equal("Midi.sysexbyte", field.Processor);
        Assert.Equal("128", Assert.Single(field.Values).Value);

        field.Name = "Size";
        field.DefineCommand.Execute(null);

        var document = Assert.Single(schemas.Documents);
        Assert.Equal("Midi", Assert.Single(document.Schema.Includes).Schema);
        Assert.Equal("128", Assert.Single(field.Values).Value);
    }

    [Fact]
    public void Field_ApplyChangesTheDataType_InterpretationSetsIt()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", Message(0, 0x00, 0x10, 0x50, 0x30)));
        workbench.Selection = new ByteRange(0, 11, 1);
        workbench.Field.Name = "Pan";
        workbench.Field.DefineCommand.Execute(null);
        Assert.Equal("80", Assert.Single(workbench.Field.Values).Value);

        workbench.Field.UseInterpretationCommand.Execute(Assert.Single(workbench.Interpretations, row => row.Name == "int8"));
        Assert.Equal("sys.int8", workbench.Field.DataType);
        workbench.Field.ApplyCommand.Execute(null);

        var span = workbench.Layouts[0]!.Find(11)!;
        Assert.Equal("sys.int8", span.Field.DataType.ToString());
        Assert.Equal("Pan", workbench.Field.Name);
    }

    [Fact]
    public void Field_FixedValue_IsTakenFromTheBaselineAndCheckedInEveryCapture()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", Message(0, 0x00, 0x10, 0x20, 0x30)));
        var other = new byte[15];
        Message(0, 0x00, 0x10, 0x20, 0x30).CopyTo(other, 0);
        other[1] = 0x43;
        workbench.AddCapture(new Capture("yamaha?", [other]));
        workbench.Selection = new ByteRange(0, 1, 1);
        var field = workbench.Field;
        field.Name = "RolandId";

        field.IsFixed = true;
        Assert.Equal("65", field.FixedValue);
        Assert.False(field.Values[0].HasProblem);
        // the reader checks the fixed value; the problem names the field as typed
        Assert.Contains("'Message1.RolandId': the value is '67'", field.Values[1].Problem);

        field.DefineCommand.Execute(null);
        var defined = workbench.Layouts[0]!.Find(1)!.Field;
        Assert.Equal("65", WorkbenchSchema.GetValue(defined));
        // the selected field shows its fixed value again
        Assert.True(field.IsFixed);
        Assert.Equal("65", field.FixedValue);

        field.IsFixed = false;
        field.ApplyCommand.Execute(null);
        Assert.Null(WorkbenchSchema.GetValue(defined));
    }

    [Fact]
    public void Field_CopyToOtherSlots_DefinesItWhereTheBytesAreTheSame()
    {
        var schemas = new FakeSchemaHost();
        var workbench = CreateWorkbench(schemas: schemas);
        byte[] yamaha = [0xF0, 0x43, 0x10, 0x20, 0xF7];
        workbench.AddCapture(Dump("patch1", Message(0, 1, 2, 3, 4), Message(1, 1, 2, 3, 4), yamaha, Message(3, 1, 2, 3, 4)));
        workbench.Selection = new ByteRange(0, 1, 1);
        var field = workbench.Field;
        field.Name = "RolandId";
        field.IsFixed = true;
        field.DefineCommand.Execute(null);

        // slot 3 has another manufacturer id
        Assert.Equal([1, 3], field.CopyTargets);
        Assert.Equal("Define it in 2 other slots", field.CopyText);
        field.CopyRequiresSameBytes = false;
        Assert.Equal([1, 2, 3], field.CopyTargets);
        field.CopyRequiresSameBytes = true;

        field.CopyToOtherSlotsCommand.Execute(null);

        var document = Assert.Single(schemas.Documents);
        Assert.Equal(["Message1", "Message2", "Message4"], document.Schema.Members.Select(member => member.Name));
        foreach (var slot in new[] { 1, 3 })
        {
            var copy = workbench.Layouts[slot]!.Find(1)!.Field;
            Assert.Equal("RolandId", copy.Name);
            Assert.Equal("65", WorkbenchSchema.GetValue(copy));
        }
        Assert.Null(workbench.Layouts[2]);
        // nowhere left to copy it to
        Assert.Empty(field.CopyTargets);
    }

    [Fact]
    public void Field_WhileDefining_DefinesHereAndInTheOtherSlotsAtOnce()
    {
        var schemas = new FakeSchemaHost();
        var workbench = CreateWorkbench(schemas: schemas);
        workbench.AddCapture(Dump("patch1", Message(0, 1, 2, 3, 4), Message(1, 1, 2, 3, 4), Message(2, 1, 2, 3, 4)));
        workbench.Selection = new ByteRange(0, 1, 1);
        var field = workbench.Field;
        Assert.Equal(FieldMode.Define, field.Mode);
        Assert.Equal([1, 2], field.CopyTargets);
        Assert.Equal("Define here and in 2 other slots", field.CopyText);

        field.Name = "RolandId";
        field.IsFixed = true;
        field.CopyToOtherSlotsCommand.Execute(null);

        Assert.Equal(3, Assert.Single(schemas.Documents).Schema.Members.Count);
        Assert.All(workbench.Layouts, layout => Assert.Equal("RolandId", layout!.Find(1)!.Field.Name));
        Assert.Equal(FieldMode.Edit, field.Mode);
        Assert.Empty(field.CopyTargets);
    }

    [Fact]
    public void Field_NameIsValidatedOnDefine()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", Message(0, 0x00, 0x10, 0x50, 0x30)));
        workbench.Selection = new ByteRange(0, 11, 1);

        workbench.Field.DefineCommand.Execute(null);

        Assert.Contains("name", workbench.Field.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FieldMode.Define, workbench.Field.Mode);
    }

    [Fact]
    public void NewSession_AsksForTheTitle()
    {
        var workbench = new WorkbenchViewModel(new FakeStorage().Create(), new MidiBarViewModel(null), new FakeSchemaHost().Create());
        Assert.False(workbench.HasSession);
        Assert.False(workbench.LoadSysExCommand.CanExecute(null));

        workbench.NewSessionCommand.Execute(null);
        var prompt = Assert.IsType<Jacobi.Midi.SchemaEditor.ViewModels.TextPromptViewModel>(workbench.Prompt);
        prompt.ConfirmCommand.Execute(null);
        Assert.True(prompt.HasError);

        prompt.Text = " GX-700 ";
        prompt.ConfirmCommand.Execute(null);

        Assert.Null(workbench.Prompt);
        Assert.Equal("GX-700", workbench.Title);
        Assert.True(workbench.HasSession);
        Assert.True(workbench.IsDirty);
        Assert.True(workbench.LoadSysExCommand.CanExecute(null));
    }

    [Fact]
    public void FirstCapture_IsTheBaseline_LaterCapturesAreComparedAndFocused()
    {
        var workbench = CreateWorkbench();
        var baseline = Dump("patch1", Message(0, 0x00, 0x10, 0x20, 0x30));
        var variant = Dump("again", Message(0, 0x00, 0x10, 0x28, 0x30));

        workbench.AddCapture(baseline);
        Assert.Same(baseline, workbench.Baseline);
        Assert.True(baseline.IsBaseline);
        Assert.Single(workbench.Slots);

        workbench.AddCapture(variant);
        Assert.Same(variant, workbench.SelectedCapture);
        Assert.Same(variant, workbench.FocusedVariant);
        Assert.Equal(2, variant.DifferenceCount);
        Assert.Equal("2 B Δ", variant.DifferenceText);

        variant.IsHidden = true;
        Assert.Null(workbench.FocusedVariant);
        Assert.False(workbench.Slots[0].HasDifference);
        Assert.Equal("hidden", variant.DifferenceText);
    }

    [Fact]
    public void Selection_IsInterpretedInEveryComparedCapture()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", Message(0, 0x00, 0x10, 0x20, 0x30)));
        var variant = Dump("again", Message(0, 0x00, 0x10, 0x28, 0x30));
        workbench.AddCapture(variant);
        variant.ChangeNote = "reverb 40";

        workbench.Selection = new ByteRange(0, 11, 1);

        Assert.Same(workbench.Slots[0], workbench.SelectedSlot);
        Assert.Equal(["patch1", "reverb 40"], workbench.InterpretationColumns);
        Assert.Equal(["32", "40"], Row(workbench.Interpretations, "uint8"));
        Assert.Contains("reverb 40: 28", workbench.DescribeByte(0, 11));
    }

    [Fact]
    public void SelectingASlot_SelectsItsFirstDataByte()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", Message(0, 1, 2, 3, 4), Message(1, 1, 2, 3, 4)));

        workbench.SelectedSlot = workbench.Slots[1];

        Assert.Equal(new ByteRange(1, 1, 1), workbench.Selection);
    }

    [Fact]
    public void AnnotationText_AddsChangesAndRemovesTheNoteOnTheSelection()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", Message(0, 1, 2, 3, 4)));
        workbench.Selection = new ByteRange(0, 9, 2);

        workbench.AnnotationText = "tone A/B?";
        Assert.Equal("tone A/B?", Assert.Single(workbench.Annotations).Text);
        Assert.Contains("Note (slot 1 · 9–10): tone A/B?", workbench.DescribeByte(0, 10));

        workbench.Selection = new ByteRange(0, 9, 1);
        Assert.Equal(String.Empty, workbench.AnnotationText);
        workbench.Selection = new ByteRange(0, 9, 2);
        workbench.AnnotationText = "tone";
        Assert.Equal("tone", Assert.Single(workbench.Annotations).Text);

        workbench.AnnotationText = "";
        Assert.Empty(workbench.Annotations);
    }

    [Fact]
    public void SetBaseline_AndRemovingTheBaseline_ChoosesAnotherBaseline()
    {
        var workbench = CreateWorkbench();
        var first = Dump("first", [0xF0, 0x01, 0xF7]);
        var second = Dump("second", [0xF0, 0x02, 0xF7]);
        workbench.AddCapture(first);
        workbench.AddCapture(second);

        workbench.SetBaselineCommand.Execute(second);
        Assert.Same(second, workbench.Baseline);
        Assert.False(first.IsBaseline);
        Assert.Equal(1, first.DifferenceCount);

        workbench.RemoveCaptureCore(second);
        Assert.Same(first, workbench.Baseline);
        Assert.True(first.IsBaseline);
    }

    [Fact]
    public void PasteHex_AddsACaptureSplitIntoMessages()
    {
        var workbench = CreateWorkbench();

        workbench.PasteHexCommand.Execute(null);
        var prompt = workbench.Prompt!;
        prompt.Text = "F0 41 F7 F0 42 F7";
        prompt.ConfirmCommand.Execute(null);

        var capture = Assert.Single(workbench.Captures);
        Assert.Equal(2, capture.Messages.Count);
        Assert.Same(capture, workbench.Baseline);
    }

    [Fact]
    public async Task LoadSysEx_AddsTheFileAsACapture()
    {
        var storage = new FakeStorage { SysEx = ("patch1.syx", [0xF0, 0x41, 0xF7, 0xF0, 0x42, 0xF7]) };
        var workbench = CreateWorkbench(storage);

        await workbench.LoadSysExCommand.ExecuteAsync(null);

        var capture = Assert.Single(workbench.Captures);
        Assert.Equal("patch1", capture.Label);
        Assert.Equal(2, workbench.Slots.Count);
    }

    [Fact]
    public async Task SaveAndOpenSession_RestoreTheSession()
    {
        var storage = new FakeStorage();
        var workbench = CreateWorkbench(storage);
        workbench.AddCapture(Dump("patch1", [0xF0, 0x41, 0xF7]));
        workbench.AddCapture(new Capture("again", [new byte[] { 0xF0, 0x42, 0xF7 }]) { ChangeNote = "reverb 40" });
        Assert.True(workbench.IsDirty);

        await workbench.SaveSessionCommand.ExecuteAsync(null);
        Assert.False(workbench.IsDirty);
        Assert.Equal("GX-700.workbench.json", workbench.SessionFileName);

        storage.Session = (new SessionFile("GX-700.workbench.json", null), storage.SavedText!);
        var other = new WorkbenchViewModel(storage.Create(), new MidiBarViewModel(null), new FakeSchemaHost().Create());
        other.OpenSessionCommand.Execute(null);

        Assert.Equal("GX-700", other.Title);
        Assert.Equal(2, other.Captures.Count);
        Assert.Same(other.Captures[0], other.Baseline);
        Assert.Equal("reverb 40", other.Captures[1].ChangeNote);
        Assert.Equal(1, other.Captures[1].DifferenceCount);
        Assert.False(other.IsDirty);
    }

    [Fact]
    public void NewSession_AsksToDiscardUnsavedChanges()
    {
        var workbench = CreateWorkbench();
        workbench.AddCapture(Dump("patch1", [0xF0, 0x41, 0xF7]));

        workbench.NewSessionCommand.Execute(null);
        Assert.Null(workbench.Prompt);
        var confirmation = Assert.IsType<Jacobi.Midi.SchemaEditor.ViewModels.ConfirmViewModel>(workbench.Confirmation);

        confirmation.ConfirmCommand.Execute(null);
        Assert.Null(workbench.Confirmation);
        Assert.NotNull(workbench.Prompt);
    }
}
