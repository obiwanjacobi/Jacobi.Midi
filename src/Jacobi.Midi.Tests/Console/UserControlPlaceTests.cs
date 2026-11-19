using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Libraries;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// Placing a user control with the GX-700 schema: a copy of its section on the base around the drop, checked against
/// its nodeDef; its bindings resolve below that base.
/// </summary>
public class UserControlPlaceTests
{
    private const string Library = """
        <library format="jacobi.midi.library/1">
          <userControl name="Patch" nodeDef="Gx700.patch">
            <section size="20,12">
              <section label="Compressor" at="0,0" size="8,9" base="Compressor">
                <knob label="Sustain" at="0,0" size="5,7"><bind slot="value" parameter="Sustain" /></knob>
              </section>
              <knob label="Attack" at="9,0" size="5,7"><bind slot="value" parameter="Compressor.Attack" /></knob>
            </section>
          </userControl>
          <userControl name="Compressor" nodeDef="Gx700.compressor">
            <section size="8,9" base="Compressor">
              <knob label="Sustain" at="0,0" size="5,7"><bind slot="value" parameter="Sustain" /></knob>
            </section>
          </userControl>
        </library>
        """;

    // a section on the temporary patch, one on a user patch, and one on the system (another nodeDef)
    private const string Dashboard = """
        <dashboard format="jacobi.midi.dashboard/1" name="Test">
          <device alias="fx" schema="Gx700" />
          <grid device="fx" size="80,30">
            <variable name="patch" value="3" />
            <section label="Temporary" at="0,0" size="25,20" base="AddressMap.Temporary" />
            <section label="User" at="26,0" size="25,20" base="AddressMap.Memory[$patch].Patch" />
            <section label="System" at="52,0" size="25,20" base="AddressMap.System" />
          </grid>
        </dashboard>
        """;

    private static UserControlDefinition Patch => ControlLibraryFile.Read(Library, FileFormat.Xml).Find("Patch")!;

    private static UserControlDefinition Compressor => ControlLibraryFile.Read(Library, FileFormat.Xml).Find("Compressor")!;

    private static LayoutControl Target(Dashboard dashboard, string label)
        => (LayoutControl)dashboard.Layout.Children.Single(child => child.Label == label);

    [Theory]
    [InlineData("Temporary", "AddressMap.Temporary", 0)]
    [InlineData("User", "AddressMap.Memory.Patch", 3)]
    public void Plan_OnABaseWithTheNodeDef_GetsThatBase_AndItsBindingsResolveBelowIt(string target, string path, int item)
    {
        var dashboard = DashboardFile.Read(Dashboard, FileFormat.Xml);
        var container = Target(dashboard, target);

        var plan = UserControlPlace.Plan(dashboard, container, Patch, ConsoleLibrary.BuiltInLibrary);
        container.Children = [plan.Section];

        Assert.Empty(plan.Problems);
        Assert.Equal(container.Base, plan.Section.Base);
        var resolution = BindingResolver.Resolve(dashboard, new BindingContext { Library = ConsoleLibrary.BuiltInLibrary });
        Assert.Empty(resolution.Problems);
        Assert.Equal(
            [$"{path}.Compressor.Sustain", $"{path}.Compressor.Attack"],
            resolution.Bindings.Select(binding => binding.Parameter!.Path));
        if (item > 0)
            Assert.All(resolution.Bindings, binding => Assert.Equal(item, binding.Parameter!.Instance[0]));
    }

    [Fact]
    public void Plan_OnABaseWithAnotherNodeDef_IsReported_AndPlacedWithoutABase()
    {
        var dashboard = DashboardFile.Read(Dashboard, FileFormat.Xml);

        var plan = UserControlPlace.Plan(dashboard, Target(dashboard, "System"), Patch, ConsoleLibrary.BuiltInLibrary);

        Assert.Null(plan.Section.Base);
        Assert.Contains("is a 'Gx700.system', not a 'Gx700.patch'", Assert.Single(plan.Problems));
    }

    [Theory]
    [InlineData("Temporary", "AddressMap.Temporary.Compressor")]
    [InlineData("User", "AddressMap.Memory[$patch].Patch.Compressor")]
    public void Plan_OfASectionWithABase_GetsTheBaseAroundFollowedByItsOwn(string target, string fullBase)
    {
        var dashboard = DashboardFile.Read(Dashboard, FileFormat.Xml);
        var container = Target(dashboard, target);

        var plan = UserControlPlace.Plan(dashboard, container, Compressor, ConsoleLibrary.BuiltInLibrary);
        container.Children = [plan.Section];

        Assert.Empty(plan.Problems);
        Assert.Equal(("fx", fullBase), (plan.Section.Device, plan.Section.Base));
        var resolution = BindingResolver.Resolve(dashboard, new BindingContext { Library = ConsoleLibrary.BuiltInLibrary });
        Assert.Empty(resolution.Problems);
        Assert.EndsWith(".Compressor.Sustain", Assert.Single(resolution.Bindings).Parameter!.Path);
    }

    // the full base is not a node: not a problem of the placement, but of the resolution (checked on each change)
    [Fact]
    public void Plan_WhereTheFullBaseIsNotANode_KeepsItsOwnBase_AndTheResolutionReportsIt()
    {
        var dashboard = DashboardFile.Read(Dashboard, FileFormat.Xml);
        var container = Target(dashboard, "System");

        var plan = UserControlPlace.Plan(dashboard, container, Compressor, ConsoleLibrary.BuiltInLibrary);
        container.Children = [plan.Section];

        Assert.Empty(plan.Problems);
        Assert.Equal((null, "Compressor"), (plan.Section.Device, plan.Section.Base));
        var resolution = BindingResolver.Resolve(dashboard, new BindingContext { Library = ConsoleLibrary.BuiltInLibrary });
        Assert.Contains(resolution.Problems, problem => problem.Control == plan.Section && problem.Message.Contains("'AddressMap.System.Compressor' is not a node"));
    }

    // placed on an empty grid: its bindings have no device; once the grid has a device and base, they resolve
    [Fact]
    public void Plan_WithoutABaseAround_IsNotReported_AndResolvesOnceTheBaseAroundIsSet()
    {
        var dashboard = DashboardFile.Read("""
            <dashboard format="jacobi.midi.dashboard/1" name="Test">
              <device alias="fx" schema="Gx700" />
              <grid size="80,30" />
            </dashboard>
            """, FileFormat.Xml);
        var grid = dashboard.Layout;

        var plan = UserControlPlace.Plan(dashboard, grid, Compressor, ConsoleLibrary.BuiltInLibrary);
        grid.Children = [plan.Section];

        Assert.Empty(plan.Problems);
        var context = new BindingContext { Library = ConsoleLibrary.BuiltInLibrary };
        Assert.Contains(BindingResolver.Resolve(dashboard, context).Problems, problem => problem.Message.Contains("has no device"));

        (grid.Device, grid.Base) = ("fx", "AddressMap.Temporary");
        var resolution = BindingResolver.Resolve(dashboard, context);

        Assert.Empty(resolution.Problems);
        Assert.Equal("AddressMap.Temporary.Compressor.Sustain", Assert.Single(resolution.Bindings).Parameter!.Path);
    }

    [Fact]
    public void Plan_PlacesACopy()
    {
        var dashboard = DashboardFile.Read(Dashboard, FileFormat.Xml);
        var patch = Patch;

        var first = UserControlPlace.Plan(dashboard, Target(dashboard, "Temporary"), patch, ConsoleLibrary.BuiltInLibrary).Section;
        var second = UserControlPlace.Plan(dashboard, Target(dashboard, "User"), patch, ConsoleLibrary.BuiltInLibrary).Section;

        Assert.NotSame(first, second);
        Assert.NotSame(patch.Section.Children[0], first.Children[0]);
        Assert.Null(patch.Section.Base);
    }

    // a group dropped from the parameter tree, saved as a user control (keeping its last node as its base) and placed
    // on another patch: all its bindings are there, on the same node of the other patch
    [Fact]
    public void DroppedGroup_SavedAndPlaced_KeepsAllItsBindings()
    {
        var rig = RigSession.Open(RigFile.Read("""<rig format="jacobi.midi.rig/1"><device alias="fx" schema="Gx700" /></rig>""", FileFormat.Xml), ConsoleLibrary.BuiltInLibrary);
        using var session = DashboardSession.Open(rig, DashboardFile.Read("""
            <dashboard format="jacobi.midi.dashboard/1" name="Test">
              <device alias="fx" schema="Gx700" />
              <grid device="fx" size="200,200">
                <section label="User patch 4" at="0,0" size="10,10" base="AddressMap.Memory[3].Patch" />
              </grid>
            </dashboard>
            """, FileFormat.Xml));
        var root = session.Dashboard.Layout;
        var node = rig.Devices["fx"].Parameters.Select(top => Find(top, "AddressMap.Temporary.Compressor")).First(found => found is not null)!;
        var drop = ControlDrops.Drop(session.Dashboard, session.Resolution, new DropSource("fx", "Gx700", node), root);
        root.Variables = [.. drop.Variables.Select(variable => variable.Variable)];
        root.Children = [.. root.Children, drop.Control!];
        session.Refresh();
        var dropped = (LayoutControl)drop.Control!;
        var before = session.Resolution.Bindings.Count(binding => IsIn(dropped, binding.Control));
        Assert.True(before > 3);

        var saved = UserControlSave.Plan(session.Dashboard, dropped, ConsoleLibrary.BuiltInLibrary);
        Assert.Empty(saved.Problems);
        var library = new ControlLibrary();
        library.Put(saved.Control!);
        var read = ControlLibraryFile.Read(ControlLibraryFile.Write(library, FileFormat.Xml), FileFormat.Xml).Controls.Single();
        var target = (LayoutControl)root.Children[0];
        var placed = UserControlPlace.Plan(session.Dashboard, target, read, ConsoleLibrary.BuiltInLibrary);
        Assert.Empty(placed.Problems);
        target.Children = [placed.Section];
        session.Refresh();

        Assert.Empty(session.Problems);
        var after = session.Resolution.Bindings.Where(binding => IsIn(placed.Section, binding.Control)).ToList();
        Assert.Equal(before, after.Count);
        Assert.All(after.Where(binding => binding.Parameter is not null), binding => Assert.StartsWith("AddressMap.Memory.Patch.Compressor.", binding.Parameter!.Path));

        static bool IsIn(DashboardControl control, DashboardControl other) => ControlMoves.Contains(control, other);
        static ParameterNode? Find(ParameterNode node, string path)
            => node.Path == path ? node : node.Children.Select(child => Find(child, path)).FirstOrDefault(found => found is not null);
    }
}
