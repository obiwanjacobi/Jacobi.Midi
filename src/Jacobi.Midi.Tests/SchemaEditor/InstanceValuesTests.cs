using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.Tests.Schema;
using Xunit;
using JsonSerializer = Jacobi.BinarySerializer.Schema.Json.JsonSerializer;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class InstanceValuesTests
{
    private const string Patches = """
        {
          "name": "Doc",
          "nodeDefs": [
            { "name": "Inner", "kind": "group", "members": [{ "name": "Level", "kind": "field", "datatype": "sys.uint8" }] },
            { "name": "Patch", "kind": "group", "members": [
                { "name": "Id", "kind": "field", "datatype": "sys.uint8", "value": "7" },
                { "name": "Common", "kind": "group", "members": [{ "name": "Volume", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "Tone", "kind": "group", "nodeDef": "Inner" },
                { "name": "Steps", "kind": "repeat", "members": [{ "name": "S", "kind": "field", "datatype": "sys.uint8" }] },
                { "name": "Mode", "kind": "choice", "members": [
                    { "name": "One", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] },
                    { "name": "Two", "kind": "group", "members": [{ "name": "B", "kind": "field", "datatype": "sys.uint8" }] }] }] }
          ],
          "members": [
            { "name": "Root", "kind": "group", "members": [
                { "name": "Current", "kind": "group", "nodeDef": "Patch" },
                { "name": "Previous", "kind": "group", "nodeDef": "Patch" },
                { "name": "Slots", "kind": "repeat", "count": 2, "members": [
                    { "name": "Name", "kind": "field", "datatype": "sys.uint8" },
                    { "name": "Common", "kind": "group", "members": [{ "name": "Volume", "kind": "field", "datatype": "sys.uint8" }] }] }] }
          ]
        }
        """;

    [Fact]
    public void RepeatItem_PastesIntoAnotherItemOfTheSameRepeat()
    {
        var root = CreateRoot();
        var slots = Repeat(root, "Slots");
        Field(slots.Items[0], "Name").Value = "3";
        Field(Group(slots.Items[0], "Common"), "Volume").Value = "100";

        var problems = new List<string>();
        var count = Paste(slots.Items[0], slots.Items[1], problems);

        Assert.Equal(2, count);
        Assert.Empty(problems);
        Assert.Equal("3", Field(slots.Items[1], "Name").Value);
        Assert.Equal("100", Field(Group(slots.Items[1], "Common"), "Volume").Value);
    }

    [Fact]
    public void GroupsOfTheSameNodeDef_TakeTheFieldsChoicesAndRepeatItems()
    {
        var root = CreateRoot();
        var current = Group(root, "Current");
        Field(Group(current, "Common"), "Volume").Value = "90";
        Field(Group(current, "Tone"), "Level").Value = "64";
        var steps = Repeat(current, "Steps");
        steps.AddNewItem();
        Field(steps.Items[0], "S").Value = "1";
        Field(steps.Items[1], "S").Value = "2";
        var mode = Choice(current, "Mode");
        mode.SelectedOption = mode.Options[1];
        Field((InstanceGroup)mode.Selected!, "B").Value = "5";
        var previous = Group(root, "Previous");

        var problems = new List<string>();
        Paste(current, previous, problems);

        Assert.Empty(problems);
        Assert.Equal("90", Field(Group(previous, "Common"), "Volume").Value);
        Assert.Equal("64", Field(Group(previous, "Tone"), "Level").Value);
        Assert.Equal(["1", "2"], Repeat(previous, "Steps").Items.Select(item => Field(item, "S").Value));
        var pasted = Choice(previous, "Mode");
        Assert.Equal("Two", pasted.SelectedOption?.Name);
        Assert.Equal("5", Field((InstanceGroup)pasted.Selected!, "B").Value);
    }

    [Fact]
    public void Shape_IsTheNodeDefOrTheSchemaNode()
    {
        var root = CreateRoot();
        var current = Group(root, "Current");
        var slots = Repeat(root, "Slots");

        Assert.Equal("nodeDef:Doc.Patch", InstanceValues.ShapeOf(current));
        Assert.Equal("nodeDef:Doc.Inner", InstanceValues.ShapeOf(Group(current, "Tone")));
        // a group in an instance of a node def is the same group in every instance
        Assert.Equal("nodeDef:Doc.Patch/Common", InstanceValues.ShapeOf(Group(current, "Common")));
        Assert.Equal(InstanceValues.ShapeOf(Group(current, "Common")), InstanceValues.ShapeOf(Group(Group(root, "Previous"), "Common")));
        Assert.Equal("schema:Doc.Root.Slots", InstanceValues.ShapeOf(slots.Items[1]));
        Assert.Equal("schema:Doc.Root.Slots.Common", InstanceValues.ShapeOf(Group(slots.Items[0], "Common")));
    }

    [Fact]
    public void Paste_IntoAnotherGroup_IsRefused()
    {
        var root = CreateRoot();
        var current = Group(root, "Current");
        var slot = Repeat(root, "Slots").Items[0];
        Field(Group(current, "Common"), "Volume").Value = "90";
        var text = InstanceValues.Copy(Group(current, "Common")).ToText();

        // the same members, but not the same group
        var outcome = InstantiateSchemaViewModel.Paste(Group(slot, "Common"), text);

        Assert.StartsWith("Could not paste", outcome);
        Assert.Equal(String.Empty, Field(Group(slot, "Common"), "Volume").Value);
        Assert.Throws<InvalidOperationException>(() => InstanceValues.Copy(current).PasteInto(slot, []));
    }

    [Fact]
    public void Paste_IntoAnotherInstanceTree_FitsTheSameGroup()
    {
        var first = CreateRoot();
        Field(Group(Group(first, "Current"), "Tone"), "Level").Value = "12";
        var text = InstanceValues.Copy(Group(first, "Current")).ToText();
        // another dialog: another instantiation
        var second = CreateRoot();

        var outcome = InstantiateSchemaViewModel.Paste(Group(second, "Previous"), text);

        Assert.StartsWith("Pasted", outcome);
        Assert.Equal("12", Field(Group(Group(second, "Previous"), "Tone"), "Level").Value);
    }

    [Fact]
    public void Paste_ReportsAConstantThatDiffersAndValuesWithoutAField()
    {
        var root = CreateRoot();
        var text = InstanceValues.Copy(Group(root, "Current")).ToText()
            .Replace("\"Id\": \"7\"", "\"Id\": \"8\", \"Extra\": \"1\"", StringComparison.Ordinal);
        var previous = Group(root, "Previous");

        Assert.True(InstanceValues.TryParse(text, out var values));
        var problems = new List<string>();
        values!.PasteInto(previous, problems);

        Assert.Equal("7", Field(previous, "Id").Value);
        Assert.Contains(problems, problem => problem.Contains("Root.Previous.Id: kept '7'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Root.Previous.Extra", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some text")]
    [InlineData("""{ "values": {} }""")]
    public void TryParse_RejectsTextWithoutCopiedValues(string? text)
    {
        Assert.False(InstanceValues.TryParse(text, out _));
        Assert.StartsWith("Could not paste", InstantiateSchemaViewModel.Paste(CreateRoot(), text));
    }

    private static int Paste(InstanceGroup source, InstanceGroup target, List<string> problems)
    {
        Assert.True(InstanceValues.TryParse(InstanceValues.Copy(source).ToText(), out var values));
        return values!.PasteInto(target, problems);
    }

    private static InstanceGroup CreateRoot()
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Patches));
        return SchemaInstantiator.Create(document, [document], TestHelpers.CreateProcessors()).CreateRoot("Root");
    }

    private static T Member<T>(InstanceGroup group, string name)
        where T : InstanceNode
        => Assert.IsType<T>(group.Members.Single(member => member.Name == name), exactMatch: false);

    private static InstanceGroup Group(InstanceGroup group, string name) => Member<InstanceGroup>(group, name);

    private static InstanceField Field(InstanceGroup group, string name) => Member<InstanceField>(group, name);

    private static InstanceRepeat Repeat(InstanceGroup group, string name) => Member<InstanceRepeat>(group, name);

    private static InstanceChoice Choice(InstanceGroup group, string name) => Member<InstanceChoice>(group, name);
}
