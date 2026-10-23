using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.SchemaTree;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaTreeTests
{
    [Fact]
    public void RemovingTheDocument_ClearsItsSelection()
    {
        var first = SchemaFileService.CreateDocument("First");
        var second = SchemaFileService.CreateDocument("Second");
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([first, second]);

        tree.SelectedItem = first.Members[0];
        tree.SetDocuments([first]);
        Assert.Same(first.Members[0], tree.SelectedItem);

        tree.SetDocuments([second]);
        Assert.Null(tree.SelectedItem);

        tree.SelectedItem = tree.Roots[0];
        tree.SetDocuments([]);
        Assert.Null(tree.SelectedItem);
        Assert.Empty(tree.Items.Cast<object>());
    }

    [Fact]
    public void Items_AreTheRootsWithTheNodeDefsItemAndTheMembersBelowThem()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([document]);

        var root = Assert.IsType<SchemaTreeRoot>(Assert.Single(tree.Items.Cast<object>()));
        Assert.Same(document, root.Document);
        Assert.Equal([NodeDefsItem.Get(document), .. document.Members], root.Children.Cast<object>());
        Assert.Same(document, NodeDefsItem.Get(document).Document);
    }

    [Fact]
    public void DocumentTreeChildren_FollowTheMembers()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var children = DocumentTreeChildren.Get(document);
        Assert.Same(children, DocumentTreeChildren.Get(document));
        var added = new EditField(document) { Name = "added", Processors = [], Properties = [] };

        document.Members.Insert(0, added);
        Assert.Equal([NodeDefsItem.Get(document), .. document.Members], children);
        document.Members.Move(0, 1);
        Assert.Equal([NodeDefsItem.Get(document), .. document.Members], children);
        document.Members[1] = new EditField(document) { Name = "replaced", Processors = [], Properties = [] };
        Assert.Equal([NodeDefsItem.Get(document), .. document.Members], children);
        document.Members.RemoveAt(0);
        Assert.Equal([NodeDefsItem.Get(document), .. document.Members], children);
        document.Members.Clear();
        Assert.Equal([NodeDefsItem.Get(document)], children);
    }

    [Fact]
    public void SetDocuments_KeepsTheRootsOfDocumentsThatStay()
    {
        var first = SchemaFileService.CreateDocument("First");
        var second = SchemaFileService.CreateDocument("Second");
        var third = SchemaFileService.CreateDocument("Third");
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([first, third]);
        var firstRoot = tree.Roots[0];

        var added = tree.SetDocuments([first, second, third]);

        Assert.Equal([first, second, third], tree.Roots.Select(root => root.Document));
        Assert.Same(firstRoot, tree.Roots[0]);
        Assert.Same(tree.Roots[1], Assert.Single(added));
    }

    [Fact]
    public void ToTreeItem_MapsADocumentToItsRoot()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([document]);

        Assert.Same(tree.Roots[0], tree.ToTreeItem(document));
        Assert.Same(document, SchemaTreeViewModel.ToItem(tree.Roots[0]));
        Assert.Same(document.Members[0], tree.ToTreeItem(document.Members[0]));
        Assert.Null(SchemaTreeViewModel.ToItem(null));
    }

    [Fact]
    public void GetPath_LocatesMembersBelowTheirRoot()
    {
        var first = SchemaFileService.CreateDocument("First");
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([first, document]);
        var group = (EditGroup)document.Members[0];
        var nested = new EditField(group) { Name = "nested", Processors = [], Properties = [] };
        group.Members.Add(nested);

        Assert.Empty(tree.GetPath(document)!);
        Assert.Equal<object>([tree.Roots[1]], tree.GetPath(group)!);
        Assert.Equal<object>([tree.Roots[1], group], tree.GetPath(nested)!);
    }

    [Fact]
    public void GetPath_LocatesNodeDefsBelowTheNodeDefsItem()
    {
        var first = SchemaFileService.CreateDocument("First");
        var document = SchemaFileService.CreateDocument("Doc");
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([first, document]);
        var definition = new EditGroup(null) { Name = "def", Members = [], Processors = [], Properties = [] };
        var nested = new EditField(definition) { Name = "nested", Processors = [], Properties = [] };
        definition.Members.Add(nested);
        document.NodeDefs.Add(definition);
        var nodeDefs = NodeDefsItem.Get(document);

        Assert.Equal<object>([tree.Roots[1]], tree.GetPath(nodeDefs)!);
        Assert.Equal<object>([tree.Roots[1], nodeDefs], tree.GetPath(definition)!);
        Assert.Equal<object>([tree.Roots[1], nodeDefs, definition], tree.GetPath(nested)!);
        Assert.Same(nodeDefs, tree.ToTreeItem(nodeDefs));
        Assert.Same(nodeDefs, SchemaTreeViewModel.ToItem(nodeDefs));
    }

    [Fact]
    public void GetPath_IsNullForItemsThatAreNotInTheTree()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var other = SchemaFileService.CreateDocument("Other");
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([document]);
        // a node def of a document that is not shown
        var definition = new EditGroup(null) { Name = "def", Members = [], Processors = [], Properties = [] };
        other.NodeDefs.Add(definition);

        Assert.Null(tree.GetPath(definition));
        Assert.Null(tree.GetPath(NodeDefsItem.Get(other)));
        Assert.Null(tree.GetPath(other));
        Assert.Null(tree.GetPath(new EditDocumentRef { Schema = "midi" }));
    }

    [Fact]
    public void RemovingTheDocument_ClearsTheSelectionOfItsNodeDefs()
    {
        var document = SchemaFileService.CreateDocument("Doc");
        var definition = new EditGroup(null) { Name = "def", Members = [], Processors = [], Properties = [] };
        document.NodeDefs.Add(definition);
        var tree = new SchemaTreeViewModel();
        tree.SetDocuments([document]);

        tree.SelectedItem = definition;
        tree.SetDocuments([]);
        Assert.Null(tree.SelectedItem);

        tree.SetDocuments([document]);
        tree.SelectedItem = NodeDefsItem.Get(document);
        tree.SetDocuments([]);
        Assert.Null(tree.SelectedItem);
    }

    [Fact]
    public void TreeDocuments_KeepPinnedDocumentsWhenANewDocumentIsSelected()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var first = viewModel.SelectedDocument!;
        Assert.Equal([first.Schema], viewModel.TreeDocuments);

        // an unpinned document is replaced by a document that is not in the tree
        viewModel.NewDocumentCommand.Execute(null);
        var second = viewModel.SelectedDocument!;
        Assert.Equal([second.Schema], viewModel.TreeDocuments);

        // a pinned document stays
        second.IsPinned = true;
        viewModel.NewDocumentCommand.Execute(null);
        var third = viewModel.SelectedDocument!;
        Assert.Equal([second.Schema, third.Schema], viewModel.TreeDocuments);

        // selecting a document in the tree does not change the tree
        viewModel.SelectedDocument = second;
        Assert.Equal([second.Schema, third.Schema], viewModel.TreeDocuments);

        // changing the pin state does not change the tree
        second.IsPinned = false;
        Assert.Equal([second.Schema, third.Schema], viewModel.TreeDocuments);
        second.IsPinned = true;
        Assert.Equal([second.Schema, third.Schema], viewModel.TreeDocuments);

        // the unpinned documents are replaced, the pinned stay
        viewModel.SelectedDocument = first;
        Assert.Equal([first.Schema, second.Schema], viewModel.TreeDocuments);
    }

    [Fact]
    public void SelectingANodeDefOfAPinnedDocument_SelectsThatDocument()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var first = viewModel.SelectedDocument!;
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Group);
        var definition = first.Schema.NodeDefs[0];
        first.IsPinned = true;
        viewModel.NewDocumentCommand.Execute(null);
        var second = viewModel.SelectedDocument!;

        viewModel.SelectedItem = definition;
        Assert.Same(first, viewModel.SelectedDocument);
        Assert.Same(definition, viewModel.SelectedItem);
        Assert.Equal($"{first.Schema.Name}/NodeDefs/{definition.Name}", viewModel.SelectedItemPath);

        viewModel.SelectedItem = NodeDefsItem.Get(second.Schema);
        Assert.Same(second, viewModel.SelectedDocument);
        Assert.Same(NodeDefsItem.Get(second.Schema), viewModel.SelectedItem);
    }

    [Fact]
    public void SelectingADocument_SelectsItsRoot()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var first = viewModel.SelectedDocument!;
        viewModel.NewDocumentCommand.Execute(null);

        viewModel.SelectedDocument = first;

        Assert.Same(first.Schema, viewModel.SelectedItem);
        Assert.True(viewModel.IsDocumentSelected);
    }

    [Fact]
    public void SwitchingTabs_SelectsTheFirstInTheTree()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var first = viewModel.SelectedDocument!;
        first.IsPinned = true;
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.SelectedItem = viewModel.SelectedDocument!.Schema.Members[0];

        // the first pre-loaded assembly is selected: nothing of the schema tab remains in the node editor
        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;
        Assert.Same(viewModel.OpenedAssemblies.Assemblies[0], Assert.IsType<LoadedAssembly>(viewModel.SelectedAssemblyItem));
        Assert.Null(viewModel.SelectedItem);
        Assert.Null(viewModel.ActiveDocument);
        Assert.Equal(String.Empty, viewModel.SelectedItemPath);
        Assert.False(viewModel.CanPin);
        Assert.False(viewModel.CanAdd);

        viewModel.SelectedLeftTab = MainViewModel.SchemaLeftTab;
        Assert.Same(first.Schema, viewModel.SelectedItem);
        Assert.Same(first, viewModel.SelectedDocument);
    }

    [Fact]
    public void SelectingAnItemOfAPinnedDocument_SelectsThatDocument()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var pinned = viewModel.SelectedDocument!;
        pinned.IsPinned = true;
        viewModel.NewDocumentCommand.Execute(null);

        var node = pinned.Schema.Members[0];
        viewModel.SelectedItem = node;

        Assert.Same(pinned, viewModel.SelectedDocument);
        Assert.Same(node, viewModel.SelectedItem);
        Assert.Same(pinned, viewModel.ActiveDocument);
    }
}
