using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Xunit;
using JsonSerializer = Jacobi.BinarySerializer.Schema.Json.JsonSerializer;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaInstantiatorTests
{
    private const string Envelope = """
        {
          "name": "Env",
          "nodeDefs": [{ "name": "Body", "kind": "group" }],
          "members": [
            { "name": "Root", "kind": "group", "members": [
                { "name": "Id", "kind": "field", "datatype": "sys.uint8", "value": "240" },
                { "name": "Fixed", "kind": "repeat", "count": 2, "members": [{ "name": "F", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "Items", "kind": "repeat", "members": [
                    { "name": "V", "kind": "field", "datatype": "sys.uint8" },
                    { "name": "Sub", "kind": "repeat", "members": [{ "name": "W", "kind": "field", "datatype": "sys.uint8" }] }] },
                { "name": "Msg", "kind": "choice", "memberNodeDef": "Body" },
                { "name": "Mode", "kind": "choice", "members": [
                    { "name": "One", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] },
                    { "name": "Two", "kind": "group", "members": [{ "name": "B", "kind": "field", "datatype": "sys.uint8" }] }] }] },
            { "name": "BodyA", "kind": "group", "nodeDef": "Body", "members": [
                { "name": "Id", "kind": "field", "datatype": "sys.uint8" },
                { "name": "Parts", "kind": "repeat", "members": [{ "name": "P", "kind": "field", "datatype": "sys.uint8" }] }] },
            { "name": "BodyB", "kind": "group", "nodeDef": "Body", "members": [{ "name": "Value", "kind": "field", "datatype": "sys.uint8" }] }
          ]
        }
        """;

    [Fact]
    public void CreateRoot_ConstantsAreFixedAndRepeatsAreOpenOrFixed()
    {
        var root = CreateRoot(Envelope, "Root");

        var id = Assert.IsType<InstanceField>(root.Members[0]);
        Assert.Equal("240", id.Value);
        Assert.True(id.IsConstant);

        var fixedRepeat = Assert.IsType<InstanceRepeat>(root.Members[1]);
        Assert.False(fixedRepeat.IsOpen);
        Assert.Equal(2, fixedRepeat.Items.Count);
        Assert.Throws<InvalidOperationException>(() => fixedRepeat.Items[0].RemoveCommand.Execute(null));

        var items = Assert.IsType<InstanceRepeat>(root.Members[2]);
        Assert.True(items.IsOpen);
        Assert.Single(items.Items);
    }

    [Fact]
    public void NestedRepeats_ShowTheInstancePathOfEachItem()
    {
        var root = CreateRoot(Envelope, "Root");
        var items = Assert.IsType<InstanceRepeat>(root.Members[2]);
        items.AddItemCommand.Execute(null);
        var second = items.Items[1];
        var sub = Assert.IsType<InstanceRepeat>(second.Members[1]);
        sub.AddItemCommand.Execute(null);
        var inner = sub.Items[1];

        Assert.Equal("Root.Items[1]", second.DisplayPath);
        Assert.Equal("[1]", second.InstanceText);
        Assert.Equal("Root.Items[1].Sub[1]", inner.DisplayPath);
        Assert.Equal("[1][1]", inner.InstanceText);
        var field = Assert.IsType<InstanceField>(inner.Members[0]);
        Assert.Equal("Root.Items[1].Sub[1].W", field.DisplayPath);
        Assert.Equal("Root.Items.Sub.W", field.Path.Value);
    }

    [Fact]
    public void RemovingAnItem_RenumbersTheOthers()
    {
        var root = CreateRoot(Envelope, "Root");
        var items = Assert.IsType<InstanceRepeat>(root.Members[2]);
        items.AddItemCommand.Execute(null);
        items.AddItemCommand.Execute(null);
        var last = items.Items[2];
        var lastField = Assert.IsType<InstanceField>(last.Members[0]);

        items.Items[0].RemoveCommand.Execute(null);

        Assert.Equal(2, items.Items.Count);
        Assert.Same(last, items.Items[1]);
        Assert.Equal(1, last.Index);
        Assert.Equal("Root.Items[1].V", lastField.DisplayPath);
    }

    [Fact]
    public void OpenChoice_ListsTheConformingRootsAndSelectingOneCreatesItsBody()
    {
        var root = CreateRoot(Envelope, "Root");
        var msg = Assert.IsType<InstanceChoice>(root.Members[3]);

        Assert.True(msg.IsOpen);
        Assert.Equal(["Env.BodyA", "Env.BodyB"], msg.Options.Select(option => option.Name));
        Assert.Null(msg.Selected);

        msg.SelectedOption = msg.Options[0];
        var body = Assert.IsType<InstanceBody>(msg.Selected);
        Assert.Equal("Root.Msg.BodyA", body.DisplayPath);
        var parts = Assert.IsType<InstanceRepeat>(body.Members[1]);
        parts.AddItemCommand.Execute(null);
        Assert.Equal("Root.Msg.BodyA.Parts[1]", parts.Items[1].DisplayPath);
        // the body is a separate plan: its instance path starts anew, its path is relative to the body root
        Assert.Equal("[1]", parts.Items[1].InstanceText);
        Assert.Equal("BodyA.Parts.P", Assert.IsType<InstanceField>(parts.Items[1].Members[0]).Path.Value);

        msg.SelectedOption = msg.Options[1];
        Assert.Equal("Root.Msg.BodyB", Assert.IsType<InstanceBody>(msg.Selected).DisplayPath);
        Assert.Contains("Value", root.Fields().Select(field => field.Name));
        Assert.DoesNotContain("Parts", root.Fields().Select(field => field.Name));
    }

    [Fact]
    public void OpenChoice_AnyPlainGroupCanDeclareTheContract()
    {
        var root = CreateRoot("""
            { "name": "Env", "nodeDefs": [{ "name": "Body", "kind": "group" }],
              "members": [{ "name": "Root", "kind": "group", "members": [
                  { "name": "Msg", "kind": "choice", "memberNodeDef": "Body" },
                  { "name": "Nested", "kind": "group", "nodeDef": "Body", "members": [{ "name": "N", "kind": "field", "datatype": "sys.uint8" }] }] }] }
            """, "Root");
        var msg = Assert.IsType<InstanceChoice>(root.Members[0]);

        Assert.Equal(["Env.Nested"], msg.Options.Select(option => option.Name));
        msg.SelectedOption = msg.Options[0];
        Assert.Equal("Root.Msg.Nested", msg.Selected!.DisplayPath);
    }

    [Fact]
    public void OpenChoice_MatchesTheContractByDocumentQualifiedName()
    {
        var lib = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Lib", "nodeDefs": [{ "name": "Body", "kind": "group" }],
              "members": [
                { "name": "Own", "kind": "group", "nodeDef": "Body", "members": [{ "name": "X", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "Envelope", "kind": "group", "members": [{ "name": "Msg", "kind": "choice", "memberNodeDef": "Body" }] }] }
            """));
        var device = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Dev", "includes": [{ "schema": "Lib" }],
              "members": [
                { "name": "Qualified", "kind": "group", "nodeDef": "lib.body", "members": [{ "name": "Y", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "Other", "kind": "group", "members": [{ "name": "Z", "kind": "field", "datatype": "sys.uint8" }] }] }
            """));

        var root = SchemaInstantiator.Create(lib, [lib, device]).CreateRoot("Envelope");

        // 'Body' in Lib is Lib.Body; the nodeDef of Dev.Qualified names the document explicitly; Dev includes Lib so it is part of the set
        Assert.Equal(["Dev.Qualified", "Lib.Own"], Assert.IsType<InstanceChoice>(root.Members[0]).Options.Select(option => option.Name));
        var withDevice = SchemaInstantiator.Create(device, [lib, device]);
        var envelope = Assert.IsType<InstanceGroup>(withDevice.Create((EditGroup)lib.Members[1]));
        Assert.Equal(["Dev.Qualified", "Lib.Own"], Assert.IsType<InstanceChoice>(envelope.Members[0]).Options.Select(option => option.Name));
    }

    [Fact]
    public void OpenChoice_FindsBodiesInOpenDocumentsThatIncludeTheSchema()
    {
        var vendor = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Vendor", "nodeDefs": [{ "name": "messageBody", "kind": "group" }],
              "members": [{ "name": "Message", "kind": "group", "members": [{ "name": "Data", "kind": "choice", "memberNodeDef": "messageBody" }] }] }
            """));
        var device = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Dev", "includes": [{ "schema": "Vendor" }],
              "members": [{ "name": "AddressMap", "kind": "group", "nodeDef": "vendor.messageBody", "members": [{ "name": "Y", "kind": "field", "datatype": "sys.uint8" }] }] }
            """));
        var unrelated = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Broken", "includes": [{ "schema": "Vendor" }, { "schema": "NotLoaded" }], "members": [] }
            """));

        var root = SchemaInstantiator.Create(vendor, [vendor, device, unrelated]).CreateRoot("Message");

        Assert.Equal(["Dev.AddressMap"], Assert.IsType<InstanceChoice>(root.Members[0]).Options.Select(option => option.Name));
    }

    [Fact]
    public void Choice_ClosedAlternativesAreSelectedByTheUser()
    {
        var root = CreateRoot(Envelope, "Root");
        var mode = Assert.IsType<InstanceChoice>(root.Members[4]);

        Assert.False(mode.IsOpen);
        Assert.True(mode.CanSelect);
        Assert.Equal(["One", "Two"], mode.Options.Select(option => option.Name));
        mode.SelectedOption = mode.Options[1];
        Assert.Equal("Root.Mode.Two.B", Assert.IsType<InstanceField>(Assert.IsType<InstanceGroup>(mode.Selected).Members[0]).DisplayPath);
    }

    [Fact]
    public void Create_MissingInclude_Throws()
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Dev", "includes": [{ "schema": "Missing" }],
              "members": [{ "name": "Root", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] }] }
            """));

        var exception = Assert.Throws<InvalidOperationException>(() => SchemaInstantiator.Create(document, [document]));

        Assert.Contains("'Missing'", exception.Message);
    }

    [Fact]
    public void Create_UsesTheIncludedDocumentsOfTheWorkspace()
    {
        var other = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Lib", "nodeDefs": [{ "name": "Body", "kind": "group" }],
              "members": [{ "name": "Extra", "kind": "group", "nodeDef": "Body", "members": [{ "name": "X", "kind": "field", "datatype": "sys.uint8" }] }] }
            """));
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Dev", "includes": [{ "schema": "Lib" }],
              "members": [{ "name": "Root", "kind": "group", "members": [{ "name": "Msg", "kind": "choice", "memberNodeDef": "Lib.Body" }] }] }
            """));

        var root = SchemaInstantiator.Create(document, [document, other]).CreateRoot("Root");

        Assert.Equal(["Lib.Extra"], Assert.IsType<InstanceChoice>(root.Members[0]).Options.Select(option => option.Name));
    }

    [Fact]
    public void InstantiateCommand_WorksOnAnyGroupButNotOnAField()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        var root = (EditGroup)schema.Members.Single();
        viewModel.SelectedItem = root;
        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Repeat);
        var repeat = (EditRepeat)root.Members.Single();
        viewModel.SelectedItem = repeat;
        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
        var field = repeat.Members.Single();

        viewModel.SelectedItem = field;
        Assert.False(viewModel.InstantiateSchemaCommand.CanExecute(null));

        viewModel.SelectedItem = repeat;
        Assert.True(viewModel.InstantiateSchemaCommand.CanExecute(null));
        viewModel.InstantiateSchemaCommand.Execute(null);
        var dialog = Assert.IsType<InstantiateSchemaViewModel>(viewModel.InstantiateDialog);
        var instance = Assert.IsType<InstanceRepeat>(dialog.Root);
        Assert.Equal(repeat.Name, instance.Name);
        Assert.Equal($"{root.Name}.{repeat.Name}", instance.Path.Value);

        dialog.ConfirmCommand.Execute(null);
        Assert.Null(viewModel.InstantiateDialog);
        Assert.Same(instance, viewModel.LastInstance);

        viewModel.SelectedItem = root;
        viewModel.InstantiateSchemaCommand.Execute(null);
        Assert.IsType<InstanceGroup>(viewModel.InstantiateDialog!.Root);
    }

    [Fact]
    public void InstantiateCommand_ShowsAnUnloadedIncludeInTheDialog()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.SelectedDocument!.AddInclude("schema");
        viewModel.SelectedItem = viewModel.SelectedDocument!.Schema.Members.Single();

        viewModel.InstantiateSchemaCommand.Execute(null);

        var dialog = Assert.IsType<InstantiateSchemaViewModel>(viewModel.InstantiateDialog);
        Assert.Null(dialog.Root);
        Assert.True(dialog.HasError);
        Assert.Contains("not loaded", dialog.Error);
        dialog.ConfirmCommand.Execute(null);
        Assert.Null(viewModel.InstantiateDialog);
        Assert.Null(viewModel.LastInstance);
    }


    private static InstanceGroup CreateRoot(string json, string root)
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(json));
        return SchemaInstantiator.Create(document, [document]).CreateRoot(root);
    }
}
