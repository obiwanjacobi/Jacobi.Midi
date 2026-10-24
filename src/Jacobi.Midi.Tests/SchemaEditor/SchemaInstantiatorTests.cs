using System.Reflection;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Jacobi.Midi.Tests.Schema;
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

    private const string Vendor = """
        { "name": "Vendor", "nodeDefs": [{ "name": "messageBody", "kind": "group" }],
          "members": [{ "name": "Message", "kind": "group", "members": [{ "name": "Data", "kind": "choice", "memberNodeDef": "messageBody" }] }] }
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
    public void OpenChoice_FindsBodiesInDocumentsThatDoNotIncludeTheSchema()
    {
        var vendor = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Vendor));
        // the contract is referenced by its qualified name only
        var device = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Dev",
              "members": [{ "name": "AddressMap", "kind": "group", "nodeDef": "vendor.messageBody", "members": [{ "name": "Y", "kind": "field", "datatype": "sys.uint8" }] }] }
            """));
        var unrelated = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Other", "members": [{ "name": "Root", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] }] }
            """));

        var instantiator = SchemaInstantiator.Create(vendor, [vendor, device, unrelated]);
        var root = instantiator.CreateRoot("Message");

        Assert.Equal(["Dev.AddressMap"], Assert.IsType<InstanceChoice>(root.Members[0]).Options.Select(option => option.Name));
        Assert.Equal(["Vendor", "Dev", "Other"], instantiator.Documents.Select(document => document.Name));
        Assert.Empty(instantiator.Warnings);
    }

    [Fact]
    public void Create_ReportsAnUnrelatedDocumentThatDoesNotCompileAsLeftOut()
    {
        var vendor = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Vendor));
        var broken = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Broken", "members": [{ "name": "Map", "kind": "group", "nodeDef": "unknownBody", "members": [] }] }
            """));

        var instantiator = SchemaInstantiator.Create(vendor, [vendor, broken]);

        Assert.Equal(["Vendor"], instantiator.Documents.Select(document => document.Name));
        var warning = Assert.Single(instantiator.Warnings);
        Assert.Contains("'Broken' is left out", warning);
        Assert.Contains("unknownBody", warning);
    }

    [Fact]
    public void Create_ReportsADependentWithAnUnloadedIncludeAsLeftOut()
    {
        var vendor = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Vendor));
        var unloaded = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Partial", "includes": [{ "schema": "Vendor" }, { "schema": "NotLoaded" }], "members": [] }
            """));

        var instantiator = SchemaInstantiator.Create(vendor, [vendor, unloaded]);

        var warning = Assert.Single(instantiator.Warnings);
        Assert.Contains("'Partial' is left out", warning);
        Assert.Contains("'NotLoaded'", warning);
    }

    [Fact]
    public void Create_LeavesOutOnlyTheDependentThatDoesNotCompileAndReportsIt()
    {
        var vendor = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Vendor));
        var device = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Dev", "includes": [{ "schema": "Vendor" }],
              "members": [{ "name": "AddressMap", "kind": "group", "nodeDef": "vendor.messageBody", "members": [{ "name": "Y", "kind": "field", "datatype": "sys.uint8" }] }] }
            """));
        var broken = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Broken", "includes": [{ "schema": "Vendor" }],
              "members": [{ "name": "Map", "kind": "group", "nodeDef": "vendor.unknownBody", "members": [] }] }
            """));

        var instantiator = SchemaInstantiator.Create(vendor, [vendor, broken, device]);
        var root = instantiator.CreateRoot("Message");

        Assert.Equal(["Dev.AddressMap"], Assert.IsType<InstanceChoice>(root.Members[0]).Options.Select(option => option.Name));
        var warning = Assert.Single(instantiator.Warnings);
        Assert.Contains("'Broken' is left out", warning);
        Assert.Contains("unknownBody", warning);
    }

    [Fact]
    public void OpenChoice_WithoutBodies_IsReported()
    {
        var vendor = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Vendor));
        var instantiator = SchemaInstantiator.Create(vendor, [vendor]);

        instantiator.CreateRoot("Message");

        var warning = Assert.Single(instantiator.Warnings);
        Assert.Contains("'Message.Data' has no bodies", warning);
        Assert.Contains("'Vendor.messageBody'", warning);
    }

    [Fact]
    public void InstantiateCommand_ShowsTheWarningsInTheDialog()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");
        var embedded = Assert.Single(loaded.Schemas);
        // an open document with the name of the embedded schema is used instead of it
        viewModel.NewDocumentCommand.Execute(null);
        var replacing = viewModel.SelectedDocument!;
        replacing.Schema.Name = embedded.Name;
        replacing.MarkChanged();
        Assert.True(embedded.IsReplaced);
        Assert.Equal($"the open document '{embedded.Name}'", embedded.ReplacedBy);
        viewModel.SelectedItem = replacing.Schema;
        Assert.True(viewModel.HasDocumentSourceNote);
        Assert.Contains($"This schema is used instead of the embedded schema '{embedded.Name}' of Jacobi.Midi.Device", viewModel.DocumentSourceNote);
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.SelectedDocument!.AddInclude(embedded.Name);
        viewModel.SelectedItem = viewModel.SelectedDocument!.Schema.Members.Single();

        viewModel.InstantiateSchemaCommand.Execute(null);

        var dialog = Assert.IsType<InstantiateSchemaViewModel>(viewModel.InstantiateDialog);
        Assert.False(dialog.HasError, dialog.Error);
        Assert.True(dialog.HasWarnings);
        Assert.Contains($"'{embedded.Name}' is used instead of the embedded schema", dialog.Warnings);
        dialog.CloseCommand.Execute(null);

        // renaming the replacing document restores the embedded schema
        replacing.Schema.Name = "Other";
        replacing.MarkChanged();
        Assert.False(embedded.IsReplaced);
    }

    [Fact]
    public void MatchCommand_OnAnEmbeddedSchema_FindsTheBodiesInTheSchemasOfItsAssembly()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        var roland = loaded.Schemas.Single(schema => schema.Name == "Roland");
        var message = (EditGroup)roland.Document.Schema.Members.Single(member => member.Name == "Message32");
        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;

        viewModel.MatchSchemaCommand.Execute(message.Members.Single(member => member.Name == "DT1"));

        var dialog = Assert.IsType<InstantiateSchemaViewModel>(viewModel.InstantiateDialog);
        Assert.False(dialog.HasError, dialog.Error);
        Assert.DoesNotContain("left out", dialog.Warnings);
        var data = FindChoice(Assert.IsType<InstanceGroup>(dialog.Root), "Data");
        Assert.NotNull(data);
        Assert.Contains(data.Options, option => option.Name == "Gx700.AddressMap");
    }

    // the first choice named name in the (nested) groups
    private static InstanceChoice? FindChoice(InstanceGroup group, string name)
    {
        foreach (var member in group.Members)
        {
            if (member is InstanceChoice choice && choice.Name == name)
            {
                return choice;
            }
            if (member is InstanceGroup nested && FindChoice(nested, name) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    [Fact]
    public void EmbeddedSchemaReplacedByAnOpenDocument_ShowsANote()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");
        var embedded = Assert.Single(loaded.Schemas);
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.SelectedDocument!.Schema.Name = embedded.Name;
        viewModel.SelectedDocument!.MarkChanged();

        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;
        viewModel.SelectedAssemblyItem = embedded;

        Assert.True(viewModel.HasDocumentSourceNote);
        Assert.Contains($"The open document '{embedded.Name}' is used instead of this schema", viewModel.DocumentSourceNote);
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

        Assert.True(dialog.CanSerialize);
        dialog.CloseCommand.Execute(null);
        Assert.Null(viewModel.InstantiateDialog);

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
        Assert.False(dialog.CanSerialize);
        dialog.CloseCommand.Execute(null);
        Assert.Null(viewModel.InstantiateDialog);
    }

    [Fact]
    public void InstantiateCommand_FindsIncludedSchemasInTheOpenedAssemblies()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");
        var embedded = Assert.Single(loaded.Schemas);
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.SelectedDocument!.AddInclude(embedded.Name);
        viewModel.SelectedItem = viewModel.SelectedDocument!.Schema.Members.Single();

        viewModel.InstantiateSchemaCommand.Execute(null);

        var dialog = Assert.IsType<InstantiateSchemaViewModel>(viewModel.InstantiateDialog);
        Assert.False(dialog.HasError, dialog.Error);
        Assert.NotNull(dialog.Root);
    }

    [Fact]
    public void InstantiateCommand_WorksOnAnEmbeddedSchemaSelectedInTheAssemblyTree()
    {
        var viewModel = new MainViewModel();
        // the Roland schemas include the 'midi' schema of the Device assembly
        viewModel.OpenedAssemblies.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;
        var schema = loaded.Schemas.First(candidate => candidate.Document.Schema.Members.OfType<EditGroup>().Any());
        var root = schema.Document.Schema.Members.OfType<EditGroup>().First();

        viewModel.SelectedAssemblyItem = schema;
        Assert.False(viewModel.InstantiateSchemaCommand.CanExecute(null));

        viewModel.SelectedAssemblyItem = root;
        Assert.True(viewModel.InstantiateSchemaCommand.CanExecute(null));
        viewModel.InstantiateSchemaCommand.Execute(null);

        var dialog = Assert.IsType<InstantiateSchemaViewModel>(viewModel.InstantiateDialog);
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Equal(root.Name, dialog.Root!.Name);
    }

    private const string Serializable = """
        {
          "name": "Ser",
          "nodeDefs": [{ "name": "Body", "kind": "group" }],
          "members": [
            { "name": "Msg", "kind": "group", "members": [
                { "name": "Start", "kind": "field", "datatype": "sys.uint8", "value": "240" },
                { "name": "Head", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "Data", "kind": "repeat", "count": 2, "members": [{ "name": "V", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "End", "kind": "field", "datatype": "sys.uint8", "value": "247" }] },
            { "name": "Env", "kind": "group", "members": [{ "name": "Payload", "kind": "choice", "memberNodeDef": "Body" }] },
            { "name": "BodyB", "kind": "group", "nodeDef": "Body", "members": [{ "name": "Value", "kind": "field", "datatype": "sys.uint8" }] }
          ]
        }
        """;

    [Fact]
    public void Serialize_WritesTheValuesOfARoot()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(msg));
        Field(root, "Head", "A").Value = "1";
        var data = Assert.IsType<InstanceRepeat>(root.Members[2]);
        ((InstanceField)data.Items[0].Members[0]).Value = "2";
        ((InstanceField)data.Items[1].Members[0]).Value = "3";

        var bytes = instantiator.Serialize(instantiator.FindRootName(msg)!.Value, root, TestHelpers.CreateProcessors(), []).Single().Bytes;

        Assert.Equal([0xF0, 0x01, 0x02, 0x03, 0xF7], bytes);
    }

    [Fact]
    public void Serialize_WritesANestedGroupAsARangeOfItsRoot()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var head = (EditGroup)msg.Members.Single(member => member.Name == "Head");
        var instance = Assert.IsType<InstanceGroup>(instantiator.Create(head));
        ((InstanceField)instance.Members[0]).Value = "5";

        Assert.Equal("Ser.Msg", instantiator.FindRootName(head)!.Value.ToString());
        var bytes = instantiator.Serialize(instantiator.FindRootName(head)!.Value, instance, TestHelpers.CreateProcessors(), []).Single().Bytes;

        Assert.Equal([0x05], bytes);
    }

    [Fact]
    public void Serialize_WritesTheSelectedBodyOfAnOpenChoice()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var env = (EditGroup)document.Members.Single(member => member.Name == "Env");
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(env));
        var choice = Assert.IsType<InstanceChoice>(root.Members[0]);
        choice.SelectedOption = choice.Options.Single(option => option.Name == "Ser.BodyB");
        ((InstanceField)((InstanceGroup)choice.Selected!).Members[0]).Value = "7";

        var bytes = instantiator.Serialize(instantiator.FindRootName(env)!.Value, root, TestHelpers.CreateProcessors(), []).Single().Bytes;

        Assert.Equal([0x07], bytes);
    }

    [Fact]
    public void Serialize_ReportsAnInvalidValue()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(msg));
        Field(root, "Head", "A").Value = "abc";

        var exception = Assert.Throws<InvalidOperationException>(
            () => instantiator.Serialize(instantiator.FindRootName(msg)!.Value, root, TestHelpers.CreateProcessors(), []));
        Assert.Contains("'abc'", exception.Message);
    }

    [Fact]
    public async Task SaveAs_ShowsAPlanProblemInThePopup()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var saved = false;
        var dialog = new InstantiateSchemaViewModel(instantiator.Create(msg),
            _ => throw new Jacobi.BinarySerializer.Execution.ExecutionPlanException("Msg", ["Msg: Unknown processor 'sys.bytepacker'."]),
            (_, _) => { saved = true; return Task.FromResult<string?>("file.syx"); },
            new Jacobi.Midi.SchemaEditor.Midi.MidiBarViewModel(null), _ => { });

        await dialog.SaveAsCommand.ExecuteAsync(null);

        Assert.False(saved);
        Assert.True(dialog.HasProblem);
        Assert.Contains("sys.bytepacker", dialog.Problem);
        dialog.DismissProblemCommand.Execute(null);
        Assert.False(dialog.HasProblem);
    }

    [Fact]
    public void CreateRoot_ExpandsSmallNodesAndCollapsesLargeOnes()
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""
            { "name": "Big", "members": [{ "name": "Root", "kind": "group", "members": [
                { "name": "Small", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "Many", "kind": "repeat", "count": 60, "members": [{ "name": "V", "kind": "field", "datatype": "sys.uint8" }] }] }] }
            """));

        var root = SchemaInstantiator.Create(document, [document]).CreateRoot("Root");

        Assert.True(root.IsExpanded);
        Assert.True(Assert.IsType<InstanceGroup>(root.Members[0]).IsExpanded);
        var many = Assert.IsType<InstanceRepeat>(root.Members[1]);
        Assert.False(many.IsExpanded);
        // a collapsed node shows no items (so the dialog creates no controls for them) but keeps them
        Assert.Null(many.ShownItems);
        Assert.Equal(60, many.Items.Count);
        Assert.Equal("60 fields", many.FieldCountText);
        Assert.All(many.Items, item => Assert.True(item.IsExpanded));

        many.IsExpanded = true;
        Assert.Same(many.Items, many.ShownItems);
    }

    [Fact]
    public void SelectingTheBodyOfTheDeviceAddressMap_KeepsItsLargePartsCollapsed()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        var roland = loaded.Schemas.Single(schema => schema.Name == "Roland");
        var message = (EditGroup)roland.Document.Schema.Members.Single(member => member.Name == "Message32");
        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;
        viewModel.MatchSchemaCommand.Execute(message.Members.Single(member => member.Name == "DT1"));
        var data = FindChoice(Assert.IsType<InstanceGroup>(viewModel.InstantiateDialog!.Root), "Data")!;

        data.SelectedOption = data.Options.Single(option => option.Name == "Gx700.AddressMap");

        var body = Assert.IsType<InstanceBody>(data.Selected);
        Assert.True(body.IsExpanded);
        var memory = Assert.IsType<InstanceRepeat>(body.Members.Single(member => member.Name == "Memory"));
        Assert.False(memory.IsExpanded);
        // a repeat of an address map shows one of the items of the address space (midi.repeat)
        Assert.Single(memory.Items);
        Assert.Equal(100, memory.AddressItemCount);
        Assert.False(memory.IsOpen);
        Assert.All(memory.Items, patch => Assert.False(patch.IsExpanded));
    }

    // a GP-8 DT1 message of the bulk dump (one memory patch each) and the size of one message
    private const string Gp8BulkDumpFile = "Schema/gp8-sysex.syx";
    private const int Gp8MessageSize = 59;

    // the GP-8 DT1 message (Roland.Message16) in the match dialog with the GP-8 address map selected as its body
    private static (InstantiateSchemaViewModel Dialog, InstanceRepeat Memory) MatchGp8DataMessage()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        var roland = loaded.Schemas.Single(schema => schema.Name == "Roland");
        var message = (EditGroup)roland.Document.Schema.Members.Single(member => member.Name == "Message16");
        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;
        viewModel.MatchSchemaCommand.Execute(message.Members.Single(member => member.Name == "DT1"));
        var dialog = viewModel.InstantiateDialog!;
        var data = FindChoice(Assert.IsType<InstanceGroup>(dialog.Root), "Data")!;
        data.SelectedOption = data.Options.Single(option => option.Name == "Gp8.AddressMap");
        var body = Assert.IsType<InstanceBody>(data.Selected);
        return (dialog, Assert.IsType<InstanceRepeat>(body.Members.Single(member => member.Name == "Memory")));
    }

    [Fact]
    public void Match_ADataMessageOfAnAddressMap_ReadsThePatchAtItsAddress()
    {
        var (dialog, memory) = MatchGp8DataMessage();
        var bytes = File.ReadAllBytes(TestHelpers.PathOf(Gp8BulkDumpFile)).AsSpan(5 * Gp8MessageSize, Gp8MessageSize).ToArray();

        dialog.Match!.SelectedBuffer = new SysExBuffer("patch 5", bytes);

        Assert.False(dialog.Match.HasError, dialog.Match.Error);
        Assert.False(dialog.Match.HasWarning, dialog.Match.Warning);
        // the address of the message selects the item of the repeat; it received the values and is included
        var item = Assert.Single(memory.Items);
        Assert.Equal(5, memory.ItemIndex);
        Assert.Equal(5, item.Index);
        Assert.Contains(item.Fields(), field => !field.IsConstant && field.Value.Length > 0);
        Assert.True(item.IsIncluded);
        Assert.Contains("0x42-40", dialog.Transfer);
    }

    [Fact]
    public void Match_AnIncompleteDataMessageOfAnAddressMap_ReadsAsMuchAsPossibleAndWarns()
    {
        var (dialog, memory) = MatchGp8DataMessage();
        var complete = File.ReadAllBytes(TestHelpers.PathOf(Gp8BulkDumpFile)).AsSpan(5 * Gp8MessageSize, Gp8MessageSize).ToArray();
        // half of the patch data, then a checksum and the end of exclusive
        byte[] bytes = [.. complete.AsSpan(0, 7 + 20), 0x00, 0xF7];

        dialog.Match!.SelectedBuffer = new SysExBuffer("partial", bytes);

        Assert.True(dialog.Match.HasWarning);
        Assert.Contains("not recognised", dialog.Match.Warning);
        Assert.Contains("Gp8.AddressMap", dialog.Match.Warning);
        Assert.True(dialog.Match.HasError);
        Assert.Equal(5, memory.ItemIndex);
        Assert.Contains(Assert.Single(memory.Items).Fields(), field => !field.IsConstant && field.Value.Length > 0);
    }

    /// <summary>A GP-8 DT1 message (Roland.Message16) instantiated with the GP-8 address map selected as its body.</summary>
    private sealed record Gp8DataMessage(SchemaInstantiator Instantiator, SchemaName RootName, InstanceGroup Root, InstanceBody Body,
        InstanceRepeat Memory, IProcessorProvider Processors)
    {
        public InstanceGroup Temporary => Assert.IsType<InstanceGroup>(Body.Members.Single(member => member.Name == "Temporary"));
        public string? Transfer => Instantiator.DescribeTransfer(RootName, Root, Processors);
    }

    private static Gp8DataMessage InstantiateGp8DataMessage()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        var roland = loaded.Schemas.Single(schema => schema.Name == "Roland");
        var message = (EditGroup)roland.Document.Schema.Members.Single(member => member.Name == "Message16");
        var dt1 = (EditGroup)message.Members.Single(member => member.Name == "DT1");
        var processors = viewModel.OpenedAssemblies.Processors;
        var instantiator = new SchemaInstantiator(viewModel.LoadedSchemas.Compile(roland.Document.Schema), processors);
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(dt1));
        var data = FindChoice(root, "Data")!;
        data.SelectedOption = data.Options.Single(option => option.Name == "Gp8.AddressMap");
        var body = Assert.IsType<InstanceBody>(data.Selected);
        var memory = Assert.IsType<InstanceRepeat>(body.Members.Single(member => member.Name == "Memory"));
        return new Gp8DataMessage(instantiator, instantiator.FindRootName(dt1)!.Value, root, body, memory, processors);
    }

    private static byte[] Gp8Patch(int index)
        => File.ReadAllBytes(TestHelpers.PathOf(Gp8BulkDumpFile)).AsSpan(index * Gp8MessageSize, Gp8MessageSize).ToArray();

    // the groups of the temporary patch that can be included, in address order
    private static List<InstanceNode> TemporaryGroups(Gp8DataMessage gp8)
        => [.. gp8.Temporary.Members.Where(member => member.CanInclude)];

    [Fact]
    public void AddressMapBody_ShowsOneItemOfARepeatAndIncludesNothing()
    {
        var gp8 = InstantiateGp8DataMessage();

        Assert.True(gp8.Body.IsAddressMap);
        Assert.Null(gp8.Body.Note);
        var item = Assert.Single(gp8.Memory.Items);
        Assert.True(gp8.Memory.IsAddressed);
        Assert.False(gp8.Memory.IsOpen);
        Assert.Equal(128, gp8.Memory.AddressItemCount);
        Assert.Equal(128, gp8.Memory.ItemIndexes.Count);
        Assert.Equal(0, gp8.Memory.ItemIndex);
        Assert.Equal(0, item.Index);
        Assert.True(gp8.Body.CanInclude);
        Assert.False(gp8.Body.IsIncluded);
        // the envelope is not part of the map
        Assert.False(gp8.Root.CanInclude);
        Assert.Contains("Nothing of the address map 'Gp8.AddressMap' is included", gp8.Transfer);

        gp8.Temporary.IsIncluded = true;

        Assert.Null(gp8.Body.IsIncluded);
        Assert.Contains("Sends 1 Message16.DT1 message(s) at 0x00-00 (50 bytes)", gp8.Transfer);
    }

    [Fact]
    public void AddressMapBody_TheAddressFollowsTheItemIndex()
    {
        var gp8 = InstantiateGp8DataMessage();
        gp8.Memory.IsIncluded = true;

        // the memory patches from 0x40-00 (linear 0x2000), 0x40 addresses each
        Assert.Contains("Sends 1 Message16.DT1 message(s) at 0x40-00 (50 bytes)", gp8.Transfer);

        gp8.Memory.ItemIndex = 5;

        var item = Assert.Single(gp8.Memory.Items);
        Assert.Equal(5, item.Index);
        Assert.EndsWith("Memory[5]", item.DisplayPath);
        Assert.Contains("Sends 1 Message16.DT1 message(s) at 0x42-40 (50 bytes)", gp8.Transfer);
    }

    [Fact]
    public void AddressMapBody_IncludingTheWholeMap_SendsTheShownItemsOnly()
    {
        var gp8 = InstantiateGp8DataMessage();
        gp8.Memory.ItemIndex = 6;

        gp8.Body.IsIncluded = true;

        Assert.Contains("Sends 2 Message16.DT1 message(s) at 0x00-00 (50 bytes), 0x43-00 (50 bytes)", gp8.Transfer);
    }

    [Fact]
    public void AddressMapBody_IncludingANodeAfterAGap_IncludesTheNodesInBetween()
    {
        var gp8 = InstantiateGp8DataMessage();
        var groups = TemporaryGroups(gp8);

        groups[0].IsIncluded = true;
        groups[2].IsIncluded = true;

        Assert.Equal([true, true, true], groups.Take(3).Select(group => group.IsIncluded));
        Assert.All(groups.Skip(3), group => Assert.False(group.IsIncluded));

        // the memory patch is after the temporary patch: the rest of the temporary patch is included too
        gp8.Memory.IsIncluded = true;

        Assert.All(groups, group => Assert.True(group.IsIncluded));
        Assert.True(gp8.Body.IsIncluded);
    }

    [Fact]
    public void AddressMapBody_LeavingOutANode_LeavesOutEverythingAfterIt()
    {
        var gp8 = InstantiateGp8DataMessage();
        var groups = TemporaryGroups(gp8);
        gp8.Body.IsIncluded = true;

        groups[1].IsIncluded = false;

        Assert.True(groups[0].IsIncluded);
        Assert.All(groups.Skip(1), group => Assert.False(group.IsIncluded));
        Assert.Null(gp8.Temporary.IsIncluded);
        Assert.False(gp8.Memory.IsIncluded);
    }

    [Fact]
    public void AddressMapBody_NothingIncluded_CannotBeSent()
    {
        var gp8 = InstantiateGp8DataMessage();

        Assert.Contains("Nothing of the address map 'Gp8.AddressMap' is included", gp8.Transfer);
        var exception = Assert.Throws<InvalidOperationException>(() => gp8.Instantiator.Serialize(gp8.RootName, gp8.Root, gp8.Processors, []));
        Assert.Contains("Nothing of the address map", exception.Message);
    }

    [Fact]
    public void AddressMapBody_MatchThenSerialize_WritesTheSameMessage()
    {
        var gp8 = InstantiateGp8DataMessage();
        var original = Gp8Patch(5);
        gp8.Instantiator.Deserialize(gp8.RootName, gp8.Root, original, gp8.Processors, []);

        var warnings = new List<string>();
        var message = Assert.Single(gp8.Instantiator.Serialize(gp8.RootName, gp8.Root, gp8.Processors, warnings));

        // the patch that was read is the one that is sent, at its own address
        Assert.Empty(warnings);
        Assert.Equal(Convert.ToHexString(original), Convert.ToHexString(message.Bytes));
        Assert.Equal(TimeSpan.FromMilliseconds(20), message.Interval);
    }

    [Fact]
    public void AddressMapBody_MatchThenAnotherItemIndex_SendsTheSameDataAsThatItem()
    {
        var gp8 = InstantiateGp8DataMessage();
        var original = Gp8Patch(5);
        gp8.Instantiator.Deserialize(gp8.RootName, gp8.Root, original, gp8.Processors, []);

        gp8.Memory.ItemIndex = 7;
        var bytes = Assert.Single(gp8.Instantiator.Serialize(gp8.RootName, gp8.Root, gp8.Processors, [])).Bytes;

        // patch 7 is at 0x43-40; the data is the same, the checksum follows the address
        Assert.Equal(original.Length, bytes.Length);
        Assert.Equal([0x43, 0x40], bytes[5..7]);
        Assert.Equal(Convert.ToHexString(original[7..^2]), Convert.ToHexString(bytes[7..^2]));
        Assert.Equal(0, bytes[5..^1].Sum(value => value) % 0x80);
    }

    [Fact]
    public void AddressMapBody_TheAddressFieldIsCalculated()
    {
        var gp8 = InstantiateGp8DataMessage();
        gp8.Instantiator.Deserialize(gp8.RootName, gp8.Root, Gp8Patch(5), gp8.Processors, []);
        // a wrong address typed by the user is not used
        Assert.IsType<InstanceField>(Assert.IsType<InstanceGroup>(gp8.Root.Members.Single(member => member.Name == "ChecksumScope"))
            .Members.Single(member => member.Name == "Address")).Value = "0";

        var message = Assert.Single(gp8.Instantiator.Serialize(gp8.RootName, gp8.Root, gp8.Processors, []));

        Assert.Equal(Convert.ToHexString(Gp8Patch(5)), Convert.ToHexString(message.Bytes));
    }

    [Fact]
    public void AddressMapBody_TheDialogShowsTheTransferOfTheIncludedGroups()
    {
        var (dialog, memory) = MatchGp8DataMessage();
        Assert.Contains("Nothing of the address map", dialog.Transfer);

        memory.IsIncluded = true;
        Assert.Contains("Sends 1 Message16.DT1 message(s) at 0x40-00 (50 bytes)", dialog.Transfer);

        memory.ItemIndex = 5;
        Assert.Contains("Sends 1 Message16.DT1 message(s) at 0x42-40 (50 bytes)", dialog.Transfer);
    }

    [Fact]
    public void Deserialize_ExpandsThePartThatWasRead()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var head = (EditGroup)((EditGroup)document.Members.Single(member => member.Name == "Msg")).Members.Single(member => member.Name == "Head");
        var instance = Assert.IsType<InstanceGroup>(instantiator.Create(head));
        instance.IsExpanded = false;

        // a nested group is read as a range of its root: only its own bytes
        instantiator.Deserialize(instantiator.FindRootName(head)!.Value, instance, [0x05], TestHelpers.CreateProcessors(), []);

        Assert.Equal("5", ((InstanceField)instance.Members[0]).Value);
        Assert.True(instance.IsExpanded);
    }

    [Fact]
    public void Deserialize_ReadsTheValuesOfARoot()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(msg));

        var read = instantiator.Deserialize(instantiator.FindRootName(msg)!.Value, root, [0xF0, 0x01, 0x02, 0x03, 0xF7], TestHelpers.CreateProcessors(), []);

        Assert.Equal(5, read);
        Assert.Equal("1", Field(root, "Head", "A").Value);
        var data = Assert.IsType<InstanceRepeat>(root.Members[2]);
        Assert.Equal(["2", "3"], data.Items.Select(item => ((InstanceField)item.Members[0]).Value));
    }

    [Fact]
    public void Deserialize_ReadsANestedGroupAsARangeOfItsRoot()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var head = (EditGroup)msg.Members.Single(member => member.Name == "Head");
        var instance = Assert.IsType<InstanceGroup>(instantiator.Create(head));

        instantiator.Deserialize(instantiator.FindRootName(head)!.Value, instance, [0x05], TestHelpers.CreateProcessors(), []);

        Assert.Equal("5", ((InstanceField)instance.Members[0]).Value);
    }

    [Fact]
    public void Deserialize_ReadsTheSelectedBodyOfAnOpenChoice()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var env = (EditGroup)document.Members.Single(member => member.Name == "Env");
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(env));
        var choice = Assert.IsType<InstanceChoice>(root.Members[0]);

        Assert.Throws<InvalidOperationException>(
            () => instantiator.Deserialize(instantiator.FindRootName(env)!.Value, root, [0x07], TestHelpers.CreateProcessors(), []));

        choice.SelectedOption = choice.Options.Single(option => option.Name == "Ser.BodyB");
        instantiator.Deserialize(instantiator.FindRootName(env)!.Value, root, [0x07], TestHelpers.CreateProcessors(), []);

        Assert.Equal("7", ((InstanceField)((InstanceGroup)choice.Selected!).Members[0]).Value);
    }

    [Fact]
    public void Deserialize_AddsAndRemovesTheItemsOfAnOpenRepeatAndSelectsTheAlternativeOfAChoice()
    {
        var (instantiator, document) = CreateInstantiator("""
            {
              "name": "Dyn",
              "members": [
                { "name": "Msg", "kind": "group", "members": [
                    { "name": "Count", "kind": "field", "datatype": "sys.uint8" },
                    { "name": "Data", "kind": "repeat", "count": { "ref": "Msg.Count" }, "members": [{ "name": "V", "kind": "field", "datatype": "sys.uint8" }] },
                    { "name": "Mode", "kind": "choice", "selectedIndex": { "ref": "Msg.Count" }, "members": [
                        { "name": "None", "kind": "group", "members": [{ "name": "N", "kind": "field", "datatype": "sys.uint8" }] },
                        { "name": "One", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] },
                        { "name": "Two", "kind": "group", "members": [{ "name": "B", "kind": "field", "datatype": "sys.uint8" }] }] }] }
              ]
            }
            """);
        var msg = (EditGroup)document.Members.Single();
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(msg));
        var data = Assert.IsType<InstanceRepeat>(root.Members[1]);
        var mode = Assert.IsType<InstanceChoice>(root.Members[2]);
        var rootName = instantiator.FindRootName(msg)!.Value;

        instantiator.Deserialize(rootName, root, [0x02, 0x0A, 0x0B, 0x0C], TestHelpers.CreateProcessors(), []);
        Assert.Equal(["10", "11"], data.Items.Select(item => ((InstanceField)item.Members[0]).Value));
        Assert.Equal("Two", mode.SelectedOption!.Name);
        Assert.Equal("12", ((InstanceField)((InstanceGroup)mode.Selected!).Members[0]).Value);

        instantiator.Deserialize(rootName, root, [0x01, 0x0D, 0x0E], TestHelpers.CreateProcessors(), []);
        Assert.Equal(["13"], data.Items.Select(item => ((InstanceField)item.Members[0]).Value));
        Assert.Equal("One", mode.SelectedOption!.Name);
    }

    [Fact]
    public void Deserialize_ReportsBytesThatDoNotMatchAndKeepsTheValuesReadBeforeThem()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(msg));

        // the End field expects F7
        Assert.Throws<InvalidOperationException>(
            () => instantiator.Deserialize(instantiator.FindRootName(msg)!.Value, root, [0xF0, 0x01, 0x02, 0x03, 0x00], TestHelpers.CreateProcessors(), []));

        Assert.Equal("1", Field(root, "Head", "A").Value);
    }

    [Fact]
    public void Match_ShowsTheReadErrorAndTheTrailingBytes()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var root = instantiator.Create(msg);
        var rootName = instantiator.FindRootName(msg)!.Value;
        (string, byte[])? file = ("dump.syx", [0xF0, 0x01, 0x02, 0x03, 0xF7, 0xF0, 0x01, 0xF7]);
        var match = new SysExMatchViewModel((bytes, warnings) => instantiator.Deserialize(rootName, root, bytes, TestHelpers.CreateProcessors(), warnings),
            () => Task.FromResult(file), new Jacobi.Midi.SchemaEditor.Midi.MidiBarViewModel(null));

        match.LoadFileCommand.Execute(null);

        Assert.Equal(["dump.syx #1", "dump.syx #2"], match.Buffers.Select(buffer => buffer.Source));
        Assert.Same(match.Buffers[0], match.SelectedBuffer);
        Assert.False(match.HasError, match.Error);
        Assert.Equal("1", Field((InstanceGroup)root, "Head", "A").Value);

        match.SelectedBuffer = match.Buffers[1];
        Assert.True(match.HasError);
        Assert.False(match.HasWarning);

        match.SelectedBuffer = new SysExBuffer("extra", [0xF0, 0x04, 0x05, 0x06, 0xF7, 0x7E]);
        Assert.False(match.HasError, match.Error);
        Assert.Contains("1 trailing byte(s)", match.Warning);
        Assert.Contains("7E", match.Warning);
    }

    [Fact]
    public void MatchCommand_OpensTheDialogInMatchMode()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.SelectedItem = viewModel.SelectedDocument!.Schema.Members.Single();

        Assert.True(viewModel.MatchSchemaCommand.CanExecute(null));
        viewModel.MatchSchemaCommand.Execute(null);

        var dialog = Assert.IsType<InstantiateSchemaViewModel>(viewModel.InstantiateDialog);
        Assert.True(dialog.IsMatch);
        Assert.True(dialog.Match!.CanMatch);
        Assert.StartsWith("Match ", dialog.Title);
        dialog.CloseCommand.Execute(null);
        Assert.Null(viewModel.InstantiateDialog);
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02 }, 1)]
    [InlineData(new byte[] { 0xF0, 0x01, 0xF7 }, 1)]
    [InlineData(new byte[] { 0xF0, 0x01, 0xF7, 0xF0, 0x02, 0xF7, 0xF0, 0x03 }, 3)]
    public void SysExBuffer_SplitsAFileIntoItsMessages(byte[] data, int count)
    {
        var buffers = SysExBuffer.Split("file.syx", data);

        Assert.Equal(count, buffers.Count);
        Assert.Equal(data, buffers.SelectMany(buffer => buffer.Bytes));
    }

    [Fact]
    public void FindRootName_IsNullForANodeDef()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);

        Assert.Null(instantiator.FindRootName((EditGroup)document.NodeDefs.Single()));
    }

    private static InstanceField Field(InstanceGroup root, string group, string field)
        => (InstanceField)((InstanceGroup)root.Members.Single(member => member.Name == group)).Members.Single(member => member.Name == field);

    private static (SchemaInstantiator Instantiator, EditDocument Document) CreateInstantiator(string json)
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(json));
        return (SchemaInstantiator.Create(document, [document]), document);
    }

    private static InstanceGroup CreateRoot(string json, string root)
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(json));
        return SchemaInstantiator.Create(document, [document]).CreateRoot(root);
    }
}
