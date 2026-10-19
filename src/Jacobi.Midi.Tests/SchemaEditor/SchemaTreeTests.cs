using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.SchemaTree;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaTreeTests
{
    [Fact]
    public void Initialize_BuildsRootsAndChildren()
    {
        var (schema, root, field) = CreateDocument();
        var tree = new SchemaTreeViewModel();

        tree.Initialize(schema);

        var rootItem = Assert.Single(tree.SchemaNodes);
        Assert.Same(root, rootItem.Node);
        var fieldItem = Assert.Single(rootItem.Children);
        Assert.Same(field, fieldItem.Node);
        Assert.Same(rootItem, fieldItem.Parent);
        Assert.Null(tree.SelectedNode);
    }

    [Fact]
    public void Initialize_WithNull_ClearsTree()
    {
        var (schema, _, _) = CreateDocument();
        var tree = new SchemaTreeViewModel();
        tree.Initialize(schema);

        tree.Initialize(null);

        Assert.Empty(tree.SchemaNodes);
    }

    [Fact]
    public void MemberChanges_AreReflectedInTree()
    {
        var (schema, root, field) = CreateDocument();
        var tree = new SchemaTreeViewModel();
        tree.Initialize(schema);
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        var rootItem = Assert.Single(tree.SchemaNodes);

        var group = document.AddGroup(root, "Header");
        var nested = document.AddField(group, "Nested", "sys.uint8", String.Empty);
        root.Members.Move(1, 0);

        Assert.Equal(new EditNode[] { group, field }, rootItem.Children.Select(item => item.Node));
        Assert.Same(nested, Assert.Single(rootItem.Children[0].Children).Node);

        root.Members.Remove(field);
        Assert.Same(group, Assert.Single(rootItem.Children).Node);

        schema.Members.Add(new EditGroup(schema) { Name = "Second", Members = [], Processors = [], Properties = [] });
        Assert.Equal(2, tree.SchemaNodes.Count);
    }

    [Fact]
    public void Rename_UpdatesDisplayName()
    {
        var (schema, _, field) = CreateDocument();
        var tree = new SchemaTreeViewModel();
        tree.Initialize(schema);
        var item = Assert.Single(tree.SchemaNodes[0].Children);
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        field.Name = "Renamed";

        Assert.Contains(nameof(SchemaTreeItem.DisplayName), changed);
        Assert.StartsWith("Renamed", item.DisplayName);
    }

    [Fact]
    public void Select_ExpandsAncestorsAndSelectsItem()
    {
        var (schema, root, _) = CreateDocument();
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        var group = document.AddGroup(root, "Header");
        var nested = document.AddField(group, "Nested", "sys.uint8", String.Empty);
        var tree = new SchemaTreeViewModel();
        tree.Initialize(schema);

        tree.Select(nested);

        Assert.Same(nested, tree.SelectedNode?.Node);
        Assert.True(tree.SchemaNodes[0].IsExpanded);
        Assert.True(tree.SchemaNodes[0].Children[1].IsExpanded);

        tree.Select(null);
        Assert.Null(tree.SelectedNode);
    }

    [Fact]
    public void Reinitialize_DetachesPreviousDocument()
    {
        var (first, firstRoot, _) = CreateDocument();
        var (second, _, _) = CreateDocument();
        var tree = new SchemaTreeViewModel();
        tree.Initialize(first);
        var oldRootItem = tree.SchemaNodes[0];

        tree.Initialize(second);
        firstRoot.Members.Add(new EditField(firstRoot) { Name = "Late", Processors = [], Properties = [] });
        first.Members.Add(new EditGroup(first) { Name = "Late", Members = [], Processors = [], Properties = [] });

        Assert.Single(oldRootItem.Children);
        Assert.Single(tree.SchemaNodes);
        Assert.Same(second.Members[0], tree.SchemaNodes[0].Node);
    }

    private static (EditDocument Schema, EditGroup Root, EditField Field) CreateDocument()
    {
        var schema = SchemaFileService.CreateDocument("Tree");
        var root = Assert.IsType<EditGroup>(Assert.Single(schema.Members));
        var field = new SchemaWorkspaceDocument(schema, SchemaFormat.Json).AddField(root, "Field", "sys.uint8", String.Empty);
        return (schema, root, field);
    }
}
