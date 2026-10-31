using System.Reflection;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

/// <summary>
/// Tests the Schema Editor's use of the address map validation: <see cref="AddressMapCheck"/> relates the problems to the
/// authored nodes (<see cref="CompiledNodeLocator.FindAuthored"/>), and <see cref="EditNode.AddressTag"/> marks the nodes.
/// </summary>
public class AddressMapCheckTests
{
    private static (MainViewModel ViewModel, EditDocument Roland, EditDocument Gp8) LoadRoland()
    {
        var viewModel = new MainViewModel();
        var loaded = viewModel.OpenedAssemblies.Load(
            Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        return (viewModel,
            loaded.Schemas.Single(schema => schema.Name == "Roland").Document.Schema,
            loaded.Schemas.Single(schema => schema.Name == "Gp8").Document.Schema);
    }

    private static EditNode Member(EditNode node, string name)
        => ((EditGroup)node).Members.Single(member => member.Name == name);

    [Fact]
    public void Run_Gp8_TheDeviceIdIsSuppliedByTheClient()
    {
        var (viewModel, roland, gp8) = LoadRoland();

        var issues = AddressMapCheck.Run(viewModel.LoadedSchemas.Compile(gp8), viewModel.OpenedAssemblies.Processors);

        Assert.All(issues, issue => Assert.True(issue.IsInformation, issue.Problem.ToString()));
        var dt1DeviceId = Member(Member(roland.Members.Single(member => member.Name == "Message16"), "DT1"), "DeviceId");
        Assert.Contains(issues, issue => ReferenceEquals(issue.Node, dt1DeviceId));
    }

    [Fact]
    public void Run_LeavesOutTheIssuesOfDocumentsThatAreNotIncluded()
    {
        var (viewModel, roland, _) = LoadRoland();

        var issues = AddressMapCheck.Run(viewModel.LoadedSchemas.Compile(roland), viewModel.OpenedAssemblies.Processors);

        Assert.NotEmpty(issues);
        Assert.All(issues, issue => Assert.Equal("Roland", issue.Problem.Document, ignoreCase: true));
    }

    [Fact]
    public void FindAuthored_FollowsTheNodeDefs()
    {
        var (viewModel, _, gp8) = LoadRoland();
        var compilation = viewModel.LoadedSchemas.Compile(gp8);

        var node = CompiledNodeLocator.FindAuthored(compilation.Documents, "Gp8", new SchemaPath("AddressMap.Memory.Patch.EffectSwiches1"));

        var patch = gp8.NodeDefs.Single(def => def.Name == "patch");
        Assert.Same(Member(patch, "EffectSwiches1"), node);
        Assert.Null(CompiledNodeLocator.FindAuthored(compilation.Documents, "Gp8", new SchemaPath("AddressMap.Missing")));
    }

    [Fact]
    public void AddressTag_FollowsTheProperties()
    {
        var field = new EditField(null) { Name = "F", Processors = [], Properties = [] };
        Assert.Null(field.AddressTag);
        var changed = new List<string?>();
        field.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var role = new EditProperty { Name = "midi.role", Value = "address" };
        field.Properties.Add(role);
        Assert.Equal("address", field.AddressTag);

        role.Value = "size";
        Assert.Equal("size", field.AddressTag);
        Assert.Contains(nameof(EditNode.AddressTag), changed);

        field.Properties.Remove(role);
        Assert.Null(field.AddressTag);

        // a message has no kind in the schema (an exchange file gives it its part): only roles are shown
        var group = new EditGroup(null) { Name = "G", Members = [], Processors = [], Properties = [new EditProperty { Name = "midi.message", Value = "request" }] };
        Assert.Null(group.AddressTag);
    }
}
