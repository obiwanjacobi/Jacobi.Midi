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
    }

    [Fact]
    public void ViewSwitch_ChangesRootItems()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel { Document = document };

        Assert.Same(document.Members, tree.Items);
        tree.IsDocumentView = true;
        Assert.False(tree.IsMembersView);
        var categories = Assert.IsAssignableFrom<IEnumerable<SchemaTreeCategory>>(tree.Items);
        Assert.Equal(["Includes", "Node Defs", "Data Type Defs", "Processor Defs", "Processors", "Properties"],
            categories.Select(category => category.Title));
        Assert.Same(document.NodeDefs, categories.Single(category => category.Title == "Node Defs").Items);
    }

    [Fact]
    public void GetPath_LocatesItemsInTheirView()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel { Document = document };
        var group = (EditGroup)document.Members[0];
        var definition = new EditGroup(null) { Name = "def", Members = [], Processors = [], Properties = [] };
        var nested = new EditField(definition) { Name = "nested", Processors = [], Properties = [] };
        definition.Members.Add(nested);
        document.NodeDefs.Add(definition);
        var include = new EditDocumentRef { Schema = "midi" };
        document.Includes.Add(include);

        Assert.False(SchemaTreeViewModel.IsDocumentItem(group));
        Assert.Empty(tree.GetPath(group)!);

        Assert.True(SchemaTreeViewModel.IsDocumentItem(nested));
        Assert.Equal<object>([tree.Categories[1], definition], tree.GetPath(nested)!);

        Assert.True(SchemaTreeViewModel.IsDocumentItem(include));
        Assert.Equal<object>([tree.Categories[0]], tree.GetPath(include)!);

        Assert.Null(tree.GetPath(new EditDocumentRef { Schema = "other" }));
    }
}
