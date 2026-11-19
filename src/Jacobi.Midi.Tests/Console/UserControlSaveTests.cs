using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Libraries;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// "Save as user control" with the GX-700 schema: the contract is the nodeDef of the section's base, the bases and
/// bindings become relative, and what does not fit a user control is reported.
/// </summary>
public class UserControlSaveTests
{
    // a grid on the user patches with a section on a patch; inside a section with its own device and full base, one
    // with a relative base, and a binding relative to the section
    private const string Dashboard = """
        <dashboard format="jacobi.midi.dashboard/1" name="Test">
          <device alias="fx" schema="Gx700" />
          <device alias="other" schema="Gx700" />
          <grid device="fx" base="AddressMap.Memory[$patch]" size="40,30">
            <variable name="patch" value="3" />
            <section label="Patch" at="1,1" size="30,20" base="Patch">
              <section label="Compressor" at="0,0" size="8,9" device="fx" base="AddressMap.Memory[$patch].Patch.Compressor">
                <knob label="Sustain" at="0,0" size="5,7"><bind slot="value" parameter="Sustain" /></knob>
              </section>
              <section label="Wah" at="9,0" size="8,9" base="Wah">
                <switch label="On" at="0,0" size="5,7"><bind slot="value" parameter="On" /></switch>
              </section>
              <knob label="Attack" at="18,0" size="5,7"><bind slot="value" parameter="Compressor.Attack" /></knob>
            </section>
          </grid>
        </dashboard>
        """;

    private static Dashboard Read(string dashboard = Dashboard) => DashboardFile.Read(dashboard, FileFormat.Xml);

    private static LayoutControl Patch(Dashboard dashboard) => (LayoutControl)dashboard.Layout.Children[0];

    [Fact]
    public void Plan_GivesAUserControl_WithTheNodeDefOfTheBase_TheLastNodeOfTheBase_AndRelativeBasesAndBindings()
    {
        var dashboard = Read();

        var plan = UserControlSave.Plan(dashboard, Patch(dashboard), ConsoleLibrary.BuiltInLibrary);

        Assert.Empty(plan.Problems);
        var control = plan.Control!;
        Assert.Equal(("Patch", "Gx700.patch"), (control.Name, control.NodeDef.FullName));
        var section = control.Section;
        Assert.Equal(("Patch", null, "Patch", null), (section.Label, section.Device, section.Base, section.At));
        Assert.Equal(new CellSize(30, 20), section.Size);
        Assert.Equal(["Compressor", "Wah", null], section.Children.Select(child => child.Base));
        Assert.Equal("Sustain", ((ParameterControl)section.Children[0].Children[0]).Slots[SlotNames.Value].ToString());
        Assert.Equal("Compressor.Attack", ((ParameterControl)section.Children[2]).Slots[SlotNames.Value].ToString());
        Assert.Empty(UserControlDefinition.Check(section));
    }

    [Fact]
    public void Plan_DoesNotChangeTheDashboard()
    {
        var dashboard = Read();
        var before = DashboardFile.Write(dashboard, FileFormat.Xml);

        UserControlSave.Plan(dashboard, Patch(dashboard), ConsoleLibrary.BuiltInLibrary);

        Assert.Equal(before, DashboardFile.Write(dashboard, FileFormat.Xml));
    }

    [Fact]
    public void Plan_OfASectionOnASectionOfThePatch_HasItsNodeDef()
    {
        var dashboard = Read();

        var plan = UserControlSave.Plan(dashboard, (LayoutControl)Patch(dashboard).Children[1], ConsoleLibrary.BuiltInLibrary);

        Assert.Equal(("Wah", "Gx700.wah"), (plan.Control!.Name, plan.Control.NodeDef.FullName));
        Assert.Equal("Wah", plan.Control.Section.Base);
    }

    [Fact]
    public void Plan_OfASectionWithItsOwnDeviceAndFullBase_KeepsTheLastNode()
    {
        var dashboard = Read();

        var plan = UserControlSave.Plan(dashboard, (LayoutControl)Patch(dashboard).Children[0], ConsoleLibrary.BuiltInLibrary);

        Assert.Empty(plan.Problems);
        Assert.Equal(("Gx700.compressor", null, "Compressor"), (plan.Control!.NodeDef.FullName, plan.Control.Section.Device, plan.Control.Section.Base));
    }

    [Fact]
    public void Plan_OfASectionWithoutABaseOfItsOwn_HasNoBase()
    {
        var dashboard = Read(Dashboard.Replace("""<section label="Patch" at="1,1" size="30,20" base="Patch">""", """<section label="Patch" at="1,1" size="30,20">""")
            .Replace("""grid device="fx" base="AddressMap.Memory[$patch]" """, """grid device="fx" base="AddressMap.Memory[$patch].Patch" """));

        var plan = UserControlSave.Plan(dashboard, Patch(dashboard), ConsoleLibrary.BuiltInLibrary);

        Assert.Empty(plan.Problems);
        Assert.Equal(("Gx700.patch", null), (plan.Control!.NodeDef.FullName, plan.Control.Section.Base));
    }

    [Theory]
    [InlineData("""<section base="Chain[$chain]"><variable name="chain" value="0" /></section>""", null)]
    [InlineData("""<section base="Chain[$chain]" />""", "'$chain', which is not declared in the user control")]
    [InlineData("""<section base="Common.Chain[0]" />""", "is one node, not 'Common.Chain[0]'")]
    public void Check_TheBaseOfTheSection_IsOneNode_WithDeclaredVariables(string xml, string? message)
    {
        var section = (LayoutControl)DashboardFile.Read($"""<dashboard format="jacobi.midi.dashboard/1" name="T"><grid>{xml}</grid></dashboard>""", FileFormat.Xml).Layout.Children[0];

        var problems = UserControlDefinition.Check(section);

        if (message is null)
            Assert.Empty(problems);
        else
            Assert.Contains(problems, problem => problem.Contains(message));
    }

    [Theory]
    [InlineData("""<knob label="X"><bind slot="value" parameter="other:AddressMap.Temporary.Compressor.Sustain" /></knob>""", "not a parameter of the device of the section")]
    [InlineData("""<knob label="X"><bind slot="value" parameter="fx:AddressMap.Temporary.Compressor.Sustain" /></knob>""", "not below the base around it")]
    [InlineData("""<section label="X" device="fx" base="AddressMap.Temporary.Wah" />""", "not below the base around it")]
    [InlineData("""<section label="X" device="other" />""", "has the device 'other'")]
    [InlineData("""<knob label="X"><bind slot="value" parameter="Common.Assign[$patch].Target" /></knob>""", "'$patch', which is not declared in the user control")]
    [InlineData("""<numberBox label="X"><bind slot="value" parameter="$patch" /></numberBox>""", "'$patch', which is not declared")]
    public void Plan_ReportsWhatDoesNotFit_AndGivesNoUserControl(string content, string message)
    {
        var dashboard = Read(Dashboard.Replace("""<knob label="Attack" """, content + """<knob label="Attack" """));

        var plan = UserControlSave.Plan(dashboard, Patch(dashboard), ConsoleLibrary.BuiltInLibrary);

        Assert.Null(plan.Control);
        Assert.Contains(plan.Problems, problem => problem.Contains(message));
    }

    [Theory]
    [InlineData("""label="Patch" """, "", "no label")]
    [InlineData("""grid device="fx" base="AddressMap.Memory[$patch]" """, "grid ", "has no device and base")]
    [InlineData("""base="Patch">""", """base="Patch.Common.Chain[0]">""", "is not a node with a nodeDef")]
    public void Plan_OfASectionWithoutNameOrContract_IsReported(string attribute, string replacement, string message)
    {
        var dashboard = Read(Dashboard.Replace(attribute, replacement));

        var plan = UserControlSave.Plan(dashboard, Patch(dashboard), ConsoleLibrary.BuiltInLibrary);

        Assert.Null(plan.Control);
        Assert.Contains(plan.Problems, problem => problem.Contains(message));
    }
}
