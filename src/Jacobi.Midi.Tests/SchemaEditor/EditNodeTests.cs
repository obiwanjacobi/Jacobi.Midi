using Jacobi.BinarySerializer.Schema;
using Jacobi.BinarySerializer.Yaml;
using Jacobi.Midi.SchemaEditor.Models;
using Xunit;
using JsonSerializer = Jacobi.BinarySerializer.Schema.Json.JsonSerializer;
using XmlSerializer = Jacobi.BinarySerializer.Schema.Xml.XmlSerializer;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class EditNodeTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("yaml")]
    public void RoundTrip_PreservesAuthoredSchema(string format)
    {
        var original = CreateDocument();
        var edit = EditNodeMapper.ToEditDocument(original);
        var snapshot = EditNodeMapper.ToSchemaDocument(edit);
        var reloaded = format switch
        {
            "xml" => XmlSerializer.Deserialize(XmlSerializer.Serialize(snapshot)),
            "yaml" => YamlSerializer.Deserialize(YamlSerializer.Serialize(snapshot)),
            _ => JsonSerializer.Deserialize(JsonSerializer.Serialize(snapshot))
        };

        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(snapshot));
        Assert.Equal(XmlSerializer.Serialize(original), XmlSerializer.Serialize(snapshot));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(reloaded));
        Assert.Same(snapshot.Members.Single(), snapshot.Roots.Single());
        Assert.Equal(new[] { "Root", "Flags", "Items", "Variant", "Body" }, snapshot.Groups.Select(group => group.Name));
        Assert.Equal(new[] { "Status", "Payload", "Mode", "Entry" }, snapshot.Fields.Select(field => field.Name));
        Assert.False(snapshot.IsCompiled);
    }

    [Fact]
    public void Edits_DoNotMutateOriginalOrEarlierSnapshots()
    {
        var original = CreateDocument();
        var edit = EditNodeMapper.ToEditDocument(original);
        var root = (EditGroup)edit.Members.Single();
        var payload = (EditField)root.Members[1];
        var before = EditNodeMapper.ToSchemaDocument(edit);

        payload.Name = "Changed";
        payload.Processors.Single().Properties.Single().Value = "changed";
        root.Properties.Single().Value = "changed";
        edit.DataTypeDefs.Single().Options!["1"] = "Changed";
        edit.Includes.Single().Path = "changed.json";
        root.Members.RemoveAt(0);

        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(before));
        var after = EditNodeMapper.ToSchemaDocument(edit);
        Assert.Equal("Changed", after.Fields.First().Name);
        Assert.Equal("Changed", after.DataTypeDefs.Single().Options!["1"]);
        Assert.Equal("changed.json", after.Includes.Single().Path);
        Assert.Equal("One", original.DataTypeDefs.Single().Options!["1"]);
        Assert.NotSame(before.Members.Single(), after.Members.Single());
        Assert.NotSame(before.NodeDefs.Single(), after.NodeDefs.Single());
        Assert.NotSame(before.Includes.Single(), after.Includes.Single());
    }

    [Fact]
    public void NodeRoundTrip_PreservesReferenceIndices()
    {
        Assert.True(SchemaNodeRef.TryParse("ref:Root.Items[].Payload[2].Length", out var reference));
        var field = new EditField(null) { Name = "Payload", ByteLength = reference!, Processors = [], Properties = [] };
        var snapshot = (SchemaField)EditNodeMapper.ToSchemaNode(field);
        var restored = (EditField)EditNodeMapper.ToEditNode(snapshot, null);
        Assert.True(restored.ByteLength is SchemaNodeRef result && result.ToString() == reference!.ToString());
        Assert.True(snapshot.ByteLength is SchemaNodeRef copy && !ReferenceEquals(copy, reference));
    }

    [Fact]
    public void Snapshot_PreservesDocumentSettingsAndTypedReferences()
    {
        var edit = new EditDocument
        {
            Name = "Doc",
            NodeDef = new SchemaName("Base"),
            ByteSize = 20,
            DataTypeDefs = [],
            Includes = [],
            Members = [],
            NodeDefs = [],
            ProcessorDefs = [],
            Processors = [],
            Properties = [],
        };
        edit.Properties.Add(new EditProperty { Name = "custom", Value = null });
        edit.Processors.Add(new EditProcessorRef { Processor = new SchemaName("sys.align"), PublishNamespace = "doc", Properties = [] });
        var group = new EditGroup(edit) { Name = "Root", Members = [], Processors = [], Properties = [] };
        group.Members.Add(new EditField(group)
        {
            Name = "Value",
            Value = new SchemaNodeRef { Path = "Root.Status" },
            ByteLength = new SchemaPubRef { Namespace = "header", Name = "size" },
            Processors = [],
            Properties = [],
        });
        edit.Members.Add(group);

        var snapshot = EditNodeMapper.ToSchemaDocument(edit);
        Assert.Equal("Base", snapshot.NodeDef?.ToString());
        Assert.True(snapshot.ByteSize is int size && size == 20);
        Assert.Null(snapshot.Properties.Single().Value);
        Assert.Equal("doc", snapshot.Processors.Single().PublishNamespace);
        Assert.True(snapshot.Fields.Single().Value is SchemaNodeRef reference && reference.Path == "Root.Status");
        Assert.True(snapshot.Fields.Single().ByteLength is SchemaPubRef length && length.ToString() == "pub:header.size");
    }

    [Fact]
    public void RoundTrip_ConcreteDefinitions_PreservesTypedSettingsAndPipelinePresence()
    {
        var original = new SchemaDocumentBuilder("Doc")
            .NodeDefs(definitions => definitions
                .Field("Value", "sys.bytes", value: "240", byteLength: new SchemaPubRef { Namespace = "header", Name = "length" }, byteOffset: -1)
                .Group("Body", members => members.Field("Code"), byteSize: 6)
                .Repeat("Rows", new SchemaNodeRef { Path = "Root.Count" }, members => members.Field("Row"), byteSize: 8,
                    valueProcessors: processors => processors.Add("sys.map"))
                .Choice("Variant", 1, members => members.Group("Alternative"), byteSize: 4,
                    valueProcessors: processors => processors.Add("sys.map"))
                .OpenChoice("Open", "Body", SchemaPipelineMode.Isolate))
            .Group("Root", members => members
                .Field("Count")
                .Choice("Inherited", default, nodeDef: "Open")
                .Choice("Explicit", default, nodeDef: "Open", pipeline: SchemaPipelineMode.Integrate))
            .Build();
        var edit = EditNodeMapper.ToEditDocument(original);
        Assert.Equal(new[]
        {
            typeof(EditField), typeof(EditGroup), typeof(EditRepeat), typeof(EditChoice), typeof(EditChoice)
        }, edit.NodeDefs.Select(node => node.GetType()));
        var snapshot = EditNodeMapper.ToSchemaDocument(edit);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(snapshot));
        var builder = new SchemaSetBuilder();
        builder.AddDocument(snapshot);
        builder.Compile();
        var choices = snapshot.Roots.Single().Members.OfType<SchemaChoice>().ToArray();
        Assert.Equal(SchemaPipelineMode.Isolate, choices[0].Pipeline);
        Assert.Equal(SchemaPipelineMode.Integrate, choices[1].Pipeline);
        Assert.Throws<ArgumentException>(() => EditNodeMapper.ToEditDocument(snapshot));
    }

    [Theory]
    [InlineData(SchemaNodeKind.Field)]
    [InlineData(SchemaNodeKind.Group)]
    [InlineData(SchemaNodeKind.Repeat)]
    [InlineData(SchemaNodeKind.Choice)]
    public void NodeRoundTrip_PreservesPropertiesProcessorsAndMemberOrder(SchemaNodeKind kind)
    {
        EditNode edit = kind switch
        {
            SchemaNodeKind.Field => new EditField(null) { Name = "", Processors = [], Properties = [] },
            SchemaNodeKind.Repeat => new EditRepeat(null) { Name = "", Members = [], Processors = [], Properties = [], ValueProcessors = [] },
            SchemaNodeKind.Choice => new EditChoice(null) { Name = "", Members = [], Processors = [], Properties = [], ValueProcessors = [] },
            _ => new EditGroup(null) { Name = "", Members = [], Processors = [], Properties = [] }
        };
        edit.Name = "Node";
        edit.NodeDef = new SchemaName("Other.Template");
        edit.Properties.Add(new EditProperty { Name = "custom", Value = null });
        edit.Properties.Add(new EditProperty { Name = "custom", Value = "second" });
        var processors = edit is EditField field ? field.Processors : ((EditGroup)edit).Processors;
        var processor = new EditProcessorRef { Processor = new SchemaName("Other.map"), PublishNamespace = "node", Properties = [] };
        processor.Properties.Add(new EditProperty { Name = "option", Value = null });
        processors.Add(processor);
        if (edit is EditGroup group)
        {
            group.Members.Add(new EditField(group) { Name = "First", Processors = [], Properties = [] });
            group.Members.Add(new EditField(group) { Name = "Second", Processors = [], Properties = [] });
        }

        var snapshot = EditNodeMapper.ToSchemaNode(edit);
        var restored = EditNodeMapper.ToEditNode(snapshot, null);
        Assert.Equal(edit.GetType(), restored.GetType());
        Assert.Equal(edit.Name, restored.Name);
        Assert.Equal(edit.NodeDef, restored.NodeDef);
        Assert.Equal(edit.Properties.Select(property => (property.Name, property.Value)),
            restored.Properties.Select(property => (property.Name, property.Value)));
        var restoredProcessors = restored is EditField restoredField ? restoredField.Processors : ((EditGroup)restored).Processors;
        var restoredProcessor = Assert.Single(restoredProcessors);
        Assert.Equal(processor.Processor, restoredProcessor.Processor);
        Assert.Equal("node", restoredProcessor.PublishNamespace);
        Assert.Null(Assert.Single(restoredProcessor.Properties).Value);
        if (restored is EditGroup restoredGroup)
        {
            Assert.Equal(new[] { "First", "Second" }, restoredGroup.Members.Select(member => member.Name));
        }
        else
        {
            Assert.Null(((EditField)restored).DataType);
        }
    }

    private static SchemaDocument CreateDocument()
        => JsonSerializer.Deserialize("""
            {
              "name": "Doc",
              "properties": [{ "name": "description", "value": "Document" }],
              "includes": [{ "schema": "Other", "path": "other.json" }],
              "nodeDefs": [{ "name": "BodyType", "kind": "group",
                "properties": [{ "name": "extra", "value": null }],
                "processors": [{ "name": "sys.align", "pubns": "body", "alignment": "2" }],
                "members": [{ "name": "DefinitionField", "kind": "field", "datatype": "sys.uint8" }] }],
              "dataTypeDefs": [{ "name": "Scaled", "basedOn": "sys.int16", "min": -10, "max": 100, "scale": 2, "shift": 1,
                "options": { "1": "One" }, "properties": [{ "name": "note", "value": null }],
                "processors": [{ "name": "sys.scale", "pubns": "scaled" }] }],
              "processorDefs": [{ "name": "map", "processor": "sys.map", "properties": [{ "name": "other", "value": null }] }],
              "members": [{ "name": "Root", "kind": "group", "byteSize": { "pub": "header.size" },
                "properties": [{ "name": "note", "value": "root" }],
                "processors": [{ "name": "sys.align", "pubns": "root", "alignment": "4" }],
                "members": [
                  { "name": "Status", "kind": "field", "datatype": "sys.uint8", "value": "240", "byteOffset": 0 },
                  { "name": "Payload", "kind": "field", "nodeDef": "Other.Payload", "datatype": "sys.bytes",
                    "value": { "pub": "header.value" }, "byteLength": { "ref": "Root.Items[].Length" },
                    "processors": [{ "name": "map", "pubns": "payload", "extra": "value" }] },
                  { "name": "Flags", "kind": "group", "byteSize": 3,
                    "processors": [{ "name": "sys.bitpacker", "bitorder": "little" }],
                    "members": [{ "name": "Mode", "kind": "field", "datatype": "sys.uint8" }] },
                  { "name": "Items", "kind": "repeat", "count": { "ref": "Root.Status" }, "byteSize": 12,
                    "valueProcessors": [{ "name": "map", "pubns": "count" }],
                    "members": [{ "name": "Entry", "kind": "field", "datatype": "Scaled", "byteLength": 2 }] },
                  { "name": "Variant", "kind": "choice", "selectedIndex": { "pub": "header.index" },
                    "valueProcessors": [{ "name": "map" }],
                    "members": [{ "name": "Body", "kind": "choice", "memberNodeDef": "BodyType", "pipeline": "isolate" }] }
                ] }]
            }
            """);
}