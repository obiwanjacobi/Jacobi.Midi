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
    public void DeviceWhoseRootIsNoRootGroup_IsAProblem_AnyRootGroupIsNot()
    {
        Dashboard With(string root) => new()
        {
            Name = "Test",
            Devices = [new DashboardDevice("gp8", root)],
            Layout = new LayoutControl { Kind = LayoutControlKind.Row },
        };

        var problem = Assert.Single(BindingResolver.Resolve(With("Gp8.Nothing"), new BindingContext { Library = Library }).Problems);
        Assert.Contains("'Gp8.Nothing' (device 'gp8') is not a root group", problem.Message);
        // not an address map, but a root group of the schemas
        Assert.Empty(BindingResolver.Resolve(With("Roland.Message16"), new BindingContext { Library = Library }).Problems);
    }

    [Fact]
    public void DeviceWithAPlainRoot_WorksLikeAnyOther_ButHasNoDataMessages()
    {
        // a plain schema: groups, a repeat and fields, no address map
        const string schema = """
            <schema name="Plain">
              <members>
                <group name="Settings">
                  <members>
                    <field name="Level" datatype="sys.uint8"/>
                    <repeat name="Slot" count="4">
                      <members>
                        <field name="Value" datatype="sys.uint8"/>
                      </members>
                    </repeat>
                  </members>
                </group>
              </members>
            </schema>
            """;
        const string rig = """
            <rig format="jacobi.midi.rig/1">
              <device alias="plain" mapRoot="Plain.Settings" />
            </rig>
            """;
        const string dashboard = """
            <dashboard format="jacobi.midi.dashboard/1" name="Test">
              <device alias="plain" mapRoot="Plain.Settings" />
              <grid device="plain" base="Settings" size="20,10">
                <variable name="slot" value="2" />
                <numberBox at="0,0" size="6,4"><bind slot="value" parameter="Level" /></numberBox>
                <numberBox at="6,0" size="6,4"><bind slot="value" parameter="Slot[$slot].Value" /></numberBox>
                <numberBox at="12,0" size="6,4"><bind slot="value" parameter="$slot" /></numberBox>
              </grid>
            </dashboard>
            """;
        var path = Path.Combine(_directory, "Plain.xml");
        File.WriteAllText(path, schema);
        var library = ConsoleLibrary.BuiltIn().AddSchemaFile(path).Build();
        var plain = new Jacobi.BinarySerializer.Schema.SchemaName("Plain.Settings");
        Assert.True(library.HasRoot(plain));
        Assert.False(library.Maps!.IsMap(plain));
        Assert.Equal(["Plain.Settings"], library.RootsFromFile(path).Select(root => root.FullName));
        Assert.Equal(4, library.ItemCount(plain, "Settings.Slot"));

        var rigSession = Jacobi.Midi.Console.Core.Parameters.RigSession.Open(Jacobi.Midi.Console.Core.Rigs.RigFile.Read(rig, Jacobi.Midi.Console.Core.Files.FileFormat.Xml), library);
        using var session = Jacobi.Midi.Console.Core.Parameters.DashboardSession.Open(rigSession, DashboardFile.Read(dashboard, Jacobi.Midi.Console.Core.Files.FileFormat.Xml));

        Assert.Empty(rigSession.Problems);
        Assert.Empty(session.Problems);
        Assert.Equal(["Level", "Slot"], rigSession.Devices["plain"].Parameters.Children.Select(node => node.Name));
        var level = session.ValueOf((ParameterControl)session.Dashboard.Layout.Children[0], SlotNames.Value)!;
        level.Set(42m);
        Assert.Equal(42m, level.Number);
        Assert.Equal(new Jacobi.Midi.Console.Core.Faces.ValueRange(1, 4), session.ValueOf((ParameterControl)session.Dashboard.Layout.Children[2], SlotNames.Value)!.Info.Range);

        var load = rigSession.Devices["plain"].LoadDump([0xF0, 0x41, 0x00, 0x13, 0x12, 0x00, 0x00, 0x00, 0x00, 0xF7]);
        Assert.Equal(0, load.Decoded);
        Assert.Contains("not an address map", Assert.Single(load.Warnings));
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

    [Fact]
    public void MapRoots_OfASource_AreThoseOfItsDocuments_AlsoWhenTheyReplaceABuiltInOne()
    {
        // the GX-700 schema is built in (the Roland assembly); the same schema from a file replaces it
        var roland = typeof(Jacobi.Midi.Roland.Processors.ProcessorFactory).Assembly;
        var resource = roland.GetManifestResourceNames().Single(name => name.EndsWith("Gx700.xml", StringComparison.Ordinal));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-Gx700.xml");
        using (var stream = roland.GetManifestResourceStream(resource)!)
        using (var file = File.Create(path))
            stream.CopyTo(file);
        try
        {
            var library = ConsoleLibrary.BuiltIn().AddSchemaFile(path).Build();

            var fromFile = library.RootsFromFile(path).Select(name => name.ToString()).ToList();
            Assert.Equal("Gx700.AddressMap", fromFile[0]);
            Assert.All(fromFile, root => Assert.StartsWith("Gx700.", root));
            Assert.DoesNotContain("Gx700.AddressMap", library.RootsFromAssembly(roland.GetName().Name!).Select(name => name.ToString()));
            Assert.Contains("Gp8.AddressMap", library.RootsFromAssembly(roland.GetName().Name!).Select(name => name.ToString()));
            Assert.Contains(library.Problems, problem => !problem.IsError && problem.Source == path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
