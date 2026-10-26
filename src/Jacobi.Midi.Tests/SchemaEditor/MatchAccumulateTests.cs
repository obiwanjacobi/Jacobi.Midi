using System.Reflection;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

/// <summary>
/// Tests matching more than one message into the same values (<see cref="SysExMatchViewModel.Accumulate"/>,
/// <see cref="SysExMatchViewModel.MatchAllCommand"/>), with the Roland data messages that carry a part of an address map.
/// </summary>
public class MatchAccumulateTests
{
    private const string Gx700PatchFile = "Schema/gx700-sysex-patch1.syx";
    private const string Gp8BulkDumpFile = "Schema/gp8-sysex.syx";
    private const int Gp8MessageSize = 59;

    /// <summary>A Roland DT1 message in the match dialog with an address map selected as its body.</summary>
    private sealed record DataMessage(SchemaInstantiator Instantiator, SchemaName RootName, InstanceGroup Root, InstanceBody Body,
        SysExMatchViewModel Match);

    private static DataMessage MatchDataMessage(string message, string map, string fileName)
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        var roland = loaded.Schemas.Single(schema => schema.Name == "Roland");
        var envelope = (EditGroup)roland.Document.Schema.Members.Single(member => member.Name == message);
        var dt1 = (EditGroup)envelope.Members.Single(member => member.Name == "DT1");
        var instantiator = new SchemaInstantiator(viewModel.LoadedSchemas.Compile(roland.Document.Schema), viewModel.OpenedAssemblies.Processors);
        var root = Assert.IsType<InstanceGroup>(instantiator.Create(dt1));
        var data = Assert.IsType<InstanceChoice>(Nodes(root).Single(node => node is InstanceChoice { Name: "Data" }));
        data.SelectedOption = data.Options.Single(option => option.Name == map);
        var body = Assert.IsType<InstanceBody>(data.Selected);
        var rootName = instantiator.FindRootName(dt1)!.Value;
        (string, byte[])? file = (fileName, File.ReadAllBytes(TestHelpers.PathOf(fileName)));
        var match = new SysExMatchViewModel((bytes, accumulate, warnings) => instantiator.Deserialize(rootName, root, bytes, warnings, accumulate),
            () => Task.FromResult(file), new MidiBarViewModel(null));
        return new DataMessage(instantiator, rootName, root, body, match);
    }

    private static DataMessage MatchGx700Patch() => MatchDataMessage("Message32", "Gx700.AddressMap", Gx700PatchFile);

    [Fact]
    public void MatchAll_AccumulatesTheSectionsOfAPatchAndSendsThemBack()
    {
        var gx700 = MatchGx700Patch();
        gx700.Match.LoadFileCommand.Execute(null);
        Assert.Equal(14, gx700.Match.Buffers.Count);

        gx700.Match.MatchAllCommand.Execute(null);

        Assert.False(gx700.Match.HasError, gx700.Match.Error);
        Assert.False(gx700.Match.HasWarning, gx700.Match.Warning);
        Assert.StartsWith("Matched 14 message(s)", gx700.Match.Status);
        // the first and the last section of the patch hold their values
        Assert.Equal("VINTAGE TWIN", PatchField(gx700, "Common.Name").Value);
        Assert.Equal("Clean Twin", PatchField(gx700, "Preamp.Type").Value);
        // the sections that were read are included: the whole patch is sent back as it was read
        var messages = gx700.Instantiator.Serialize(gx700.RootName, gx700.Root, []);
        Assert.Equal(Convert.ToHexString(File.ReadAllBytes(TestHelpers.PathOf(Gx700PatchFile))),
            Convert.ToHexString(messages.SelectMany(message => message.Bytes).ToArray()));
    }

    [Fact]
    public void Match_KeepsTheValuesOfTheEarlierMatchesOnlyWhenAccumulating()
    {
        var gx700 = MatchGx700Patch();
        gx700.Match.LoadFileCommand.Execute(null);
        Assert.Equal("VINTAGE TWIN", PatchField(gx700, "Common.Name").Value);

        gx700.Match.SelectedBuffer = gx700.Match.Buffers[4];

        Assert.Equal(String.Empty, PatchField(gx700, "Common.Name").Value);
        Assert.Equal("Clean Twin", PatchField(gx700, "Preamp.Type").Value);

        gx700.Match.Accumulate = true;
        gx700.Match.SelectedBuffer = gx700.Match.Buffers[0];

        Assert.False(gx700.Match.HasError, gx700.Match.Error);
        Assert.Equal("VINTAGE TWIN", PatchField(gx700, "Common.Name").Value);
        Assert.Equal("Clean Twin", PatchField(gx700, "Preamp.Type").Value);
        Assert.Contains("(accumulated)", gx700.Match.Status);
    }

    [Fact]
    public void Accumulate_AMessageForAnotherItem_IsNotReadAndWarns()
    {
        var gp8 = MatchDataMessage("Message16", "Gp8.AddressMap", Gp8BulkDumpFile);
        var memory = Assert.IsType<InstanceRepeat>(gp8.Body.Members.Single(member => member.Name == "Memory"));
        gp8.Match.SelectedBuffer = new SysExBuffer("patch 5", Gp8Patch(5));
        var values = memory.Fields().Select(field => field.Value).ToList();

        gp8.Match.Accumulate = true;
        gp8.Match.SelectedBuffer = new SysExBuffer("patch 6", Gp8Patch(6));

        Assert.False(gp8.Match.HasError, gp8.Match.Error);
        Assert.Contains("Not read", gp8.Match.Warning);
        Assert.Contains("item 6", gp8.Match.Warning);
        Assert.Equal(5, memory.ItemIndex);
        Assert.Equal(values, memory.Fields().Select(field => field.Value));
    }

    private static byte[] Gp8Patch(int index)
        => File.ReadAllBytes(TestHelpers.PathOf(Gp8BulkDumpFile)).AsSpan(index * Gp8MessageSize, Gp8MessageSize).ToArray();

    // a field of the memory patch of the GX-700 map, by its path below the patch (e.g. 'Common.Name')
    private static InstanceField PatchField(DataMessage gx700, string path)
        => gx700.Body.Fields().Single(field => field.DisplayPath.Contains(".Memory", StringComparison.Ordinal)
            && field.DisplayPath.EndsWith($".{path}", StringComparison.Ordinal));

    private static IEnumerable<InstanceNode> Nodes(InstanceNode node) => [node, .. node.Children().SelectMany(Nodes)];
}
