using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class NodeFacetsTests
{
    [Fact]
    public void Edit_WritesCountAndSelectedIndexAsConstantOrReference()
    {
        var document = new SchemaWorkspaceDocument(SchemaFileService.CreateDocument("test"), SchemaFormat.Json);
        var root = document.Schema.Members.OfType<EditGroup>().Single();
        var repeat = document.AddRepeat(root, "Items", 1);
        var facets = new NodeFacets(document, repeat);
        Assert.Equal("1", facets.Count);

        facets.Count = "ref:test.SysEx.Length";
        Assert.True(repeat.Count is SchemaNodeRef);

        facets.Count = "3";
        Assert.Equal("3", new NodeFacets(document, repeat).Count);
    }

    [Fact]
    public void ValueOrRef_ReadsAndWritesThePropertyByName()
    {
        var document = new SchemaWorkspaceDocument(SchemaFileService.CreateDocument("test"), SchemaFormat.Json);
        var root = document.Schema.Members.OfType<EditGroup>().Single();
        var repeat = document.AddRepeat(root, "Items", 1);
        var facets = new NodeFacets(document, repeat);

        facets.SetValueOrRef(nameof(NodeFacets.Count), "ref:test.SysEx.Length");

        Assert.Equal("ref:test.SysEx.Length", facets.GetValueOrRef(nameof(NodeFacets.Count)));
        Assert.True(repeat.Count is SchemaNodeRef);
        Assert.Equal("byte size", NodeFacets.GetValueOrRefLabel(nameof(NodeFacets.ByteSize)));
        Assert.Throws<ArgumentOutOfRangeException>(() => facets.GetValueOrRef(nameof(NodeFacets.Name)));
    }

    [Fact]
    public void Edit_InvalidValueChangesNothing()
    {
        var document = new SchemaWorkspaceDocument(SchemaFileService.CreateDocument("test"), SchemaFormat.Json);
        var root = document.Schema.Members.OfType<EditGroup>().Single();
        var choice = document.AddChoice(root, "Pick", 2);
        var revision = document.Revision;
        var facets = new NodeFacets(document, choice);

        Assert.Throws<ArgumentException>(() => facets.SelectedIndex = "abc");

        Assert.Equal("2", facets.SelectedIndex);
        Assert.Equal(revision, document.Revision);
    }

    [Fact]
    public void Edit_IsIgnoredForAReadOnlyDocument()
    {
        var document = new SchemaWorkspaceDocument(SchemaFileService.CreateDocument("test"), SchemaFormat.Json) { IsReadOnly = true };
        var root = document.Schema.Members.OfType<EditGroup>().Single();

        new NodeFacets(document, root).Name = "Changed";

        Assert.NotEqual("Changed", root.Name);
    }
}
