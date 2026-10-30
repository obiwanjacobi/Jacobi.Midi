using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// Open dashboards on an open rig with the GP-8 schema: the parameter info the controls get from the data types, the
/// values of the shared device states as the controls see and change them, the patch variable that selects the
/// patch the controls show, and the problems of a dashboard that does not fit.
/// </summary>
public class DashboardSessionTests
{
    private const string Rig = """
        <rig format="jacobi.midi.rig/1">
          <device alias="gp8" mapRoot="Gp8.AddressMap" color="#D4A838">
            <deviceValue name="deviceId" value="0" />
          </device>
        </rig>
        """;

    private const string Dashboard = """
        <dashboard format="jacobi.midi.dashboard/1" name="Test">
          <device alias="gp8" mapRoot="Gp8.AddressMap" />
          <column device="gp8" base="AddressMap.Memory[$patch].Patch">
            <variable name="patch" value="0" />
            <knob label="Sens"><bind slot="value" parameter="DynamicFilter.Sens" /></knob>
            <slider label="High"><bind slot="value" parameter="Equalizer.HiLevel" /></slider>
            <switch label="Delay"><bind slot="value" parameter="EffectSwiches1.DelayOn" /></switch>
            <text label="Name"><bind slot="value" parameter="Name" /></text>
            <knob label="UpDown"><bind slot="value" parameter="DynamicFilter.UpDown" /></knob>
            <numberBox label="Patch"><bind slot="value" parameter="$patch" /></numberBox>
          </column>
        </dashboard>
        """;

    private const int Sens = 0, High = 1, Delay = 2, Name = 3, UpDown = 4, Patch = 5;

    private static readonly byte[] Dump = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Schema", "gp8-sysex.syx"));

    private static RigSession OpenRig()
        => RigSession.Open(RigFile.Read(Rig, FileFormat.Xml), ConsoleLibrary.BuiltInLibrary);

    private static DashboardSession Open(string dashboard = Dashboard)
        => DashboardSession.Open(OpenRig(), DashboardFile.Read(dashboard, FileFormat.Xml));

    private static IControlValue ValueOf(DashboardSession session, int index)
        => session.ValueOf((ParameterControl)session.Dashboard.Layout.Children[index], SlotNames.Value)!;

    [Fact]
    public void RenamedAlias_InTheRigAndThenInTheDashboard_BindsTheControlsAgain()
    {
        var rig = RigFile.Read(Rig, FileFormat.Xml);
        var before = RigSession.Open(rig, ConsoleLibrary.BuiltInLibrary);
        before.Devices["gp8"].LoadDump(Dump);
        var dashboard = DashboardFile.Read(Dashboard, FileFormat.Xml);

        // the rig calls the device otherwise, and is opened again (as the console does after an edit of the rig)
        rig.Devices[0].Alias = "pedal";
        using var session = DashboardSession.Open(RigSession.Open(rig, before.Library, before), dashboard);
        Assert.Null(session.ValueOf((ParameterControl)dashboard.Layout.Children[Sens], SlotNames.Value));

        new EditGroup("rename", DeviceAliases.Rename(dashboard, "gp8", "pedal")).Apply();
        session.Refresh();

        Assert.Empty(session.Problems);
        // the device kept its values: it is the same device by another alias
        Assert.Equal(" Dig Chorus", ValueOf(session, Name).Value);
    }

    [Fact]
    public void UsedPaths_AreTheBoundParameters_WithoutIndicesAndVariables()
    {
        using var session = Open();

        var used = session.UsedPaths("GP8");

        // five parameters; the number box is bound to the variable
        Assert.Equal(5, used.Count);
        Assert.Contains("AddressMap.Memory.Patch.DynamicFilter.Sens", used);
        Assert.Empty(session.UsedPaths("other"));
    }

    [Fact]
    public void Open_GivesAValuePerBoundSlot()
    {
        using var session = Open();

        Assert.Empty(session.Rig.Problems);
        Assert.Empty(session.Problems);
        Assert.Equal(["gp8"], session.Rig.Devices.Keys);
        Assert.All(Enumerable.Range(0, 6), index => Assert.NotNull(ValueOf(session, index)));
        Assert.IsType<VariableValue>(ValueOf(session, Patch));
        Assert.Equal(new AreaColors("#D4A838"), session.ColorsOf("gp8"));
    }

    [Fact]
    public void ParameterInfo_ComesFromTheDataType()
    {
        using var session = Open();

        var sens = ValueOf(session, Sens).Info;
        Assert.Equal(("Sens", ParameterKind.Number, 0m, 100m, 1m), (sens.Name, sens.Kind, sens.Range!.Value.Minimum, sens.Range.Value.Maximum, sens.Step));
        Assert.Equal(0m, sens.Default);

        var high = ValueOf(session, High).Info;
        Assert.Equal((-50m, 50m), (high.Range!.Value.Minimum, high.Range.Value.Maximum));
        Assert.True(high.Range.Value.IsBipolar);
        Assert.Equal(0m, high.Default);

        Assert.Equal((ParameterKind.Switch, false), (ValueOf(session, Delay).Info.Kind, ValueOf(session, Delay).Info.Default));
        Assert.Equal((ParameterKind.Text, (object?)null), (ValueOf(session, Name).Info.Kind, ValueOf(session, Name).Info.Default));

        var upDown = ValueOf(session, UpDown).Info;
        Assert.Equal(ParameterKind.Choice, upDown.Kind);
        Assert.Equal(["Down", "Up"], upDown.Options);
        Assert.Equal("Down", upDown.Default);
    }

    [Fact]
    public void Values_AreUnknown_UntilADumpIsLoaded()
    {
        using var session = Open();
        Assert.Null(ValueOf(session, Sens).Value);

        session.Rig.Devices["gp8"].LoadDump(Dump);

        Assert.Equal(" Dig Chorus", ValueOf(session, Name).Value);
        Assert.Equal(0m, ValueOf(session, High).Number);
        Assert.Equal(false, ValueOf(session, Delay).Value);
    }

    [Fact]
    public void Set_WritesTheDeviceState_InTheValueType_AndReportsTheChange()
    {
        using var session = Open();
        var sens = ValueOf(session, Sens);
        var changes = 0;
        sens.Changed += (_, _) => changes++;
        var origins = new List<ValueOrigin>();
        session.Rig.Devices["gp8"].ValueChanged += (_, change) => origins.Add(change.Origin);

        sens.Set(42m);

        Assert.Equal(1, changes);
        Assert.Equal([ValueOrigin.User], origins);
        Assert.Equal(sens.Info.ValueType, sens.Value!.GetType());
        Assert.Equal(42m, sens.Number);
    }

    [Fact]
    public void Changed_OnlyReportsTheParameterItself()
    {
        using var session = Open();
        var changes = 0;
        ValueOf(session, Sens).Changed += (_, _) => changes++;

        ValueOf(session, High).Set(10m);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void PatchVariable_IsTheItemNumber_WithTheItemCountAsRange()
    {
        using var session = Open();
        var patch = ValueOf(session, Patch);

        Assert.Equal(1m, patch.Number);
        Assert.Equal((1m, 128m), (patch.Info.Range!.Value.Minimum, patch.Info.Range.Value.Maximum));
        Assert.Equal(1, patch.Info.Default);
    }

    [Fact]
    public void SettingThePatch_PointsTheControlsToThatPatch()
    {
        using var session = Open();
        var state = session.Rig.Devices["gp8"];
        state.LoadDump(Dump);
        var name = (ParameterValue)ValueOf(session, Name);
        var nameChanges = 0;
        name.Changed += (_, _) => nameChanges++;
        var patchChanges = 0;
        ValueOf(session, Patch).Changed += (_, _) => patchChanges++;
        var resolved = 0;
        session.Resolved += (_, _) => resolved++;

        ValueOf(session, Patch).Set(6);

        Assert.Equal((1, 1, 1), (nameChanges, patchChanges, resolved));
        Assert.Equal(6m, ValueOf(session, Patch).Number);
        Assert.Equal(new InstancePath([5]), name.Parameter.Instance);
        Assert.True(state.TryGetValue(name.Parameter, out var expected));
        Assert.Equal(expected.Value, name.Value);
    }

    [Fact]
    public void SettingThePatch_StaysWithinItsItems()
    {
        using var session = Open();

        ValueOf(session, Patch).Set(500);
        Assert.Equal(128m, ValueOf(session, Patch).Number);

        ValueOf(session, Patch).Set(-3);
        Assert.Equal(1m, ValueOf(session, Patch).Number);
    }

    [Fact]
    public void DashboardsOnOneRig_ShareTheDeviceStates()
    {
        var rig = OpenRig();
        using var first = DashboardSession.Open(rig, DashboardFile.Read(Dashboard, FileFormat.Xml));
        using var second = DashboardSession.Open(rig, DashboardFile.Read(Dashboard, FileFormat.Xml));
        var changes = 0;
        ValueOf(second, Sens).Changed += (_, _) => changes++;

        ValueOf(first, Sens).Set(77m);

        Assert.Equal(77m, ValueOf(second, Sens).Number);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ColorsOf_ADevice_AreTheDashboardsOverTheRigs()
    {
        var dashboard = Dashboard.Replace("""<device alias="gp8" mapRoot="Gp8.AddressMap" />""",
            """<device alias="gp8" mapRoot="Gp8.AddressMap" panelColor="#263B30" />""");

        using var session = Open(dashboard);

        Assert.Equal(new AreaColors("#D4A838", Panel: "#263B30"), session.ColorsOf("gp8"));
    }

    [Fact]
    public void SlotDeviceOf_IsTheDeviceOfTheFirstBoundSlot()
    {
        using var session = Open();

        Assert.Equal("gp8", session.SlotDeviceOf((ParameterControl)session.Dashboard.Layout.Children[Sens]));
        // bound to an index variable: no device parameter
        Assert.Null(session.SlotDeviceOf((ParameterControl)session.Dashboard.Layout.Children[Patch]));
    }

    [Fact]
    public void Problems_OfTheDashboard_AreListed_AndTheSlotHasNoValue()
    {
        var dashboard = Dashboard.Replace("DynamicFilter.Sens", "DynamicFilter.Missing");

        using var session = Open(dashboard);

        Assert.Contains(session.Problems, problem => problem.StartsWith("Sens.value:") && problem.Contains("is not a field"));
        Assert.Null(session.ValueOf((ParameterControl)session.Dashboard.Layout.Children[Sens], SlotNames.Value));
    }

    [Fact]
    public void ADeviceThatIsNotInTheRig_IsAProblem()
    {
        var dashboard = Dashboard.Replace("""<device alias="gp8" mapRoot="Gp8.AddressMap" />""",
            """<device alias="gp8" mapRoot="Gp8.AddressMap" /><device alias="other" mapRoot="Gp8.AddressMap" />""");

        using var session = Open(dashboard);

        Assert.Contains(session.Problems, problem => problem.Contains("'other' is not in the rig"));
        Assert.Null(session.DeviceOf("other"));
    }

    [Fact]
    public void AVariableThatIndexesNoRepeat_IsAProblem()
    {
        var dashboard = Dashboard.Replace("AddressMap.Memory[$patch].Patch", "AddressMap.Temporary");

        using var session = Open(dashboard);

        Assert.Contains(session.Problems, problem => problem.StartsWith("Patch.value:") && problem.Contains("its number of items is not known"));
    }
}
