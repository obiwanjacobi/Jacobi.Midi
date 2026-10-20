using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class DocumentItemTests
{
    [Fact]
    public void AddCommands_AddEachDocumentItemSelectItAndSurviveSave()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;

        foreach (var kind in new[] { SchemaNodeKind.Field, SchemaNodeKind.Group, SchemaNodeKind.Repeat, SchemaNodeKind.Choice })
        {
            viewModel.AddNodeDefCommand.Execute(kind);
            Assert.Same(schema.NodeDefs[^1], viewModel.SelectedNode);
            Assert.Null(schema.NodeDefs[^1].Parent);
        }
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Field);
        Assert.Equal(["NewFieldDef", "NewGroupDef", "NewRepeatDef", "NewChoiceDef", "NewFieldDef2"], schema.NodeDefs.Select(node => node.Name));

        viewModel.AddIncludeCommand.Execute(null);
        Assert.Same(Assert.Single(schema.Includes), viewModel.SelectedItem);
        Assert.IsType<DocumentRefEditor>(viewModel.GeneralEditor);
        Assert.Null(viewModel.ItemDetails);

        viewModel.AddDataTypeDefCommand.Execute(null);
        Assert.Same(Assert.Single(schema.DataTypeDefs), viewModel.SelectedItem);
        Assert.True(viewModel.ItemDetails!.HasProcessors);

        viewModel.AddProcessorDefCommand.Execute(null);
        Assert.Same(Assert.Single(schema.ProcessorDefs), viewModel.SelectedItem);
        Assert.False(viewModel.ItemDetails!.HasProcessors);

        viewModel.AddDocumentProcessorCommand.Execute(null);
        Assert.Same(Assert.Single(schema.Processors), viewModel.SelectedItem);

        viewModel.AddDocumentPropertyCommand.Execute(null);
        Assert.Same(Assert.Single(schema.Properties), viewModel.SelectedItem);
        Assert.IsType<PropertyEditor>(viewModel.GeneralEditor);

        var saved = SchemaFileService.Serialize(schema, SchemaFormat.Json);
        var restored = SchemaFileService.Parse(saved, SchemaFormat.Json);
        Assert.Equal([typeof(EditField), typeof(EditGroup), typeof(EditRepeat), typeof(EditChoice), typeof(EditField)],
            restored.NodeDefs.Select(node => node.GetType()));
        Assert.Single(restored.Includes);
        Assert.Single(restored.DataTypeDefs);
        Assert.Single(restored.ProcessorDefs);
        Assert.Single(restored.Processors);
        Assert.Single(restored.Properties);
    }

    [Theory]
    [InlineData(nameof(SchemaFormat.Json))]
    [InlineData(nameof(SchemaFormat.Xml))]
    [InlineData(nameof(SchemaFormat.Yaml))]
    public void DocumentProcessors_SurviveSaveInEachFormat(string formatName)
    {
        var format = Enum.Parse<SchemaFormat>(formatName);
        var schema = SchemaFileService.CreateDocument("Test");
        var document = new SchemaWorkspaceDocument(schema, format);
        var processor = document.AddProcessor("sys.align");
        processor.Properties.Add(new EditProperty { Name = "bytes", Value = "4" });

        var restored = SchemaFileService.Parse(SchemaFileService.Serialize(schema, format), format);

        var restoredProcessor = Assert.Single(restored.Processors);
        Assert.Equal("sys.align", restoredProcessor.Processor.ToString());
        Assert.Equal("4", Assert.Single(restoredProcessor.Properties).Value);
    }

    [Fact]
    public void DeleteItem_RemovesDocumentItems()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        viewModel.AddIncludeCommand.Execute(null);
        viewModel.AddDataTypeDefCommand.Execute(null);
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Group);

        Assert.True(viewModel.DeleteItemCommand.CanExecute(null));
        viewModel.DeleteItemCommand.Execute(null);
        Assert.Empty(schema.NodeDefs);
        Assert.Null(viewModel.SelectedItem);

        viewModel.SelectedItem = schema.DataTypeDefs[0];
        viewModel.DeleteItemCommand.Execute(null);
        Assert.Empty(schema.DataTypeDefs);

        viewModel.SelectedItem = schema.Includes[0];
        viewModel.DeleteItemCommand.Execute(null);
        Assert.Empty(schema.Includes);
        Assert.False(viewModel.DeleteItemCommand.CanExecute(null));
    }

    [Fact]
    public void DataTypeDefEditor_EditsFacetsAndOptionsLive()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        var definition = document.AddDataTypeDef("level", "sys.uint8");
        document.IsDirty = false;
        var editor = Assert.IsType<DataTypeDefEditor>(ItemEditor.Create(document, definition));

        editor.BasedOn = "sys.uint16";
        editor.Min = "-1.5";
        editor.Max = " ";
        Assert.Equal(new SchemaName("sys.uint16"), definition.BasedOn);
        Assert.Equal(-1.5m, definition.Min);
        Assert.Null(definition.Max);
        Assert.True(document.IsDirty);

        // invalid input is rejected and leaves the model unchanged
        Assert.Throws<FormatException>(() => editor.Scale = "abc");
        Assert.Throws<ArgumentException>(() => editor.Name = " ");
        Assert.Null(definition.Scale);
        Assert.Equal("level", definition.Name);

        editor.AddOptionCommand.Execute(null);
        editor.AddOptionCommand.Execute(null);
        editor.Options[1].Value = "0x10";
        editor.Options[1].Label = "Sixteen";
        Assert.Equal(new Dictionary<long, string> { [0] = "Option0", [16] = "Sixteen" }, definition.Options);
        Assert.Throws<FormatException>(() => editor.Options[0].Value = "x");

        editor.RemoveOptionCommand.Execute(editor.Options[0]);
        editor.RemoveOptionCommand.Execute(editor.Options[0]);
        Assert.Null(definition.Options);
    }

    [Fact]
    public void ProcessorRefDetails_MergeWithProcessorDef()
    {
        var schema = SchemaFileService.CreateDocument("Test");
        var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
        var definition = document.AddProcessorDef("align", "sys.align");
        definition.Properties.Add(new EditProperty { Name = "bytes", Value = "4" });
        var processor = document.AddProcessor("ref:align");
        using var details = ItemDetailsViewModel.Create(document, processor)!;

        var inherited = Assert.Single(details.Properties);
        Assert.Equal(("bytes", "4", "processorDef align"), (inherited.Name, inherited.Value, inherited.Source));
        details.OverridePropertyCommand.Execute(inherited);
        Assert.Equal("4", Assert.Single(processor.Properties).Value);
        Assert.True(Assert.Single(details.Properties).IsOwn);

        var editor = Assert.IsType<ProcessorRefEditor>(ItemEditor.Create(document, processor));
        editor.Processor = "sys.scale";
        Assert.Empty(details.Unresolved);
        Assert.Equal("own", Assert.Single(details.Properties).Source);
    }
}
