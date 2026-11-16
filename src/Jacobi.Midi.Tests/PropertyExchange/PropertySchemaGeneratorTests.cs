using System.Text.Json.Nodes;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Device.PropertyExchange;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.PropertyExchange;

/// <summary>
/// Tests <see cref="PropertySchemaGenerator"/>: a schema document generated from a <c>ResourceList</c> and JSON
/// Schemas, after the examples of M2-103 (sections 10, 14.4 and 14.7), that compiles and gives the plans.
/// </summary>
public class PropertySchemaGeneratorTests
{
    private const string ResourceList = """
        [
          { "resource": "DeviceInfo" },
          { "resource": "ChannelList", "canSubscribe": true },
          { "resource": "RawBuffer", "canSet": "full", "mediaTypes": ["application/octet-stream"], "encodings": ["MCoded7"] },
          { "resource": "X-Tempo", "canSet": "full",
            "schema": { "title": "BPM", "description": "Set the Tempo, Global Setting", "type": "integer", "minimum": 40, "maximum": 240 } },
          { "resource": "X-Globset", "canSet": "full",
            "schema": { "title": "System Settings", "type": "object", "properties": {
              "deviceName": { "type": "string" },
              "audioOut": { "type": "string", "enum": ["USB", "line"] },
              "cv-pitch": { "type": "string", "enum": ["v/oct", "hz/oct"] } } } },
          { "resource": "X-ProgramEdit", "canSet": "partial", "requireResId": true, "canSubscribe": true,
            "schema": { "title": "Edit Patch.", "type": "object", "$ref": "midi+jsonschema://programDetails" } },
          { "resource": "X-PatchList",
            "schema": { "type": "array", "items": { "type": "object", "properties": {
              "title": { "type": "string" }, "number": { "type": "integer" } } } } },
          { "resource": "X-Missing", "schema": { "type": "object", "$ref": "midi+jsonschema://missing" } }
        ]
        """;

    private const string ProgramDetails = """
        { "type": "object", "properties": {
            "name": { "type": "string", "description": "The name of the program." },
            "lfoSpeed": { "type": "integer", "minimum": 0, "maximum": 127 },
            "lfoWaveform": { "type": "string", "enum": ["triangle", "square", "saw"] },
            "pitchEnvelope": { "type": "object", "properties": {
              "rates": { "type": "array", "minItems": 4, "maxItems": 4, "items": { "type": "integer" } },
              "levels": { "type": "array", "items": { "type": ["integer", "null"] } } } },
            "on": { "type": "boolean" },
            "gain": { "type": "number" },
            "mystery": { "anyOf": [ { "type": "integer" }, { "type": "string" } ] } } }
        """;

    private static PropertySchema Generate()
        => PropertySchemaGenerator.Generate("Pe", PropertyResource.FromResourceList(JsonNode.Parse(ResourceList)),
            new Dictionary<string, JsonObject> { ["programDetails"] = JsonNode.Parse(ProgramDetails)!.AsObject() });

    private static (Serializer Serializer, PropertySchema Schema) Compile()
    {
        var schema = Generate();
        return (TestDevice.CreateSerializer(schema.Document), schema);
    }

    private static FieldInfo Field(Serializer serializer, string root, string path)
        => Assert.IsType<FieldInfo>(serializer.GetPlan(new SchemaName($"Pe.{root}")).Find(new SchemaPath(path)));

    [Fact]
    public void FromResourceList_TakesTheDefaultsOfTheSpecification()
    {
        var resources = PropertyResource.FromResourceList(JsonNode.Parse(ResourceList));

        var deviceInfo = resources[0];
        Assert.True(deviceInfo.CanGet);
        Assert.Equal("none", deviceInfo.CanSet);
        Assert.False(deviceInfo.CanSubscribe);
        Assert.True(deviceInfo.IsJson);
        Assert.False(resources[2].IsJson);
        Assert.Equal("programDetails", resources[5].SchemaReference);
        Assert.True(resources[5].RequireResId);
        Assert.Equal("partial", resources[5].CanSet);
    }

    [Fact]
    public void Generate_ARootPerResourceWithASchema_AndNotesForTheOthers()
    {
        var (serializer, schema) = Compile();

        // the standard resources with their published schemas
        Assert.Equal(["DeviceInfo", "ChannelList", "XTempo", "XGlobset", "XProgramEdit", "XPatchList"], schema.Document.Roots.Select(root => root.Name));
        Assert.All(schema.Document.Roots, root => Assert.True(MidiProperties.IsPropertyRoot(root)));
        // the root is named after the resource; the resource name in midi.key when it is not a valid name
        Assert.Equal("X-ProgramEdit", MidiProperties.Find(schema.Document.Roots[4], MidiProperties.Key));
        Assert.Null(MidiProperties.Find(schema.Document.Roots[0], MidiProperties.Key));
        Assert.Contains(schema.Notes, note => note.StartsWith("'RawBuffer'", StringComparison.Ordinal));
        Assert.Contains(schema.Notes, note => note.StartsWith("'X-Missing'", StringComparison.Ordinal));
        Assert.Contains(schema.Notes, note => note.Contains("XProgramEdit.mystery", StringComparison.Ordinal));
        // every root gets a plan
        Assert.All(schema.Document.Roots, root => serializer.GetPlan(new SchemaName($"Pe.{root.Name}")));
    }

    [Fact]
    public void Generate_AResourceThatCannotBeSet_IsReadOnly()
    {
        var schema = Generate();
        string? AccessOf(string root) => MidiProperties.Find(schema.Document.Roots.Single(group => group.Name == root), MidiProperties.Access);

        // canSet none (the default of DeviceInfo and ChannelList): read-only
        Assert.Equal(MidiProperties.AccessRead, AccessOf("DeviceInfo"));
        Assert.Equal(MidiProperties.AccessRead, AccessOf("ChannelList"));
        Assert.Equal(MidiProperties.AccessRead, AccessOf("XPatchList"));
        // canSet full or partial: the default readwrite, no tag
        Assert.Null(AccessOf("XTempo"));
        Assert.Null(AccessOf("XGlobset"));
        Assert.Null(AccessOf("XProgramEdit"));
    }

    [Fact]
    public void Generate_AResourceThatIsSetWhole_IsAtomic_PerEntryWhenItHasEntries()
    {
        var schema = PropertySchemaGenerator.Generate("Pe", PropertyResource.FromResourceList(JsonNode.Parse(ResourceList)),
            new Dictionary<string, JsonObject> { ["programDetails"] = JsonNode.Parse(ProgramDetails)!.AsObject() },
            new Dictionary<string, IReadOnlyList<string>> { ["X-Globset"] = ["main", "my set"] });
        SchemaGroup Root(string name) => schema.Document.Roots.Single(group => group.Name == name);
        string? TransferOf(SchemaNode node) => MidiProperties.Find(node, MidiProperties.Transfer);

        // canSet full: the root, or each entry (a Set goes per entry)
        Assert.Equal(MidiProperties.TransferAtomic, TransferOf(Root("XTempo")));
        var globset = Root("XGlobset");
        Assert.Null(TransferOf(globset));
        var entries = Assert.IsType<SchemaChoice>(Assert.Single(globset.Members));
        Assert.All(entries.Members, entry => Assert.Equal(MidiProperties.TransferAtomic, TransferOf(entry)));
        Assert.Equal("my set", MidiProperties.Find(entries.Members[1], MidiProperties.Key));
        // canSet partial or none: no tag
        Assert.Null(TransferOf(Root("XProgramEdit")));
        Assert.Null(TransferOf(Root("DeviceInfo")));
        // the tags make a valid schema
        var serializer = TestDevice.CreateSerializer(schema.Document);
        Assert.DoesNotContain(MessageMapValidator.Validate(serializer), problem => problem.Document == "Pe");
    }

    [Fact]
    public void Generate_AResourceWithResIds_HasTheChoiceResId_WithAnAlternativePerResId()
    {
        var schema = PropertySchemaGenerator.Generate("Pe", PropertyResource.FromResourceList(JsonNode.Parse(ResourceList)),
            new Dictionary<string, JsonObject> { ["programDetails"] = JsonNode.Parse(ProgramDetails)!.AsObject() },
            new Dictionary<string, IReadOnlyList<string>> { ["X-ProgramEdit"] = ["abcd", "my prog"], ["X-PatchList"] = ["bank1"] });
        var serializer = TestDevice.CreateSerializer(schema.Document);

        var edit = serializer.GetPlan(new SchemaName("Pe.XProgramEdit"));
        var entries = Assert.IsType<ChoiceInfo>(Assert.Single(edit.Root.Members));
        Assert.Equal(PropertySchemaGenerator.ResIdName, entries.Name);
        Assert.True(entries.IsOpen);
        Assert.Equal(["abcd", "myprog"], entries.Members.Select(member => member.Name));
        Assert.Equal("my prog", MidiProperties.Find(entries.Members[1], MidiProperties.Key));
        // each alternative has the structure of the resource
        Assert.Equal(typeof(string), Assert.IsType<FieldInfo>(edit.Find(new SchemaPath("XProgramEdit.resId.myprog.name"))).ValueType.ClrType);
        Assert.Equal(3, Assert.IsType<FieldInfo>(edit.Find(new SchemaPath("XProgramEdit.resId.abcd.lfoWaveform"))).DataType.Options?.Count);
        // requireResId: only the resIds; else the doc tells the author it can also be read without one
        Assert.DoesNotContain("without a resId", MidiProperties.DocOf(entries), StringComparison.Ordinal);
        var list = serializer.GetPlan(new SchemaName("Pe.XPatchList"));
        Assert.IsType<FieldInfo>(list.Find(new SchemaPath("XPatchList.resId.bank1.items.title")));
        Assert.Contains("without a resId", MidiProperties.DocOf(Assert.IsType<ChoiceInfo>(Assert.Single(list.Root.Members))), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_AResourceThatRequiresAResIdWithoutLinks_IsAPlainRootWithANote()
    {
        var (serializer, _) = Compile();

        var edit = serializer.GetPlan(new SchemaName("Pe.XProgramEdit"));

        Assert.IsType<FieldInfo>(edit.Find(new SchemaPath("XProgramEdit.name")));
        Assert.Contains("requires a resId", MidiProperties.DocOf(edit.Root), StringComparison.Ordinal);
    }

    [Fact]
    public void Links_GiveTheResIdsByResource_EachOnceInTheOrderFound()
    {
        var resourceIds = new Dictionary<string, List<string>>();

        PropertyLinks.Collect(JsonNode.Parse("""
            [ { "title": "Melodic", "links": [ { "resource": "ProgramList", "resId": "melodic" }, { "resource": "ChCtrlList", "resId": "channel" } ] },
              { "title": "Drums", "links": [ { "resource": "ProgramList", "resId": "drums" } ], "more": { "resource": "ProgramList", "resId": "melodic" } },
              { "resource": "NoId" } ]
            """), resourceIds);

        Assert.Equal(["melodic", "drums"], resourceIds["ProgramList"]);
        Assert.Equal(["channel"], resourceIds["ChCtrlList"]);
        Assert.False(resourceIds.ContainsKey("NoId"));
    }

    [Fact]
    public void Generate_TheTypesAndDomains()
    {
        var (serializer, _) = Compile();

        var tempo = Field(serializer, "XTempo", "XTempo.value");
        Assert.Equal(typeof(long), tempo.ValueType.ClrType);
        Assert.Equal(40L, tempo.DataType.Min);
        Assert.Equal(240L, tempo.DataType.Max);
        Assert.Equal("Set the Tempo, Global Setting", MidiProperties.DocOf(serializer.GetPlan(new SchemaName("Pe.XTempo")).Root));

        Assert.Equal(3, Field(serializer, "XProgramEdit", "XProgramEdit.lfoWaveform").DataType.Options?.Count);
        Assert.Equal(typeof(string), Field(serializer, "XProgramEdit", "XProgramEdit.name").ValueType.ClrType);
        Assert.Equal("The name of the program.", MidiProperties.DocOf(Field(serializer, "XProgramEdit", "XProgramEdit.name")));
        Assert.Equal(typeof(bool), Field(serializer, "XProgramEdit", "XProgramEdit.on").ValueType.ClrType);
        Assert.Equal(typeof(decimal), Field(serializer, "XProgramEdit", "XProgramEdit.gain").ValueType.ClrType);
    }

    [Fact]
    public void Generate_ArraysAreRepeats_AndNamesKeepTheirJsonKey()
    {
        var (serializer, _) = Compile();
        var edit = serializer.GetPlan(new SchemaName("Pe.XProgramEdit"));

        var rates = Assert.IsType<RepeatInfo>(edit.Find(new SchemaPath("XProgramEdit.pitchEnvelope.rates")));
        Assert.Equal(4, rates.Count.Value);
        Assert.Equal(typeof(long), Field(serializer, "XProgramEdit", "XProgramEdit.pitchEnvelope.levels.item").ValueType.ClrType);
        Assert.IsType<RepeatInfo>(serializer.GetPlan(new SchemaName("Pe.XPatchList")).Find(new SchemaPath("XPatchList.items")));
        Field(serializer, "XPatchList", "XPatchList.items.number");

        var pitch = Field(serializer, "XGlobset", "XGlobset.cvpitch");
        Assert.Equal("cv-pitch", MidiProperties.Find(pitch, MidiProperties.Key));
        Assert.Null(MidiProperties.Find(Field(serializer, "XGlobset", "XGlobset.audioOut"), MidiProperties.Key));
    }

    [Fact]
    public void AGeneratedDocument_CanBeSavedAsASchemaFile()
    {
        var xml = Jacobi.BinarySerializer.Schema.Xml.XmlSerializer.Serialize(Generate().Document);

        var document = TestDevice.Parse(xml);
        var serializer = TestDevice.CreateSerializer(document);

        Assert.Equal(3, Field(serializer, "XProgramEdit", "XProgramEdit.lfoWaveform").DataType.Options?.Count);
        Assert.All(document.Roots, root => Assert.True(MidiProperties.IsPropertyRoot(root)));
        Assert.Equal("cv-pitch", MidiProperties.Find(Field(serializer, "XGlobset", "XGlobset.cvpitch"), MidiProperties.Key));
    }

    [Fact]
    public void AGeneratedDocument_HasNoMessageMapProblems_AndNoMessageMap()
    {
        var (serializer, _) = Compile();

        Assert.DoesNotContain(MessageMapValidator.Validate(serializer), problem => problem.Document == "Pe");
        var exception = Assert.Throws<InvalidOperationException>(() => new MessageMaps(serializer).Info(new SchemaName("Pe.XTempo")));
        Assert.Contains("Property Exchange root", exception.Message, StringComparison.Ordinal);
    }
}
