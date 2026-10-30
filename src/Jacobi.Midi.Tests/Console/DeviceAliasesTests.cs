using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Editing;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The device aliases of a dashboard: where it uses them, and renaming one throughout it as an edit that can be undone.
/// </summary>
public class DeviceAliasesTests
{
    // a dashboard that uses 'synth' in all places (declared, on a section, in a binding) and 'pedal' in a binding
    private static (Dashboard Dashboard, LayoutControl Section, ValueControl Knob, ValueControl Other) Sample()
    {
        var knob = new ValueControl(ParameterControlKind.Knob)
        {
            Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new Binding("AddressMap.Cutoff") { Device = "Synth" } },
        };
        var other = new ValueControl(ParameterControlKind.Knob)
        {
            Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new Binding("AddressMap.Level") { Device = "pedal" } },
        };
        var section = new LayoutControl { Kind = LayoutControlKind.Section, Device = "synth", Children = [knob] };
        var dashboard = new Dashboard
        {
            Name = "test",
            Devices = [new DashboardDevice("synth", "Synth.AddressMap")],
            Layout = new LayoutControl { Kind = LayoutControlKind.Grid, Children = [section, other] },
        };
        return (dashboard, section, knob, other);
    }

    [Fact]
    public void Of_ListsEachAliasOnce_DeclaredFirst()
    {
        var (dashboard, _, _, _) = Sample();

        Assert.Equal(["synth", "pedal"], DeviceAliases.Of(dashboard));
    }

    [Fact]
    public void Rename_RenamesEveryOccurrence_AndRevertRestoresThem()
    {
        var (dashboard, section, knob, other) = Sample();

        var edit = new EditGroup("rename", DeviceAliases.Rename(dashboard, "synth", "lead"));
        edit.Apply();

        Assert.Equal("lead", dashboard.Devices[0].Alias);
        Assert.Equal("lead", section.Device);
        Assert.Equal("lead", knob.Bindings[SlotNames.Value].Device);
        Assert.Equal("pedal", other.Bindings[SlotNames.Value].Device);
        Assert.Equal(["lead", "pedal"], DeviceAliases.Of(dashboard));

        edit.Revert();

        Assert.Equal(["synth", "pedal"], DeviceAliases.Of(dashboard));
        Assert.Equal("Synth", knob.Bindings[SlotNames.Value].Device);
    }

    [Fact]
    public void Rename_ToTheSameAlias_HasNoEdits()
    {
        var (dashboard, _, _, _) = Sample();

        Assert.Empty(DeviceAliases.Rename(dashboard, "synth", "synth"));
    }

    [Theory]
    [InlineData("pedal")]
    [InlineData("")]
    [InlineData("a:b")]
    public void Rename_ToAnAliasThatCannotBeTaken_IsAProblem(string newAlias)
    {
        var (dashboard, _, _, _) = Sample();

        Assert.NotNull(DeviceAliases.ProblemOf(dashboard, "synth", newAlias));
        Assert.Throws<ArgumentException>(() => DeviceAliases.Rename(dashboard, "synth", newAlias));
    }
}
