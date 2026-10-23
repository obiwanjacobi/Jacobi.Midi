using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class NodeArrangeTests
{
    // a document whose root group holds the fields a, b and the group g (with the field c)
    private static (SchemaWorkspaceDocument Document, EditGroup Root) CreateDocument()
    {
        var document = new SchemaWorkspaceDocument(SchemaFileService.CreateDocument("Test"), SchemaFormat.Xml);
        var root = (EditGroup)document.Schema.Members[0];
        document.AddField(root, "a", "sys.uint8", String.Empty);
        document.AddField(root, "b", "sys.uint8", String.Empty);
        var group = document.AddGroup(root, "g");
        document.AddField(group, "c", "sys.uint8", String.Empty);
        return (document, root);
    }

    private static IEnumerable<string> Names(EditGroup group) => group.Members.Select(member => member.Name);

    [Fact]
    public void MoveNode_OrdersTheNodeInItsParent()
    {
        var (document, root) = CreateDocument();
        var b = root.Members[1];
        var revision = document.Revision;

        Assert.False(document.CanMoveNode(root.Members[0], -1));
        Assert.False(document.CanMoveNode(root.Members[2], 1));
        Assert.False(document.CanMoveNode(document.Schema, -1));

        document.MoveNode(b, -1);
        Assert.Equal(["b", "a", "g"], Names(root));
        document.MoveNode(b, 2);
        Assert.Equal(["a", "g", "b"], Names(root));
        Assert.Equal(revision + 2, document.Revision);
        Assert.Throws<InvalidOperationException>(() => document.MoveNode(b, 1));
    }

    [Fact]
    public void MoveNode_OrdersTheNodeDefs()
    {
        var (document, _) = CreateDocument();
        var first = document.AddNodeDef(SchemaNodeKind.Group, "First");
        var second = document.AddNodeDef(SchemaNodeKind.Field, "Second");

        document.MoveNode(second, -1);
        Assert.Equal([second, first], document.Schema.NodeDefs);
    }

    [Fact]
    public void IndentNode_MovesTheNodeIntoTheGroupBeforeIt()
    {
        var (document, root) = CreateDocument();
        var (a, b, g) = (root.Members[0], root.Members[1], (EditGroup)root.Members[2]);
        document.MoveNode(g, -1);

        // a field before it is not a group
        Assert.False(document.CanIndentNode(a));
        Assert.True(document.CanIndentNode(b));
        document.IndentNode(b);
        Assert.Equal(["a", "g"], Names(root));
        Assert.Equal(["c", "b"], Names(g));
        Assert.Same(g, b.Parent);

        // an open choice has no explicit members
        var open = document.AddChoice(root, "open", 0);
        open.MemberNodeDef = new SchemaName("Body");
        var after = document.AddField(root, "after", "sys.uint8", String.Empty);
        Assert.False(document.CanIndentNode(after));
        Assert.Throws<InvalidOperationException>(() => document.IndentNode(after));
    }

    [Fact]
    public void OutdentNode_MovesTheNodeOutOfItsParentRightAfterIt()
    {
        var (document, root) = CreateDocument();
        var g = (EditGroup)root.Members[2];
        var c = g.Members[0];
        document.MoveNode(g, -2);

        // a top-level member would leave the document
        Assert.False(document.CanOutdentNode(root));
        Assert.True(document.CanOutdentNode(c));
        document.OutdentNode(c);
        Assert.Equal(["g", "c", "a", "b"], Names(root));
        Assert.Empty(g.Members);
        Assert.Same(root, c.Parent);

        // a member of a top-level node def would become a node def
        var definition = (EditGroup)document.AddNodeDef(SchemaNodeKind.Group, "Def");
        var member = document.AddField(definition, "m", "sys.uint8", String.Empty);
        Assert.False(document.CanOutdentNode(member));
        Assert.False(document.CanOutdentNode(definition));
        var nested = document.AddGroup(definition, "nested");
        var inner = document.AddField(nested, "inner", "sys.uint8", String.Empty);
        document.OutdentNode(inner);
        Assert.Equal(["m", "nested", "inner"], Names(definition));
    }

    [Fact]
    public void ChangeNodeKind_KeepsTheSharedValuesAndTheMembers()
    {
        var (document, root) = CreateDocument();
        var g = (EditGroup)root.Members[2];
        g.NodeDef = new SchemaName("Shape");
        g.Properties.Add(new EditProperty { Name = "description", Value = "the g" });
        var c = g.Members[0];

        var repeat = Assert.IsType<EditRepeat>(document.ChangeNodeKind(g, SchemaNodeKind.Repeat));
        Assert.Same(repeat, root.Members[2]);
        Assert.Same(root, repeat.Parent);
        Assert.Equal("g", repeat.Name);
        Assert.Equal(new SchemaName("Shape"), repeat.NodeDef);
        Assert.Equal("description", Assert.Single(repeat.Properties).Name);
        Assert.Same(c, Assert.Single(repeat.Members));
        Assert.Same(repeat, c.Parent);
        Assert.True(repeat.Count is 1);

        var choice = Assert.IsType<EditChoice>(document.ChangeNodeKind(repeat, SchemaNodeKind.Choice));
        Assert.Same(choice, c.Parent);
        Assert.Same(choice, document.ChangeNodeKind(choice, SchemaNodeKind.Choice));
        Assert.Throws<NotSupportedException>(() => document.ChangeNodeKind(choice, SchemaNodeKind.Field));
        Assert.Throws<NotSupportedException>(() => document.ChangeNodeKind(document.Schema, SchemaNodeKind.Group));

        // a node def is replaced in the node defs
        var definition = (EditGroup)document.AddNodeDef(SchemaNodeKind.Group, "Def");
        var changed = document.ChangeNodeKind(definition, SchemaNodeKind.Repeat);
        Assert.Same(changed, Assert.Single(document.Schema.NodeDefs));
        Assert.Null(changed.Parent);
    }

    [Fact]
    public void GetLostByKindChange_ListsTheValuesThatDifferFromTheDefaults()
    {
        var (document, root) = CreateDocument();
        var repeat = document.AddRepeat(root, "r", count: 1);
        Assert.Empty(SchemaWorkspaceDocument.GetLostByKindChange(repeat, SchemaNodeKind.Group));

        repeat.Count = 4;
        repeat.ValueProcessors.Add(new EditProcessorRef { Processor = new SchemaName("sys.x") });
        Assert.Equal(["count", "value processors"], SchemaWorkspaceDocument.GetLostByKindChange(repeat, SchemaNodeKind.Choice));
        Assert.Empty(SchemaWorkspaceDocument.GetLostByKindChange(repeat, SchemaNodeKind.Repeat));

        var choice = document.AddChoice(root, "ch", selectedIndex: 0);
        Assert.Empty(SchemaWorkspaceDocument.GetLostByKindChange(choice, SchemaNodeKind.Group));
        choice.SelectedIndex = 2;
        choice.MemberNodeDef = new SchemaName("Body");
        Assert.Equal(["selected index", "member node def"], SchemaWorkspaceDocument.GetLostByKindChange(choice, SchemaNodeKind.Repeat));
    }

    [Fact]
    public void ArrangedNodes_SurviveAnXmlRoundTrip()
    {
        var source = SchemaFileService.Serialize(CreateDocument().Document.Schema, SchemaFormat.Xml);
        var schema = SchemaFileService.Parse(source, SchemaFormat.Xml);
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Xml);
        var root = (EditGroup)schema.Members[0];

        document.MoveNode(root.Members[2], -1);
        document.IndentNode(root.Members[2]);
        document.ChangeNodeKind((EditGroup)root.Members[1], SchemaNodeKind.Repeat);

        var restored = SchemaFileService.Parse(SchemaFileService.Serialize(schema, SchemaFormat.Xml), SchemaFormat.Xml);
        var restoredRoot = (EditGroup)restored.Members[0];
        Assert.Equal(["a", "g"], Names(restoredRoot));
        var g = Assert.IsType<EditRepeat>(restoredRoot.Members[1]);
        Assert.Equal(["c", "b"], Names(g));
    }

    [Fact]
    public void Commands_ArrangeTheSelectedNodeAndKeepItSelected()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        var root = (EditGroup)schema.Members[0];

        // the document root cannot be arranged
        Assert.False(viewModel.CanArrange);
        Assert.False(viewModel.MoveNodeUpCommand.CanExecute(null));

        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Group);
        var group = Assert.IsType<EditGroup>(viewModel.SelectedItem);
        viewModel.SelectedItem = root;
        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
        var field = Assert.IsType<EditField>(viewModel.SelectedItem);
        Assert.True(viewModel.CanArrange);
        Assert.False(viewModel.CanChangeNodeType);
        Assert.False(viewModel.ChangeNodeKindCommand.CanExecute(SchemaNodeKind.Group));

        Assert.False(viewModel.MoveNodeDownCommand.CanExecute(null));
        viewModel.MoveNodeUpCommand.Execute(null);
        Assert.Same(field, viewModel.SelectedItem);
        Assert.Equal([field, group], root.Members);

        viewModel.MoveNodeDownCommand.Execute(null);
        viewModel.IndentNodeCommand.Execute(null);
        Assert.Same(field, viewModel.SelectedItem);
        Assert.Same(group, field.Parent);
        Assert.True(viewModel.OutdentNodeCommand.CanExecute(null));
        viewModel.OutdentNodeCommand.Execute(null);
        Assert.Same(root, field.Parent);
        Assert.Equal([group, field], root.Members);
    }

    [Fact]
    public void ChangeNodeKindCommand_ConfirmsWhenValuesAreLost()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var root = (EditGroup)viewModel.SelectedDocument!.Schema.Members[0];
        viewModel.SelectedItem = root;
        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Repeat);
        var repeat = Assert.IsType<EditRepeat>(viewModel.SelectedItem);
        Assert.True(viewModel.CanChangeNodeType);
        Assert.False(viewModel.ChangeNodeKindCommand.CanExecute(SchemaNodeKind.Repeat));

        // nothing is lost: changed at once
        viewModel.ChangeNodeKindCommand.Execute(SchemaNodeKind.Choice);
        Assert.Null(viewModel.ConfirmDialog);
        var choice = Assert.IsType<EditChoice>(viewModel.SelectedItem);
        Assert.Same(choice, root.Members[^1]);

        // a selected index is lost: confirmed first
        choice.SelectedIndex = 3;
        viewModel.ChangeNodeKindCommand.Execute(SchemaNodeKind.Group);
        Assert.NotNull(viewModel.ConfirmDialog);
        viewModel.ConfirmDialog!.CancelCommand.Execute(null);
        Assert.Same(choice, viewModel.SelectedItem);

        viewModel.ChangeNodeKindCommand.Execute(SchemaNodeKind.Group);
        viewModel.ConfirmDialog!.ConfirmCommand.Execute(null);
        Assert.Equal(SchemaNodeKind.Group, Assert.IsType<EditGroup>(viewModel.SelectedItem).Kind);
        Assert.Same(viewModel.SelectedItem, root.Members[^1]);
    }
}
