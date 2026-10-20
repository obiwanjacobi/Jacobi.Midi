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
    public void DocumentTabs_AddEachDocumentItemSelectItAndSurviveSave()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;

        foreach (var kind in new[] { SchemaNodeKind.Field, SchemaNodeKind.Group, SchemaNodeKind.Repeat, SchemaNodeKind.Choice })
        {
            viewModel.AddNodeDefCommand.Execute(kind);
            // the node def is edited in the Node Defs tab: the document root stays selected
            Assert.Same(schema, viewModel.SelectedItem);
            Assert.Same(schema.NodeDefs[^1], viewModel.SelectedNodeDef);
            Assert.Equal(MainViewModel.NodeDefsTab, viewModel.SelectedEditorTab);
            Assert.Null(schema.NodeDefs[^1].Parent);
        }
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Field);
        Assert.Equal(["NewFieldDef", "NewGroupDef", "NewRepeatDef", "NewChoiceDef", "NewFieldDef2"], schema.NodeDefs.Select(node => node.Name));

        // the collection tabs belong to the document root
        Assert.True(viewModel.IsDocumentSelected);
        var tabs = viewModel.DocumentTabs!;

        tabs.Includes.AddCommand.Execute(null);
        Assert.Same(Assert.Single(schema.Includes), tabs.Includes.SelectedEntry);
        Assert.IsType<DocumentRefEditor>(tabs.Includes.Editor);
        Assert.Null(tabs.Includes.Editor.Details);

        tabs.DataTypeDefs.AddCommand.Execute(null);
        Assert.Same(Assert.Single(schema.DataTypeDefs), tabs.DataTypeDefs.SelectedEntry);
        Assert.True(tabs.DataTypeDefs.Editor!.Details!.HasProcessors);

        tabs.ProcessorDefs.AddCommand.Execute(null);
        Assert.Same(Assert.Single(schema.ProcessorDefs), tabs.ProcessorDefs.SelectedEntry);
        Assert.False(tabs.ProcessorDefs.Editor!.Details!.HasProcessors);
        Assert.NotNull(tabs.ProcessorDefs.Editor.Details.Definition);

        // the processors and properties of the document are edited in its General tab
        Assert.True(viewModel.ItemDetails!.HasProcessors && viewModel.ItemDetails.HasPropertiesTab);
        Assert.False(viewModel.ItemDetails.ShowProcessorsTab || viewModel.ItemDetails.ShowPropertiesTab);
        viewModel.AddItemProcessorCommand.Execute(null);
        viewModel.AddItemPropertyCommand.Execute(null);
        Assert.Single(schema.Processors);
        Assert.Single(schema.Properties);
        Assert.Equal(MainViewModel.GeneralTab, viewModel.SelectedEditorTab);

        var saved = SchemaFileService.Serialize(schema, SchemaFormat.Json);
        var restored = SchemaFileService.Parse(saved, SchemaFormat.Json);
        Assert.Equal([typeof(EditField), typeof(EditGroup), typeof(EditRepeat), typeof(EditChoice), typeof(EditField)],
            restored.NodeDefs.Select(node => node.GetType()));
        Assert.Single(restored.Includes);
        Assert.Single(restored.DataTypeDefs);
        Assert.Single(restored.ProcessorDefs);
        Assert.Single(restored.Processors);
        Assert.Single(restored.Properties);

        // leaving the root disposes the tabs
        viewModel.SelectedItem = schema.NodeDefs[0];
        Assert.Null(viewModel.DocumentTabs);
        Assert.False(viewModel.IsDocumentSelected);
    }

    [Fact]
    public void DocumentTab_RemoveDeletesTheSelectedItem()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        viewModel.SelectedItem = schema;
        var includes = viewModel.DocumentTabs!.Includes;

        Assert.False(includes.RemoveCommand.CanExecute(null));
        includes.AddCommand.Execute(null);
        includes.AddCommand.Execute(null);
        Assert.Equal(2, schema.Includes.Count);

        Assert.True(includes.RemoveCommand.CanExecute(null));
        includes.RemoveCommand.Execute(null);
        Assert.Single(schema.Includes);
        Assert.Null(includes.SelectedEntry);
        Assert.Null(includes.Editor);
        Assert.False(includes.RemoveCommand.CanExecute(null));
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
    public void AddOptions_DependOnSelectedNode()
    {
        var viewModel = new MainViewModel();
        Assert.False(viewModel.CanAdd);
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        var root = Assert.IsType<EditGroup>(Assert.Single(schema.Members));

        // nothing selected, or the document root: members go into the first top-level group
        Assert.True(viewModel.CanAddMembers);
        viewModel.SelectedItem = schema;
        Assert.True(viewModel.CanAddMembers);
        Assert.True(viewModel.CanAddItemProcessor && viewModel.CanAddItemProperty);

        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
        Assert.Same(root, Assert.IsType<EditField>(viewModel.SelectedNode).Parent);

        var open = new EditChoice(root) { Name = "Body", MemberNodeDef = new("BodyType"), Members = [], Processors = [], Properties = [], ValueProcessors = [] };
        root.Members.Add(open);
        viewModel.SelectedItem = open;
        Assert.False(viewModel.CanAddMembers);
        // only its own processors and properties can be added
        Assert.True(viewModel.CanAddItemProcessor && viewModel.CanAddItemProperty);
        Assert.False(viewModel.ShowItemEntriesSeparator);

        // a group node def accepts members; a field node def does not
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Group);
        Assert.True(viewModel.CanAddMembers);
        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
        Assert.Same(schema, viewModel.SelectedItem);
        Assert.Same(schema.NodeDefs[0], Assert.IsType<EditField>(viewModel.SelectedNodeDef).Parent);
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Field);
        Assert.False(viewModel.CanAddMembers);
    }

    [Fact]
    public void AddItemEntries_AddToSelectedItemAndShowTheirTab()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var root = Assert.IsType<EditGroup>(Assert.Single(viewModel.SelectedDocument!.Schema.Members));
        // a new document selects its root
        Assert.Same(viewModel.SelectedDocument.Schema, viewModel.SelectedItem);

        viewModel.SelectedItem = root;
        Assert.True(viewModel.CanAddItemProcessor && viewModel.CanAddItemProperty);
        Assert.True(viewModel.ShowItemEntriesSeparator);

        viewModel.AddItemProcessorCommand.Execute(null);
        Assert.Single(root.Processors);
        Assert.Equal(MainViewModel.ProcessorsTab, viewModel.SelectedEditorTab);

        viewModel.AddItemPropertyCommand.Execute(null);
        Assert.Equal("property", Assert.Single(root.Properties).Name);
        Assert.Equal(MainViewModel.PropertiesTab, viewModel.SelectedEditorTab);

        // the document has no Processors and Properties tabs: its entries are in the General tab
        viewModel.SelectedItem = viewModel.SelectedDocument!.Schema;
        viewModel.AddItemPropertyCommand.Execute(null);
        Assert.Equal(MainViewModel.GeneralTab, viewModel.SelectedEditorTab);

        // a document tab does not exist for a node
        viewModel.SelectedItem = viewModel.SelectedDocument.Schema;
        viewModel.SelectedEditorTab = MainViewModel.DataTypeDefsTab;
        viewModel.SelectedItem = root;
        Assert.Equal(MainViewModel.GeneralTab, viewModel.SelectedEditorTab);
    }

    [Fact]
    public void DeleteItem_RemovesNodesAndSelectsTheirParent()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Group);

        Assert.True(viewModel.DeleteItemCommand.CanExecute(null));
        viewModel.DeleteItemCommand.Execute(null);
        Assert.Empty(schema.NodeDefs);
        Assert.Same(schema, viewModel.SelectedItem);
        Assert.Null(viewModel.SelectedNodeDef);

        // the document itself cannot be deleted
        Assert.False(viewModel.DeleteItemCommand.CanExecute(null));
    }

    [Fact]
    public void NodeDef_IsEditedNextToTheNodeDefsTab()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Group);
        var definition = Assert.IsType<EditGroup>(viewModel.SelectedNodeDef);

        // the staged values and the details are those of the node def; the document keeps its own
        Assert.Equal("NewGroupDef", viewModel.NodeName);
        Assert.Equal(schema.Name, viewModel.DocumentName);
        Assert.True(viewModel.SelectedNodeDefDetails!.ShowProcessorsTab);
        Assert.Equal($"{schema.Name}/NodeDefs/NewGroupDef", viewModel.SelectedItemPath);

        viewModel.NodeName = "Renamed";
        viewModel.ApplyNodeEditsCommand.Execute(null);
        Assert.Equal("Renamed", definition.Name);
        Assert.Equal(schema.Name, viewModel.SelectedDocument!.Schema.Name);
        Assert.Same(schema, viewModel.SelectedItem);

        viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
        Assert.Same(definition, Assert.IsType<EditField>(viewModel.SelectedNodeDef).Parent);
        Assert.Same(schema, viewModel.SelectedItem);

        // deleting the nested node selects its parent node def; selecting another item forgets the node def
        viewModel.DeleteItemCommand.Execute(null);
        Assert.Same(definition, viewModel.SelectedNodeDef);
        viewModel.SelectedItem = schema.Members[0];
        Assert.Null(viewModel.SelectedNodeDef);
        Assert.Null(viewModel.SelectedNodeDefDetails);
    }

    [Fact]
    public void SelectedItemPath_StartsWithTheDocumentName()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var schema = viewModel.SelectedDocument!.Schema;
        var root = Assert.IsType<EditGroup>(Assert.Single(schema.Members));

        Assert.Equal(schema.Name, viewModel.SelectedItemPath);
        viewModel.SelectedItem = schema;
        Assert.Equal(schema.Name, viewModel.SelectedItemPath);
        viewModel.SelectedItem = root;
        Assert.Equal($"{schema.Name}/{root.Name}", viewModel.SelectedItemPath);

        viewModel.AddNodeDefCommand.Execute(SchemaNodeKind.Group);
        Assert.Equal($"{schema.Name}/NodeDefs/NewGroupDef", viewModel.SelectedItemPath);
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
