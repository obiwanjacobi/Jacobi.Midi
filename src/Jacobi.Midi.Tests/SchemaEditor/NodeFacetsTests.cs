using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class NodeFacetsTests
{
    [Fact]
    public void Apply_WritesCountAndSelectedIndexAsConstantOrReference()
    {
        var document = new SchemaWorkspaceDocument(SchemaFileService.CreateDocument("test"), SchemaFormat.Json);
        var root = document.Schema.Members.OfType<EditGroup>().Single();
        var repeat = document.AddRepeat(root, "Items", 1);
        var facets = NodeFacets.Read(repeat);
        Assert.Equal("1", facets.Count);

        facets.Count = "ref:test.SysEx.Length";
        document.ApplyNodeEdits(repeat, "Items", String.Empty, String.Empty, String.Empty, facets);
        Assert.True(repeat.Count is SchemaNodeRef);

        facets.Count = "3";
        document.ApplyNodeEdits(repeat, "Items", String.Empty, String.Empty, String.Empty, facets);
        Assert.Equal("3", NodeFacets.Read(repeat).Count);
    }

    [Fact]
    public void Apply_InvalidValueChangesNothing()
    {
        var document = new SchemaWorkspaceDocument(SchemaFileService.CreateDocument("test"), SchemaFormat.Json);
        var root = document.Schema.Members.OfType<EditGroup>().Single();
        var choice = document.AddChoice(root, "Pick", 2);
        var facets = NodeFacets.Read(choice);
        facets.NodeDef = "Body";
        facets.SelectedIndex = "abc";
        Assert.Throws<ArgumentException>(() => document.ApplyNodeEdits(choice, "Pick", String.Empty, String.Empty, String.Empty, facets));
        Assert.Null(choice.NodeDef);
        Assert.Equal("2", NodeFacets.Read(choice).SelectedIndex);
    }
}
