using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class NodeDetailsTests
{
    [Fact]
    public void Resolve_MergesDataTypeNodeDefAndOwnInPrecedenceOrder()
    {
        var document = CreateDocument();
        document.DataTypeDefs.Add(new EditDataTypeDef
        {
            Name = "base", BasedOn = new SchemaName("sys.uint8"),
            Properties = [Property("a", "base"), Property("b", "base")],
        });
        document.DataTypeDefs.Add(new EditDataTypeDef
        {
            Name = "derived", BasedOn = new SchemaName("base"),
            Properties = [Property("b", "derived"), Property("c", "derived")],
        });
        var definition = Field(null, "def", dataType: "derived");
        definition.Properties.Add(Property("c", "def"));
        definition.Properties.Add(Property("d", "def"));
        document.NodeDefs.Add(definition);
        var field = Field(Root(document), "field", dataType: null);
        field.NodeDef = new SchemaName("def");
        field.Properties.Add(Property("d", "own"));

        var effective = EffectiveNodeResolver.Resolve(document, field);

        Assert.Equal(
            [("a", "base", "dataType base"), ("b", "derived", "dataType derived"), ("c", "def", "nodeDef def"), ("d", "own", "own")],
            effective.Properties.Select(p => (p.Name, p.Value, p.Source)));
        Assert.Same(field.Properties[0], effective.Properties[3].Own);
        Assert.All(effective.Properties.Take(3), p => Assert.Null(p.Own));
        Assert.Empty(effective.Unresolved);
    }

    [Fact]
    public void Resolve_ProcessorRefsMergeWithProcessorDefsAndOverrideInherited()
    {
        var document = CreateDocument();
        document.ProcessorDefs.Add(new EditProcessorDef { Name = "align", Processor = "sys.align", Properties = [Property("bytes", "4"), Property("relative", "root")] });
        var definition = Group(null, "def");
        definition.Processors.Add(new EditProcessorRef { Processor = new SchemaName("ref:align") });
        definition.Processors.Add(new EditProcessorRef { Processor = new SchemaName("sys.checksum") });
        document.NodeDefs.Add(definition);
        var group = Group(Root(document), "group");
        group.NodeDef = new SchemaName("def");
        var own = new EditProcessorRef { Processor = new SchemaName("ref:align"), Properties = [Property("bytes", "8")] };
        group.Processors.Add(own);

        var effective = EffectiveNodeResolver.Resolve(document, group);

        Assert.Equal(["sys.checksum", "ref:align"], effective.Processors.Select(p => p.Processor.Processor.ToString()));
        var align = effective.Processors[1];
        Assert.True(align.IsOwn);
        Assert.Same(own, align.Processor);
        Assert.Equal([("relative", "root", "processorDef align"), ("bytes", "8", "own")], align.Properties.Select(p => (p.Name, p.Value, p.Source)));
        Assert.False(effective.Processors[0].IsOwn);
        Assert.Equal("nodeDef def", effective.Processors[0].Source);
    }

    [Fact]
    public void Resolve_ReportsDefinitionsOfOtherDocuments()
    {
        var document = CreateDocument();
        document.Includes.Add(new EditDocumentRef { Schema = "midi" });
        var field = Field(Root(document), "field", dataType: "midi.sysexbyte");
        field.NodeDef = new SchemaName("midi.byte");
        field.Processors.Add(new EditProcessorRef { Processor = new SchemaName("ref:midi.sysex") });

        var effective = EffectiveNodeResolver.Resolve(document, field);

        Assert.Equal(3, effective.Unresolved.Count);
        Assert.Contains(effective.Unresolved, message => message.Contains("nodeDef 'midi.byte'"));
        Assert.Contains(effective.Unresolved, message => message.Contains("dataType 'midi.sysexbyte'"));
        Assert.Contains(effective.Unresolved, message => message.Contains("processorDef 'ref:midi.sysex'"));
        // built-in types are not reported
        Assert.Empty(EffectiveNodeResolver.Resolve(document, Field(Root(document), "plain", dataType: "sys.uint8")).Unresolved);
    }

    [Fact]
    public void Details_EditOwnEntriesAndMarkDocumentChanged()
    {
        var schema = CreateDocument();
        var definition = Field(null, "def", dataType: "sys.uint8");
        definition.Properties.Add(Property("inherited", "x"));
        schema.NodeDefs.Add(definition);
        var field = Field(Root(schema), "field", dataType: null);
        field.NodeDef = new SchemaName("def");
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        using var details = new NodeDetailsViewModel(document, field);

        var inherited = Assert.Single(details.Properties);
        Assert.False(inherited.IsOwn);
        details.OverridePropertyCommand.Execute(inherited);
        var overridden = Assert.Single(details.Properties);
        Assert.True(overridden.IsOwn);
        Assert.Equal("x", overridden.Value);
        Assert.True(document.IsDirty);

        var revision = document.Revision;
        overridden.Value = "y";
        Assert.Equal("y", Assert.Single(field.Properties).Value);
        Assert.Equal(revision + 1, document.Revision);
        // a value edit keeps the rows (and so the focused editor)
        Assert.Same(overridden, Assert.Single(details.Properties));

        details.RemovePropertyCommand.Execute(overridden);
        Assert.Empty(field.Properties);
        Assert.False(Assert.Single(details.Properties).IsOwn);

        details.AddProcessorCommand.Execute(null);
        var processor = Assert.Single(details.Processors);
        processor.Processor = "sys.scale";
        Assert.Equal("sys.scale", Assert.Single(field.Processors).Processor.ToString());
        details.AddProcessorPropertyCommand.Execute(details.Processors[0]);
        Assert.Equal("property", Assert.Single(Assert.Single(details.Processors).Properties).Name);

        details.RemoveProcessorCommand.Execute(details.Processors[0]);
        Assert.Empty(field.Processors);
        Assert.Empty(details.Processors);
    }

    [Fact]
    public void Details_RebuildsWhenTheNodeIsEditedElsewhere()
    {
        var schema = CreateDocument();
        var field = Field(Root(schema), "field", dataType: "sys.uint8");
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        using var details = new NodeDetailsViewModel(document, field);
        Assert.Empty(details.Properties);

        document.ApplyNodeEdits(field, "field", "sys.uint8", "A description", String.Empty);

        Assert.Equal("description", Assert.Single(details.Properties).Name);
    }

    private static EditDocument CreateDocument() => SchemaFileService.CreateDocument("Test");

    private static EditGroup Root(EditDocument document) => (EditGroup)document.Members[0];

    private static EditProperty Property(string name, string? value) => new() { Name = name, Value = value };

    private static EditField Field(EditGroup? parent, string name, string? dataType)
    {
        var field = new EditField(parent)
        {
            Name = name,
            DataType = dataType is null ? (SchemaName?)null : new SchemaName(dataType),
            Processors = [],
            Properties = [],
        };
        parent?.Members.Add(field);
        return field;
    }

    private static EditGroup Group(EditGroup? parent, string name)
    {
        var group = new EditGroup(parent) { Name = name, Members = [], Processors = [], Properties = [] };
        parent?.Members.Add(group);
        return group;
    }
}
