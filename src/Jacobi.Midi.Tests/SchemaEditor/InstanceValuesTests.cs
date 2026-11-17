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

        var count = Paste(slots.Items[0], slots.Items[1]);

        Assert.Equal(2, count);
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

        var count = Paste(current, previous);

        Assert.Equal(5, count);
        Assert.Equal("90", Field(Group(previous, "Common"), "Volume").Value);
        Assert.Equal("64", Field(Group(previous, "Tone"), "Level").Value);
        Assert.Equal(["1", "2"], Repeat(previous, "Steps").Items.Select(item => Field(item, "S").Value));
        var pasted = Choice(previous, "Mode");
        Assert.Equal("Two", pasted.SelectedOption?.Name);
        Assert.Equal("5", Field((InstanceGroup)pasted.Selected!, "B").Value);
    }

    [Fact]
    public void Copy_SkipsTheConstants()
    {
        var root = CreateRoot();

        var text = InstanceValues.Copy(Group(root, "Current")).ToText();

        // Id is a constant (value 7): not the user's value
        Assert.DoesNotContain("\"Id\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Paste_IntoAnotherGroup_PastesTheMembersWithTheSameName()
    {
        var root = CreateRoot();
        var current = Group(root, "Current");
        var slot = Repeat(root, "Slots").Items[0];
        Field(Group(current, "Common"), "Volume").Value = "90";
        Field(Group(current, "Tone"), "Level").Value = "64";
        var values = InstanceValues.Copy(current);

        // the slot only has 'Common' in common with a patch
        Assert.True(values.Matches(slot));
        var outcome = InstantiateSchemaViewModel.Paste(slot, values.ToText());

        Assert.Equal($"Pasted 1 of {values.FieldCount} value(s) of Root.Current into {slot.DisplayPath}.", outcome);
        Assert.Equal("90", Field(Group(slot, "Common"), "Volume").Value);
        Assert.Equal(String.Empty, Field(slot, "Name").Value);
    }

    [Fact]
    public void Matches_WithoutAFieldInCommon_IsFalse()
    {
        var root = CreateRoot();
        var current = Group(root, "Current");
        var slot = Repeat(root, "Slots").Items[0];

        var values = InstanceValues.Copy(Group(current, "Tone"));

        Assert.False(values.Matches(slot));
        Assert.Equal(0, values.PasteInto(slot));
    }

    [Fact]
    public void Matches_DoesNotChangeTheTarget()
    {
        var root = CreateRoot();
        var current = Group(root, "Current");
        Repeat(current, "Steps").AddNewItem();
        var mode = Choice(current, "Mode");
        mode.SelectedOption = mode.Options[1];
        var previous = Group(root, "Previous");
        var selected = Choice(previous, "Mode").SelectedOption;
        var steps = Repeat(previous, "Steps").Items.Count;

        // the steps and the alternative differ: a paste would change them
        Assert.True(InstanceValues.Copy(current).Matches(previous));

        Assert.Same(selected, Choice(previous, "Mode").SelectedOption);
        Assert.Equal(steps, Repeat(previous, "Steps").Items.Count);
        Assert.Equal(String.Empty, Field(Group(previous, "Common"), "Volume").Value);
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

    private static int Paste(InstanceGroup source, InstanceGroup target)
    {
        Assert.True(InstanceValues.TryParse(InstanceValues.Copy(source).ToText(), out var values));
        return values!.PasteInto(target);
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
