using System.Reflection;
using Jacobi.Midi.SchemaEditor.Assemblies;
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
    public void Load_AddsEmbeddedSchemasToTheSchemaSet()
    {
        var workspace = new AssemblyWorkspace();

        var loaded = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");

        Assert.NotEmpty(loaded.SchemaNames);
        Assert.False(loaded.HasProblems);
        Assert.Same(loaded, Assert.Single(workspace.Assemblies));
        // the schemas are part of the shared set: compiling resolves them
        Assert.NotNull(workspace.SchemaSet.Compile());
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
    public void Load_SharesOneSchemaSetAcrossAssembliesAndIgnoresAReopenedAssembly()
    {
        var workspace = new AssemblyWorkspace();
        var device = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");
        var roland = workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");

        Assert.Equal(2, workspace.Assemblies.Count);
        Assert.Same(device, workspace.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "again.dll"));
        Assert.Equal(2, workspace.Assemblies.Count);
        // the roland schemas include the base midi schema of the other assembly
        Assert.NotNull(workspace.SchemaSet.Compile());
        Assert.NotEqual(device.FullName, roland.FullName);
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
        viewModel.NodeName = "Changed";
        viewModel.ApplyNodeEditsCommand.Execute(null);
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
