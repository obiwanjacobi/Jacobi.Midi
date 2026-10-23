using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class ProcessorPropertyTests
{
    [Fact]
    public void Catalog_ReturnsPublishedPropertiesOfKnownProcessors()
    {
        var catalog = CreateCatalog();

        Assert.Contains(catalog.GetProperties("sys.align")!, descriptor => descriptor.Name == "bytes" && descriptor.IsRequired);
        Assert.Null(catalog.GetProperties("sys.unknown"));
        Assert.Null(catalog.GetProperties("other.align"));
        Assert.Null(catalog.GetProperties("align"));
        // the data types of the processor factories are known
        Assert.True(catalog.DataTypes.TryGet(new SchemaName("sys.alignrelativeto"), out _));
    }

    [Fact]
    public void Details_ChecksTheProcessorPropertiesAgainstThePublishedProperties()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var group = Group((EditGroup)schema.Members[0], "group");
        group.Processors.Add(new EditProcessorRef
        {
            Processor = new SchemaName("sys.align"),
            Properties = [Property("relative", "middle"), Property("unknown", "1")]
        });
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        using var details = ItemDetailsViewModel.Create(document, group, CreateCatalog())!;

        var processor = Assert.Single(details.Processors);
        Assert.Contains("'bytes'", processor.Warning);
        Assert.Contains(processor.Properties, row => row.Name == "relative" && row.Warning is not null);
        Assert.Contains(processor.Properties, row => row.Name == "unknown" && row.Warning!.Contains("not a property"));
        Assert.Contains(processor.Properties[0].NameOptions, option => option.Name == "bytes");

        var relative = processor.Properties.Single(row => row.Name == "relative");
        relative.Value = "root";
        Assert.Null(relative.Warning);
        Assert.NotNull(relative.Description);
    }

    [Fact]
    public void Details_ChecksOnlyPrefixedItemPropertiesOfKnownProcessors()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var group = Group((EditGroup)schema.Members[0], "group");
        group.Processors.Add(new EditProcessorRef { Processor = new SchemaName("sys.align") });
        group.Properties.Add(Property("anything", "goes"));
        group.Properties.Add(Property("my.custom.data", "x"));
        group.Properties.Add(Property("sys.align.bytes", "four"));
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        using var details = ItemDetailsViewModel.Create(document, group, CreateCatalog())!;

        Assert.Null(details.Properties.Single(row => row.Name == "anything").Warning);
        Assert.Null(details.Properties.Single(row => row.Name == "my.custom.data").Warning);
        Assert.NotNull(details.Properties.Single(row => row.Name == "sys.align.bytes").Warning);
        Assert.Contains(details.Properties[0].NameOptions, option => option.Name == "sys.align.bytes");
        // like the serializer, only the properties of the processor ref count: a property on the node is not passed to the processor
        Assert.Contains("'bytes'", Assert.Single(details.Processors).Warning);
    }

    [Fact]
    public void Details_ChecksTheProcessorOfAProcessorRefAndAProcessorDef()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var definition = new EditProcessorDef { Name = "align", Processor = "sys.align", Properties = [Property("bytes", "x")] };
        schema.ProcessorDefs.Add(definition);
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        var catalog = CreateCatalog();

        using (var details = ItemDetailsViewModel.Create(document, definition, catalog)!)
        {
            Assert.NotNull(Assert.Single(details.Properties).Warning);
        }

        var root = (EditGroup)schema.Members[0];
        root.Processors.Add(new EditProcessorRef { Processor = new SchemaName("align"), Properties = [Property("bytes", "4")] });
        using (var details = ItemDetailsViewModel.Create(document, root, catalog)!)
        {
            var processor = Assert.Single(details.Processors);
            Assert.Null(processor.Warning);
            Assert.All(processor.Properties.Where(row => row.IsOwn), row => Assert.Null(row.Warning));
        }
    }

    [Fact]
    public void Details_SuggestsThePropertiesOfTheProcessorsOfTheNodeAndItsNodeDef()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var definition = new EditGroup(null) { Name = "def", Members = [], Properties = [], Processors = [] };
        definition.Processors.Add(new EditProcessorRef { Processor = new SchemaName("sys.scale") });
        schema.NodeDefs.Add(definition);
        var group = Group((EditGroup)schema.Members[0], "group");
        group.NodeDef = new SchemaName("def");
        group.Processors.Add(new EditProcessorRef { Processor = new SchemaName("sys.align") });
        group.Properties.Add(Property("property", null));
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        using var details = ItemDetailsViewModel.Create(document, group, CreateCatalog())!;

        var options = Assert.Single(details.Properties).NameOptions.Select(option => option.Name).ToList();
        Assert.Contains("sys.align.bytes", options);
        Assert.Contains("sys.scale.scale", options);
    }

    [Fact]
    public void Details_DoesNotCheckTheProcessorPropertiesOfAProcessorWithoutPublishedProperties()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var group = Group((EditGroup)schema.Members[0], "group");
        group.Processors.Add(new EditProcessorRef { Processor = new SchemaName("sys.null"), Properties = [Property("anything", "1")] });
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        using var details = ItemDetailsViewModel.Create(document, group, CreateCatalog())!;

        var processor = Assert.Single(details.Processors);
        Assert.Null(processor.Warning);
        Assert.Null(Assert.Single(processor.Properties).Warning);
    }

    [Fact]
    public void DataTypeDefEditor_BasedOnOptionsAreCurrentAndExcludeTheDefItself()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        var first = document.AddDataTypeDef("first", "sys.uint8");
        var second = document.AddDataTypeDef("second", "sys.uint8");

        var editor = Assert.IsType<DataTypeDefEditor>(ItemEditor.Create(document, second));

        Assert.Contains("sys.uint8", editor.BasedOnOptions);
        Assert.Contains("Test.first", editor.BasedOnOptions);
        Assert.DoesNotContain("Test.second", editor.BasedOnOptions);
        first.Name = "renamed";
        Assert.Contains("Test.renamed", editor.BasedOnOptions);
    }

    [Fact]
    public void ValueProcessors_MergeTheNodeDefAndOwnAndEditTheOwn()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var definition = new EditRepeat(null) { Name = "def", Count = 1, Members = [], Processors = [], Properties = [], ValueProcessors = [] };
        definition.ValueProcessors.Add(new EditProcessorRef { Processor = new SchemaName("sys.scale") });
        schema.NodeDefs.Add(definition);
        var root = (EditGroup)schema.Members[0];
        var repeat = new EditRepeat(root) { Name = "repeat", Count = 1, Members = [], Processors = [], Properties = [], ValueProcessors = [] };
        root.Members.Add(repeat);
        repeat.NodeDef = new SchemaName("def");
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        using var details = ItemDetailsViewModel.CreateValueProcessors(document, repeat, CreateCatalog())!;

        var inherited = Assert.Single(details.Processors);
        Assert.False(inherited.IsOwn);
        Assert.Equal("nodeDef def", inherited.Source);

        // only semantic processors (and the processor defs of one) can be value processors
        schema.ProcessorDefs.Add(new EditProcessorDef { Name = "scaled", Processor = "sys.scale", Properties = [] });
        schema.ProcessorDefs.Add(new EditProcessorDef { Name = "aligned", Processor = "sys.align", Properties = [] });
        var options = details.ProcessorOptions.Where(option => !option.IsHeader).Select(option => option.Text).ToList();
        Assert.Contains("sys.scale", options);
        Assert.Contains("sys.map", options);
        Assert.DoesNotContain("sys.align", options);
        Assert.Contains("scaled", options);
        Assert.DoesNotContain("aligned", options);
        using (var nodeDetails = ItemDetailsViewModel.Create(document, repeat, CreateCatalog())!)
        {
            Assert.Contains(nodeDetails.ProcessorOptions, option => option.Text == "sys.align");
        }

        details.AddProcessorCommand.Execute(null);
        Assert.Single(repeat.ValueProcessors);
        Assert.Empty(repeat.Processors);
        var own = details.Processors.Single(row => row.IsOwn);
        own.Processor = "sys.map";
        Assert.Equal("sys.map", Assert.Single(repeat.ValueProcessors).Processor.ToString());

        Assert.Equal("The value is converted by 2 value processors.", details.ConversionNote);

        details.RemoveProcessorCommand.Execute(details.Processors.Single(row => row.IsOwn));
        Assert.Empty(repeat.ValueProcessors);
        Assert.Equal("The value is converted by 1 value processor.", details.ConversionNote);
        Assert.Null(ItemDetailsViewModel.CreateValueProcessors(document, root));
    }

    private static ProcessorCatalog CreateCatalog()
    {
        var manager = new ProcessorManager();
        manager.Register(new Jacobi.BinarySerializer.Processors.ProcessorFactory());
        return new ProcessorCatalog(manager);
    }

    private static EditGroup Group(EditGroup parent, string name)
    {
        var group = new EditGroup(parent) { Name = name, Members = [], Processors = [], Properties = [] };
        parent.Members.Add(group);
        return group;
    }

    private static EditProperty Property(string name, string? value) => new() { Name = name, Value = value };
}
