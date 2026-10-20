using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.SchemaTree;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaTreeTests
{
    [Fact]
    public void ChangingDocument_ClearsSelection()
    {
        var first = SchemaFileService.CreateDocument("First");
        var tree = new SchemaTreeViewModel { Document = first };
        tree.SelectedItem = first.Members[0];

        tree.Document = SchemaFileService.CreateDocument("Second");
        Assert.Null(tree.SelectedItem);

        tree.SelectedItem = tree.Document.Members[0];
        tree.Document = null;
        Assert.Null(tree.SelectedItem);
        Assert.Null(tree.Items);
    }

    [Fact]
    public void Items_IsTheRootWithTheMembersBelowIt()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel { Document = document };

        var root = Assert.IsType<SchemaTreeRoot>(Assert.Single(tree.Items!.Cast<object>()));
        Assert.Same(document, root.Document);
        Assert.Same(document.Members, root.Children);
    }

    [Fact]
    public void ToTreeItem_MapsTheDocumentToTheRoot()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel { Document = document };

        Assert.Same(tree.Root, tree.ToTreeItem(document));
        Assert.Same(document, SchemaTreeViewModel.ToItem(tree.Root));
        Assert.Same(document.Members[0], tree.ToTreeItem(document.Members[0]));
        Assert.Null(SchemaTreeViewModel.ToItem(null));
    }

    [Fact]
    public void GetPath_LocatesMembersBelowTheRoot()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel { Document = document };
        var group = (EditGroup)document.Members[0];
        var nested = new EditField(group) { Name = "nested", Processors = [], Properties = [] };
        group.Members.Add(nested);

        Assert.Empty(tree.GetPath(document)!);
        Assert.Equal<object>([tree.Root!], tree.GetPath(group)!);
        Assert.Equal<object>([tree.Root!, group], tree.GetPath(nested)!);
    }

    [Fact]
    public void GetPath_IsNullForItemsThatAreNotInTheTree()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel { Document = document };
        var definition = new EditGroup(null) { Name = "def", Members = [], Processors = [], Properties = [] };
        var nested = new EditField(definition) { Name = "nested", Processors = [], Properties = [] };
        definition.Members.Add(nested);
        document.NodeDefs.Add(definition);

        Assert.Null(tree.GetPath(definition));
        Assert.Null(tree.GetPath(nested));
        Assert.Null(tree.GetPath(SchemaFileService.CreateDocument("Other")));
        Assert.Null(tree.GetPath(new EditDocumentRef { Schema = "midi" }));
    }
}
