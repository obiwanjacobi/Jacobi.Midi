using System.Reflection;
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

        var bytes = instantiator.Serialize(instantiator.FindRootName(msg)!.Value, root, TestHelpers.CreateProcessors());

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
        var bytes = instantiator.Serialize(instantiator.FindRootName(head)!.Value, instance, TestHelpers.CreateProcessors());

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

        var bytes = instantiator.Serialize(instantiator.FindRootName(env)!.Value, root, TestHelpers.CreateProcessors());

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
            () => instantiator.Serialize(instantiator.FindRootName(msg)!.Value, root, TestHelpers.CreateProcessors()));
        Assert.Contains("'abc'", exception.Message);
    }

    [Fact]
    public async Task SaveAs_ShowsAPlanProblemInThePopup()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var saved = false;
        var dialog = new InstantiateSchemaViewModel(instantiator.Create(msg),
            () => throw new Jacobi.BinarySerializer.Execution.ExecutionPlanException("Msg", ["Msg: Unknown processor 'sys.bytepacker'."]),
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
    public void Deserialize_ReadsTheValuesOfARoot()
    {
        var (instantiator, document) = CreateInstantiator(Serializable);
        var msg = (EditGroup)document.Members.Single(member => member.Name == "Msg");
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(msg));

        var read = instantiator.Deserialize(instantiator.FindRootName(msg)!.Value, root, [0xF0, 0x01, 0x02, 0x03, 0xF7], TestHelpers.CreateProcessors());

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

        instantiator.Deserialize(instantiator.FindRootName(head)!.Value, instance, [0x05], TestHelpers.CreateProcessors());

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
            () => instantiator.Deserialize(instantiator.FindRootName(env)!.Value, root, [0x07], TestHelpers.CreateProcessors()));

        choice.SelectedOption = choice.Options.Single(option => option.Name == "Ser.BodyB");
        instantiator.Deserialize(instantiator.FindRootName(env)!.Value, root, [0x07], TestHelpers.CreateProcessors());

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

        instantiator.Deserialize(rootName, root, [0x02, 0x0A, 0x0B, 0x0C], TestHelpers.CreateProcessors());
        Assert.Equal(["10", "11"], data.Items.Select(item => ((InstanceField)item.Members[0]).Value));
        Assert.Equal("Two", mode.SelectedOption!.Name);
        Assert.Equal("12", ((InstanceField)((InstanceGroup)mode.Selected!).Members[0]).Value);

        instantiator.Deserialize(rootName, root, [0x01, 0x0D, 0x0E], TestHelpers.CreateProcessors());
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
            () => instantiator.Deserialize(instantiator.FindRootName(msg)!.Value, root, [0xF0, 0x01, 0x02, 0x03, 0x00], TestHelpers.CreateProcessors()));

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
        var match = new SysExMatchViewModel(bytes => instantiator.Deserialize(rootName, root, bytes, TestHelpers.CreateProcessors()),
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
