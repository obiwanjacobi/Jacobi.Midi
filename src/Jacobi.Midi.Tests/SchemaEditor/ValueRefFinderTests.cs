using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;
using Jacobi.Midi.Tests.Schema;
using Xunit;
using JsonSerializer = Jacobi.BinarySerializer.Schema.Json.JsonSerializer;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class ValueRefFinderTests
{
    private const string Refs = """
        {
          "name": "Refs",
          "nodeDefs": [
            { "name": "header", "kind": "group", "members": [{ "name": "Len", "kind": "field", "datatype": "sys.uint8" }] },
            { "name": "Block", "kind": "group", "members": [
                { "name": "A", "kind": "field", "datatype": "sys.uint8" },
                { "name": "B", "kind": "field", "datatype": "sys.uint8" }] }],
          "members": [
            { "name": "Root", "kind": "group", "members": [
                { "name": "Head", "kind": "group", "nodeDef": "header" },
                { "name": "Name", "kind": "field", "datatype": "sys.string", "byteLength": 4,
                  "processors": [{ "name": "sys.string", "pubns": "name" }] },
                { "name": "Mode", "kind": "choice", "selectedIndex": 0, "members": [
                    { "name": "M1", "kind": "field", "datatype": "sys.uint8" },
                    { "name": "M2", "kind": "group", "members": [
                        { "name": "X", "kind": "field", "datatype": "sys.uint8" },
                        { "name": "Y", "kind": "field", "datatype": "sys.uint8" }] }] },
                { "name": "Items", "kind": "repeat", "count": 3, "members": [
                    { "name": "Size", "kind": "field", "datatype": "sys.uint8" },
                    { "name": "Data", "kind": "field", "datatype": "sys.bytes" }] },
                { "name": "Tail", "kind": "field", "datatype": "sys.uint8" }] }]
        }
        """;

    private static readonly ProcessorCatalog Catalog = new(TestHelpers.CreateProcessors());

    [Fact]
    public void Find_OffersTheFieldsBeforeTheNode_NearestFirst()
    {
        var candidates = FindCompiled("Root", "Tail");

        // the choice is skipped: which alternative is read is only known at runtime; the node def members are expanded
        Assert.Equal(["Root.Items[].Data", "Root.Items[].Size", "Root.Name", "Root.Head.Len"],
            candidates.Targets.Select(target => target.DisplayPath));
        Assert.Equal("sys.uint8", candidates.Targets[1].DataType);
    }

    [Fact]
    public void Find_OffersTheValuesPublishedBeforeTheNode()
    {
        var candidates = FindCompiled("Root", "Tail");

        var published = Assert.Single(candidates.Published);
        Assert.Equal("pub:name.length", published.Reference);
        Assert.Equal("Root.Name", published.Source);
    }

    [Fact]
    public void Find_InARepeat_MarksTheRepeatThatHoldsTheNode()
    {
        var candidates = FindCompiled("Root", "Items", "Data");

        Assert.Equal(["Root.Items[].Size", "Root.Name", "Root.Head.Len"], candidates.Targets.Select(target => target.DisplayPath));
        var repeat = Assert.Single(candidates.Targets[0].Repeats);
        Assert.True(repeat.HoldsReferrer);
        Assert.Equal(3, repeat.Count);
    }

    [Fact]
    public void Find_InAChoiceAlternative_OffersItsOwnAlternativeOnly()
    {
        var candidates = FindCompiled("Root", "Mode", "M2", "Y");

        Assert.Equal(["Root.Mode.M2.X", "Root.Name", "Root.Head.Len"], candidates.Targets.Select(target => target.DisplayPath));
    }

    [Fact]
    public void Find_InANodeDef_ReachesTheRootOfTheNodeDef()
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Refs));
        var block = (EditGroup)document.NodeDefs.Single(node => node.Name == "Block");

        var candidates = Find(document, block.Members.Single(member => member.Name == "B"));

        Assert.Equal(["Block.A"], candidates.Targets.Select(target => target.DisplayPath));
    }

    [Fact]
    public void FromDraft_DoesNotHaveTheMembersOfNodeDefs()
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Refs));
        var (names, top) = CompiledNodeLocator.GetNames(FindNode(document, "Root", "Tail"));

        var candidates = ValueRefFinder.Find(ValueRefFinder.FromDraft(top, processor => processor.Processor.FullName), names, Catalog.GetProperties);

        Assert.Equal(["Root.Items[].Data", "Root.Items[].Size", "Root.Name"], candidates.Targets.Select(target => target.DisplayPath));
        Assert.Equal("pub:name.length", Assert.Single(candidates.Published).Reference);
    }

    [Fact]
    public void Dialog_ARepeatThatDoesNotHoldTheNode_StartsAtTheFirstItem()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Tail"), String.Empty);

        dialog.SelectedTarget = dialog.Targets.Single(target => target.Path == "Root.Items.Size");

        Assert.Equal("ref:Root.Items[0].Size", dialog.Result);
        Assert.True(dialog.ApplyCommand.CanExecute(null));

        // '[]' is only valid inside the repeat
        dialog.RepeatIndexes[0].Text = String.Empty;
        Assert.True(dialog.HasProblem);
        Assert.False(dialog.ApplyCommand.CanExecute(null));

        dialog.RepeatIndexes[0].Text = "3";
        Assert.Contains("has 3 items", dialog.Problem);
    }

    [Fact]
    public void Dialog_ARepeatThatHoldsTheNode_StartsAtTheSameItem()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Items", "Data"), String.Empty);

        dialog.SelectedTarget = dialog.Targets[0];

        Assert.Equal("ref:Root.Items[].Size", dialog.Result);
        Assert.False(dialog.HasProblem);

        dialog.RepeatIndexes[0].Text = "1";
        Assert.Equal("ref:Root.Items[1].Size", dialog.Result);
    }

    [Fact]
    public void Dialog_SelectsTheCurrentReference()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Tail"), "ref:Root.Items[2].Size");

        Assert.Equal("Root.Items.Size", dialog.SelectedTarget?.Path);
        Assert.Equal("2", dialog.RepeatIndexes[0].Text);
        Assert.Equal("ref:Root.Items[2].Size", dialog.Result);
        Assert.False(dialog.HasNotes);
    }

    [Theory]
    [InlineData("Root.Mode.M2.X", "ref:^.X")]
    [InlineData("Root.Name", "ref:^.^.^.Name")]
    [InlineData("Root.Head.Len", "ref:^.^.^.Head.Len")]
    public void Dialog_Relative_GoesUpToTheCommonAncestorAndDown(string path, string expected)
    {
        var dialog = CreateDialog(FindCompiled("Root", "Mode", "M2", "Y"), String.Empty);
        dialog.IsRelative = true;

        dialog.SelectedTarget = dialog.Targets.Single(target => target.Path == path);

        Assert.Equal(expected, dialog.Result);
    }

    [Theory]
    [InlineData("Root", "Tail")]
    [InlineData("Root", "Items", "Data")]
    [InlineData("Root", "Mode", "M2", "Y")]
    public void Dialog_Relative_ResolvesToTheTargetInTheSerializer(params string[] referrer)
    {
        var candidates = FindCompiled(referrer);
        var dialog = CreateDialog(candidates, String.Empty);
        dialog.IsRelative = true;

        foreach (var target in candidates.Targets)
        {
            dialog.SelectedTarget = target;
            // a relative path cannot reach into a repeat that does not hold the referrer
            Assert.Equal(target.Repeats.Any(repeat => !repeat.HoldsReferrer), dialog.HasProblem);
            if (dialog.HasProblem)
            {
                continue;
            }
            Assert.True(SchemaNodeRef.TryParse(dialog.Result, out var reference), dialog.Result);
            Assert.True(reference.IsRelative);
            Assert.Equal(target.Path, reference.Resolve(candidates.Referrer)?.Path);
        }
    }

    [Fact]
    public void Dialog_Relative_KeepsTheItemOfTheRepeatItGoesUpThrough()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Items", "Data"), String.Empty);
        dialog.SelectedTarget = dialog.Targets.Single(target => target.Path == "Root.Items.Size");
        dialog.RepeatIndexes[0].Text = "x";
        Assert.True(dialog.HasProblem);

        dialog.IsRelative = true;

        Assert.Equal("ref:^.Size", dialog.Result);
        Assert.False(dialog.ShowsRepeatIndexes);
        Assert.False(dialog.HasProblem);
    }

    [Fact]
    public void Dialog_Relative_CannotReachIntoAnotherRepeat()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Tail"), String.Empty);
        dialog.IsRelative = true;

        dialog.SelectedTarget = dialog.Targets.Single(target => target.Path == "Root.Items.Size");

        Assert.Contains("'Root.Items'", dialog.Problem);
        Assert.False(dialog.ApplyCommand.CanExecute(null));

        dialog.IsRelative = false;
        Assert.Equal("ref:Root.Items[0].Size", dialog.Result);
        Assert.True(dialog.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void Dialog_SelectsTheCurrentRelativeReference()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Items", "Data"), "ref:^.Size");

        Assert.True(dialog.IsRelative);
        Assert.Equal("Root.Items.Size", dialog.SelectedTarget?.Path);
        Assert.Equal("ref:^.Size", dialog.Result);
        Assert.False(dialog.HasNotes);
    }

    [Fact]
    public void Dialog_InANodeDef_StartsRelative()
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Refs));
        var block = (EditGroup)document.NodeDefs.Single(node => node.Name == "Block");
        var dialog = new ValueRefFinderViewModel("title", Find(document, block.Members.Single(member => member.Name == "B")),
            String.Empty, [], (_, _) => { }, relative: true);

        dialog.SelectedTarget = dialog.Targets.Single();

        Assert.Equal("ref:^.A", dialog.Result);
    }

    [Fact]
    public void Dialog_SelectsTheCurrentPublishedValue()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Tail"), "pub:name.length");

        Assert.Equal(ValueRefFinderViewModel.PubTab, dialog.SelectedTab);
        Assert.Equal("pub:name.length", dialog.Result);
    }

    [Fact]
    public void Dialog_TellsWhenTheCurrentReferenceIsNotOffered()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Name"), "ref:Root.Tail");

        Assert.Null(dialog.SelectedTarget);
        Assert.Contains("ref:Root.Tail", dialog.Notes);
    }

    [Fact]
    public void Dialog_Apply_ClosesWithTheResult()
    {
        string? applied = null;
        var dialog = new ValueRefFinderViewModel("title", FindCompiled("Root", "Tail"), String.Empty, [], (_, reference) => applied = reference);
        dialog.SelectedTarget = dialog.Targets.Single(target => target.Path == "Root.Name");

        dialog.ApplyCommand.Execute(null);

        Assert.Equal("ref:Root.Name", applied);
    }

    [Fact]
    public void Dialog_Filter_KeepsThePathsThatContainIt()
    {
        var dialog = CreateDialog(FindCompiled("Root", "Tail"), String.Empty);

        dialog.Filter = "items";

        Assert.Equal(["Root.Items[].Data", "Root.Items[].Size"], dialog.Targets.Select(target => target.DisplayPath));
    }

    private static ValueRefFinderViewModel CreateDialog(ValueRefCandidates candidates, string current)
        => new("title", candidates, current, [], (_, _) => { });

    private static ValueRefCandidates FindCompiled(params string[] names)
    {
        var document = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(Refs));
        return Find(document, FindNode(document, names));
    }

    private static ValueRefCandidates Find(EditDocument document, EditNode node)
    {
        var compilation = SchemaSetWorkspace.Compile(document, [document]);
        var (compiled, top, names, _) = CompiledNodeLocator.Find(compilation.Schemas, compilation.Documents, node);
        Assert.NotNull(compiled);
        return ValueRefFinder.Find(ValueRefFinder.FromCompiled(top!), names, Catalog.GetProperties);
    }

    private static EditNode FindNode(EditDocument document, params string[] names)
    {
        EditNode node = document;
        foreach (var name in names)
        {
            node = ((EditGroup)node).Members.Single(member => member.Name == name);
        }
        return node;
    }
}
