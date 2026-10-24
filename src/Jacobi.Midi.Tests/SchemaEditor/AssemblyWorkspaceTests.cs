using System.Reflection;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class AssemblyWorkspaceTests
{
    [Fact]
    public void MainViewModel_PreloadsTheSysAndMidiProcessors()
    {
        var viewModel = new MainViewModel();

        Assert.Contains("sys", viewModel.OpenedAssemblies.Processors.Namespaces, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("midi", viewModel.OpenedAssemblies.Processors.Namespaces, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(viewModel.OpenedAssemblies.Assemblies, assembly => assembly.Schemas.Any(schema => schema.Name == "Midi"));
        Assert.All(viewModel.OpenedAssemblies.Assemblies, assembly => Assert.False(assembly.HasProblems, String.Join("; ", assembly.Problems)));
    }

    [Fact]
    public void ProcessorNamespace_DescribesTheProcessorsWithTheirProperties()
    {
        var viewModel = new MainViewModel();
        var sys = viewModel.OpenedAssemblies.Assemblies.SelectMany(assembly => assembly.ProcessorNamespaces).Single(item => item.Namespace == "sys");

        Assert.Equal(sys.Processors.Count, sys.ProcessorInfos.Count);
        Assert.Contains("align", sys.Processors);
        var align = sys.ProcessorInfos.Single(info => info.Key == "sys.align");
        Assert.Null(align.Problem);
        Assert.Equal("Layout", align.StageText);
        var bytes = align.Properties.Single(property => property.Name == "bytes");
        Assert.Equal("sys.int32", bytes.DataType);
        Assert.Equal("required", bytes.Flags);
        Assert.NotEmpty(bytes.Description);
        // the options of a data type published by the processor factory
        Assert.Contains("Root", align.Properties.Single(property => property.Name == "relative").Values);
        Assert.True(sys.ProcessorInfos.Single(info => info.Key == "sys.null").HasNoProperties);
    }

    [Fact]
    public void Includes_OfSchemasEmbeddedInOpenedAssembliesAreLoaded()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var document = viewModel.SelectedDocument!;

        var midi = document.AddInclude("Midi");
        var roland = document.AddInclude("Roland");
        var unknown = document.AddInclude("Unknown");

        Assert.True(midi.IsLoaded);
        Assert.False(roland.IsLoaded);
        Assert.False(unknown.IsLoaded);

        viewModel.OpenedAssemblies.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");

        Assert.True(roland.IsLoaded);
        Assert.False(unknown.IsLoaded);
    }

    [Fact]
    public void NodeDefOptions_AreTheNodeDefsOfTheSameKindLocalAndIncluded()
    {
        var viewModel = new MainViewModel();
        viewModel.NewDocumentCommand.Execute(null);
        var library = viewModel.SelectedDocument!.Schema;
        library.NodeDefs.Add(NewField("libraryField"));
        library.NodeDefs.Add(NewGroup("libraryGroup"));
        viewModel.NewDocumentCommand.Execute(null);
        var document = viewModel.SelectedDocument!;
        var schema = document.Schema;
        document.AddInclude(library.Name);
        schema.NodeDefs.Add(NewField("localField"));
        schema.NodeDefs.Add(NewGroup("localGroup"));
        var root = (EditGroup)schema.Members[0];
        var field = new EditField(root) { Name = "field", Processors = [], Properties = [] };
        root.Members.Add(field);

        viewModel.SelectedItem = field;

        Assert.Equal(["localField", $"{library.Name}.libraryField"], viewModel.NodeDefOptions);
        Assert.Equal(["localGroup", $"{library.Name}.libraryGroup"], viewModel.MemberNodeDefOptions);
    }

    private static EditField NewField(string name) => new(null) { Name = name, Processors = [], Properties = [] };
    private static EditGroup NewGroup(string name) => new(null) { Name = name, Members = [], Processors = [], Properties = [] };

    [Fact]
    public void Load_ReadsTheEmbeddedSchemas()
    {
        var workspace = new AssemblyWorkspace();

        var loaded = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");

        Assert.NotEmpty(loaded.SchemaNames);
        Assert.False(loaded.HasProblems);
        Assert.Same(loaded, Assert.Single(workspace.Assemblies));
        // the schemas are loaded schemas: they compile
        var schemas = new SchemaSetWorkspace([], workspace);
        Assert.All(loaded.Schemas, schema => Assert.Empty(schemas.Compile(schema.Document.Schema).Warnings));
    }

    [Fact]
    public void Load_PassesTheSameAssemblyToTheProcessorManager()
    {
        var workspace = new AssemblyWorkspace();

        var loaded = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");

        Assert.Equal(["roland"], loaded.ProcessorNamespaces.Select(item => item.Namespace));
        Assert.True(workspace.Processors.TryGetFactory("roland", out _));
        Assert.NotEmpty(loaded.SchemaNames);
    }

    [Fact]
    public void Load_CompilesTheSchemasAcrossAssembliesAndIgnoresAReopenedAssembly()
    {
        var workspace = new AssemblyWorkspace();
        var device = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");
        var roland = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");

        Assert.Equal(2, workspace.Assemblies.Count);
        Assert.Same(device, workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "again.dll"));
        Assert.Equal(2, workspace.Assemblies.Count);
        // the roland schemas include the base midi schema of the other assembly
        var schemas = new SchemaSetWorkspace([], workspace);
        Assert.All(roland.Schemas, schema => Assert.Empty(schemas.Compile(schema.Document.Schema).Warnings));
        Assert.NotEqual(device.FullName, roland.FullName);
    }

    [Fact]
    public void Load_DoesNotAddAnAssemblyWithoutSchemasOrProcessors()
    {
        var workspace = new AssemblyWorkspace();

        var loaded = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Core")), "Jacobi.Midi.Core.dll");

        Assert.True(loaded.IsEmpty);
        Assert.Empty(workspace.Assemblies);
    }

    [Fact]
    public void Load_ReportsProcessorsThatAreAlreadyRegistered()
    {
        var workspace = new AssemblyWorkspace();
        workspace.Processors.Register(new Jacobi.Midi.Roland.Processors.ProcessorFactory());

        var loaded = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");

        Assert.Contains(loaded.Problems, problem => problem.StartsWith("Processors:", StringComparison.Ordinal));
        Assert.NotEmpty(loaded.SchemaNames);
    }
}

public class AssemblyInspectionTests
{
    private static (MainViewModel ViewModel, LoadedAssembly Assembly) OpenRoland()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;
        return (viewModel, loaded);
    }

    [Fact]
    public void SelectingAnEmbeddedSchema_ShowsItReadOnlyWithoutOpeningADocument()
    {
        var (viewModel, loaded) = OpenRoland();
        var schema = loaded.Schemas[0];

        viewModel.SelectedAssemblyItem = schema;

        Assert.Empty(viewModel.Documents);
        Assert.Null(viewModel.SelectedDocument);
        Assert.Same(schema.Document, viewModel.ActiveDocument);
        Assert.True(viewModel.IsReadOnly);
        Assert.True(schema.Document.IsReadOnly);
        Assert.Same(schema.Document.Schema, viewModel.SelectedItem);
        // the document tabs of the root show the collections of the embedded schema
        Assert.NotNull(viewModel.DocumentTabs);
        Assert.Equal(schema.Name, viewModel.SelectedItemPath);
    }

    [Fact]
    public void AnEmbeddedSchema_CannotBeChanged()
    {
        var (viewModel, loaded) = OpenRoland();
        var schema = loaded.Schemas.First(candidate => candidate.Document.Schema.Members.Count > 0);
        var node = schema.Document.Schema.Members[0];
        var revision = schema.Document.Revision;

        viewModel.SelectedAssemblyItem = node;
        Assert.Same(node, viewModel.SelectedItem);
        Assert.Same(schema.Document, viewModel.ActiveDocument);

        var name = node.Name;
        viewModel.NodeFacets.Name = "Changed";
        Assert.Equal(name, node.Name);

        Assert.False(viewModel.CanAddMembers);
        Assert.False(viewModel.DeleteItemCommand.CanExecute(null));
        Assert.Throws<InvalidOperationException>(() => schema.Document.MarkChanged());
        Assert.Equal(revision, schema.Document.Revision);
        Assert.False(schema.Document.IsDirty);
    }

    [Fact]
    public void SelectingAnAssemblyOrProcessorNamespace_ShowsTheAssemblyInsteadOfTheNodeEditor()
    {
        var (viewModel, loaded) = OpenRoland();

        viewModel.SelectedAssemblyItem = loaded;
        Assert.Same(loaded, viewModel.InspectedAssembly);
        Assert.False(viewModel.ShowNodeEditor);
        Assert.Null(viewModel.SelectedItem);

        viewModel.SelectedAssemblyItem = loaded.ProcessorNamespaces[0];
        Assert.Null(viewModel.InspectedAssembly);
        Assert.Same(loaded.ProcessorNamespaces[0], viewModel.InspectedNamespace);
        Assert.False(viewModel.ShowNodeEditor);
        Assert.Equal(["checksum"], viewModel.InspectedNamespace.Processors);
        Assert.Equal(["roland.checksum"], viewModel.InspectedNamespace.ProcessorKeys);

        viewModel.SelectedAssemblyItem = loaded.Schemas[0];
        Assert.Null(viewModel.InspectedAssembly);
        Assert.True(viewModel.ShowNodeEditor);
    }

    [Fact]
    public void LeavingTheAssembliesTab_ClearsTheInspection()
    {
        var (viewModel, loaded) = OpenRoland();
        viewModel.SelectedAssemblyItem = loaded.Schemas[0];

        viewModel.SelectedLeftTab = MainViewModel.SchemaLeftTab;

        Assert.Null(viewModel.SelectedAssemblyItem);
        Assert.Null(viewModel.InspectedDocument);
        Assert.Null(viewModel.SelectedItem);
        Assert.False(viewModel.IsReadOnly);

        // an open schema document is edited as before, and picking it leaves the assemblies tab
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.NewDocumentCommand.Execute(null);
        viewModel.SelectedLeftTab = MainViewModel.AssembliesLeftTab;
        viewModel.SelectedAssemblyItem = loaded.Schemas[0];
        Assert.True(viewModel.IsReadOnly);
        viewModel.SelectedDocument = viewModel.Documents[0];
        Assert.Equal(MainViewModel.SchemaLeftTab, viewModel.SelectedLeftTab);
        Assert.False(viewModel.IsReadOnly);
        Assert.Same(viewModel.SelectedDocument, viewModel.ActiveDocument);
    }
}
