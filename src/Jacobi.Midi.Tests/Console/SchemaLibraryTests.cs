using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Schemas;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The schema library (built-in assemblies, assemblies and schema files of rig, device and dashboard, compiled into
/// one set) and the checks of a dashboard against it, with the GP-8 schema: bindings must be fields of the device's
/// map, index variables must index repeats of the map and stay within their item count.
/// </summary>
public sealed class SchemaLibraryTests : IDisposable
{
    private const string Gp8 = "Gp8.AddressMap";

    private readonly string _directory = Directory.CreateTempSubdirectory("console-schemas-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static SchemaLibraryBuilder BuiltIn() => ConsoleLibrary.BuiltIn();

    private static SchemaLibrary Library => ConsoleLibrary.BuiltInLibrary;

    [Fact]
    public void BuiltInAssemblies_Compile()
    {
        Assert.True(Library.IsCompiled, String.Join(Environment.NewLine, Library.Problems));
        Assert.DoesNotContain(Library.Problems, problem => problem.IsError);
        Assert.True(Library.Maps!.IsMap(new(Gp8)));
    }

    [Fact]
    public void Binding_ToAFieldOfTheMap_IsResolved_WithTheItemCountOfItsVariable()
    {
        var row = Row(new IndexVariable("patch", 5),
            Knob("AddressMap.Memory[$patch].Patch.DynamicFilter.Sens"),
            Knob("AddressMap.Temporary.DynamicFilter.Sens"));

        var resolution = Resolve(row);

        Assert.Empty(resolution.Problems);
        Assert.Equal(2, resolution.Bindings.Count);
        Assert.Equal(new InstancePath([5]), resolution.Bindings[0].Parameter!.Instance);
        Assert.Equal(128, resolution.ItemCounts[new VariableReference(row, "patch")]);
    }

    [Theory]
    [InlineData("AddressMap.Temporary.DynamicFilter", "is not a field of 'Gp8.AddressMap'")]
    [InlineData("AddressMap.Temporary.Missing", "is not a field of 'Gp8.AddressMap'")]
    [InlineData("Temporary.DynamicFilter.Sens", "must start at the map root 'AddressMap'")]
    [InlineData("AddressMap.Temporary[$patch].DynamicFilter.Sens", "indexes 'AddressMap.Temporary', which is not a repeat")]
    public void Binding_ThatDoesNotFitTheMap_IsAProblem(string path, string message)
    {
        var resolution = Resolve(Row(new IndexVariable("patch"), Knob(path)));

        Assert.Empty(resolution.Bindings);
        Assert.Contains(message, Assert.Single(resolution.Problems).Message);
    }

    [Fact]
    public void Variable_BeyondTheItemCount_IsAProblem()
    {
        var row = Row(new IndexVariable("patch", 128), Knob("AddressMap.Memory[$patch].Patch.DynamicFilter.Sens"));

        var problem = Assert.Single(Resolve(row).Problems);

        Assert.Same(row, problem.Control);
        Assert.Contains("'$patch' is 128, but there are only 128 items (0-127)", problem.Message);
    }

    [Fact]
    public void DeviceWhoseMapRootIsNoMap_IsAProblem()
    {
        var dashboard = new Dashboard
        {
            Name = "Test",
            Devices = [new DashboardDevice("gp8", "Roland.Message16")],
            Layout = new LayoutControl { Kind = LayoutControlKind.Row },
        };

        var resolution = BindingResolver.Resolve(dashboard, new BindingContext { Library = Library });

        Assert.Contains("'Roland.Message16' (device 'gp8') is not an address map", Assert.Single(resolution.Problems).Message);
    }

    [Fact]
    public void SchemaFiles_AreAdded_AndALaterOneReplacesAnEarlierOneWithTheSameName()
    {
        var first = Write("first/Extra.xml", Schema("Extra", "First"));
        var second = Write("second/Extra.xml", Schema("Extra", "Second"));

        var library = BuiltIn()
            .Add(new SchemaSources { Schemas = ["first/Extra.xml"] }, _directory)
            .Add(new SchemaSources { Schemas = [second] })
            .Build();

        Assert.True(library.IsCompiled);
        var replaced = Assert.Single(library.Problems);
        Assert.False(replaced.IsError);
        Assert.Contains($"replaces the one of {first.Replace('/', Path.DirectorySeparatorChar)}", replaced.Message.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(library.Schemas!.TryFindByPath(new("Extra.Second.Value"), out _));
    }

    [Fact]
    public void SourcesThatCannotBeLoaded_AreReported_AndTheOthersStillCompile()
    {
        Write("Broken.xml", "<schema");

        var library = BuiltIn()
            .Add(new SchemaSources { Assemblies = ["Missing.dll"], Schemas = ["Missing.xml", "Broken.xml"] }, _directory)
            .Build();

        Assert.True(library.IsCompiled);
        Assert.Equal(3, library.Problems.Count);
        Assert.Contains(library.Problems, problem => problem.Source.EndsWith("Missing.dll") && problem.Message.Contains("cannot be loaded"));
        Assert.Contains(library.Problems, problem => problem.Source.EndsWith("Missing.xml") && problem.Message.Contains("does not exist"));
        Assert.Contains(library.Problems, problem => problem.Source.EndsWith("Broken.xml") && problem.Message.Contains("cannot be read"));
    }

    [Fact]
    public void SchemasThatDoNotCompile_AreReported_WithoutSerializer()
    {
        Write("Dangling.xml", """
            <schema name="Dangling">
              <members>
                <group name="Root">
                  <members>
                    <field name="Value" datatype="missing.type" />
                  </members>
                </group>
              </members>
            </schema>
            """);

        var library = BuiltIn().Add(new SchemaSources { Schemas = ["Dangling.xml"] }, _directory).Build();

        Assert.False(library.IsCompiled);
        Assert.Null(library.Serializer);
        Assert.Contains(library.Problems, problem => problem.Source == "compile" && problem.IsError);

        var resolution = BindingResolver.Resolve(Dashboard(Row(new IndexVariable("patch"))), new BindingContext { Library = library });
        Assert.Contains("do not compile", Assert.Single(resolution.Problems).Message);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Schema(string name, string root) => $"""
        <schema name="{name}">
          <members>
            <group name="{root}">
              <members>
                <field name="Value" datatype="sys.uint8" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private static BindingResolution Resolve(LayoutControl row)
        => BindingResolver.Resolve(Dashboard(row), new BindingContext { Library = Library });

    private static Dashboard Dashboard(LayoutControl row)
        => new()
        {
            Name = "GP-8",
            Devices = [new DashboardDevice("gp8", Gp8)],
            Layout = row,
        };

    private static LayoutControl Row(IndexVariable variable, params DashboardControl[] children)
        => new() { Kind = LayoutControlKind.Row, Device = "gp8", Variables = [variable], Children = children };

    private static ValueControl Knob(string path)
        => new(ParameterControlKind.Knob) { Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new(path) } };
}
