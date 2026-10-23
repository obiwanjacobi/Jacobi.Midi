using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.SchemaEditor.Workbench;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class WorkbenchSchemaTests
{
    private static readonly byte[] Baseline = [0xF0, 0x41, 0x00, 0x79, 0x12, 0x00, 0x20, 0x56, 0x49, 0x4E, 0x34, 0xF7];

    internal static SchemaWorkspaceDocument CreateDocument()
    {
        var schema = SchemaFileService.CreateDocument("GX700");
        schema.Members.Clear();
        return new SchemaWorkspaceDocument(schema, SchemaFormat.Xml);
    }

    internal static AssemblyWorkspace CreateProcessors()
    {
        var workspace = new AssemblyWorkspace();
        foreach (var assembly in new[] { typeof(Jacobi.BinarySerializer.Processors.ProcessorFactory).Assembly,
            typeof(Jacobi.Midi.Device.Processors.ProcessorFactory).Assembly })
        {
            workspace.Load(assembly, Path.GetFileName(assembly.Location));
        }
        return workspace;
    }

    [Fact]
    public void CreateRoot_DescribesTheBytesBetweenF0AndF7WithAPlaceholder()
    {
        var document = CreateDocument();

        var root = WorkbenchSchema.CreateRoot(document, 0, Baseline);

        Assert.Equal("Message1", root.Name);
        Assert.Same(root, WorkbenchSchema.FindRoot(document.Schema, 0));
        Assert.Equal("midi.sysex", Assert.Single(root.Processors).Processor.ToString());
        var layout = WorkbenchSchema.GetLayout(root, Baseline);
        var span = Assert.Single(layout.Spans);
        Assert.Equal("Wip_1", span.Field.Name);
        Assert.Equal((1, 10), (span.Start, span.Length));
        Assert.Null(layout.Problem);
        Assert.Equal(0, layout.DescribedCount);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void DefineField_SplitsThePlaceholder_RemoveField_MergesItBack()
    {
        var document = CreateDocument();
        var root = WorkbenchSchema.CreateRoot(document, 0, Baseline);

        var level = WorkbenchSchema.DefineField(document, WorkbenchSchema.GetLayout(root, Baseline), new ByteRange(0, 6, 1), "Level", "sys.uint8", null);
        var layout = WorkbenchSchema.GetLayout(root, Baseline);
        Assert.Equal(["Wip_1", "Level", "Wip_7"], layout.Spans.Select(span => span.Field.Name));
        Assert.Equal([(1, 5), (6, 1), (7, 4)], layout.Spans.Select(span => (span.Start, span.Length)));
        Assert.Equal(1, layout.DescribedCount);

        var name = WorkbenchSchema.DefineField(document, layout, new ByteRange(0, 7, 3), "Name", "sys.string", "Midi.string");
        layout = WorkbenchSchema.GetLayout(root, Baseline);
        Assert.Equal(["Wip_1", "Level", "Name", "Wip_10"], layout.Spans.Select(span => span.Field.Name));
        Assert.Equal("Midi.string", Assert.Single(name.Processors).Processor.ToString());

        WorkbenchSchema.RemoveField(document, layout, level);
        layout = WorkbenchSchema.GetLayout(root, Baseline);
        Assert.Equal(["Wip_1", "Name", "Wip_10"], layout.Spans.Select(span => span.Field.Name));
        Assert.Equal((1, 6), (layout.Spans[0].Start, layout.Spans[0].Length));

        WorkbenchSchema.RemoveField(document, layout, name);
        Assert.Equal((1, 10), (Assert.Single(WorkbenchSchema.GetLayout(root, Baseline).Spans).Start, root.Members.Cast<EditField>().Single().ByteLength is int length ? length : 0));
    }

    [Theory]
    [InlineData(5, 2, "Level", "unknown span")]  // overlaps a field
    [InlineData(0, 1, "Start", "unknown span")]  // the F0
    [InlineData(2, 1, "Wip_x", "reserved")]
    [InlineData(2, 1, "Level", "already has")]
    [InlineData(2, 1, "My Field", "spaces")]
    public void DefineField_RejectsInvalidRangesAndNames(int start, int length, string name, string message)
    {
        var document = CreateDocument();
        var root = WorkbenchSchema.CreateRoot(document, 0, Baseline);
        WorkbenchSchema.DefineField(document, WorkbenchSchema.GetLayout(root, Baseline), new ByteRange(0, 6, 1), "Level", "sys.uint8", null);

        var exception = Assert.Throws<InvalidOperationException>(() => WorkbenchSchema.DefineField(document,
            WorkbenchSchema.GetLayout(root, Baseline), new ByteRange(0, start, length), name, "sys.uint8", null));
        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetLayout_StopsAtAFieldOfUnknownSize()
    {
        var document = CreateDocument();
        var root = WorkbenchSchema.CreateRoot(document, 0, Baseline);
        root.Members.Insert(0, new EditField(root) { Name = "Text", DataType = new SchemaName("sys.string"), Processors = [], Properties = [] });

        var layout = WorkbenchSchema.GetLayout(root, Baseline);

        Assert.Empty(layout.Spans);
        Assert.Contains("'Text'", layout.Problem);
    }

    [Fact]
    public void Reader_ReadsTheFieldValuesOfEveryCapture()
    {
        var document = CreateDocument();
        var root = WorkbenchSchema.CreateRoot(document, 0, Baseline);
        var level = WorkbenchSchema.DefineField(document, WorkbenchSchema.GetLayout(root, Baseline), new ByteRange(0, 6, 1), "Level", "sys.uint8", null);
        var processors = CreateProcessors();
        var instantiator = SchemaInstantiator.Create(document.Schema, processors.Assemblies.SelectMany(assembly => assembly.Schemas).Select(schema => schema.Document.Schema));
        byte[] variant = [.. Baseline];
        variant[6] = 0x28;
        var captures = new[] { new Capture("base", [Baseline]), new Capture("variant", [variant]), new Capture("short", [new byte[] { 0xF0, 0x41, 0xF7 }]) };

        var reads = WorkbenchReader.Read(instantiator, root, 0, captures, processors.Processors);

        var path = WorkbenchSchema.GetPath(level);
        Assert.Equal("Message1.Level", path);
        Assert.Null(reads[0].Error);
        Assert.Equal("32", reads[0].Values[path]);
        Assert.Equal("40", reads[1].Values[path]);
        Assert.NotNull(reads[2].Error);
    }
}
