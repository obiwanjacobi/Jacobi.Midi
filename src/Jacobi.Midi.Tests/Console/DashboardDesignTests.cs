using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// Building a dashboard in Edit mode with the GP-8 schema: the parameter tree of a device, the control suggested for a
/// parameter, dropping parameters and groups (bindings relative to the base around them, index variables for the
/// repeats), binding a slot, changing the kind of a control, moving a control to another grid with its bindings, and
/// the sessions following the edits.
/// </summary>
public class DashboardDesignTests
{
    private const string Rig = """
        <rig format="jacobi.midi.rig/1">
          <device alias="gp8" mapRoot="Gp8.AddressMap">
            <deviceValue name="deviceId" value="0" />
          </device>
        </rig>
        """;

    // a grid on the stored patches with a section in it, and an empty section without base
    private const string Dashboard = """
        <dashboard format="jacobi.midi.dashboard/1" name="Test">
          <device alias="gp8" mapRoot="Gp8.AddressMap" />
          <grid device="gp8" base="AddressMap.Memory[$patch].Patch" size="40,30">
            <variable name="patch" value="3" />
            <section label="Filter" at="0,0" size="12,10">
              <knob label="Sens" at="0,0" size="5,7"><bind slot="value" parameter="DynamicFilter.Sens" /></knob>
            </section>
            <section label="Other" at="13,0" size="12,10" base="AddressMap.Temporary" />
          </grid>
        </dashboard>
        """;

    private const string Empty = """
        <dashboard format="jacobi.midi.dashboard/1" name="Empty">
          <grid size="40,30" />
        </dashboard>
        """;

    private static RigSession OpenRig() => RigSession.Open(RigFile.Read(Rig, FileFormat.Xml), ConsoleLibrary.BuiltInLibrary);

    private static DashboardSession Open(string dashboard = Dashboard)
        => DashboardSession.Open(OpenRig(), DashboardFile.Read(dashboard, FileFormat.Xml));

    private static ParameterNode Node(DashboardSession session, string path)
        => Find(session.Rig.Devices["gp8"].Parameters, path) ?? throw new InvalidOperationException($"'{path}' is not in the tree.");

    private static ParameterNode? Find(ParameterNode node, string path)
        => node.Path == path ? node : node.Children.Select(child => Find(child, path)).FirstOrDefault(found => found is not null);

    private static DropSource Source(DashboardSession session, string path) => new("gp8", "Gp8.AddressMap", Node(session, path));

    private static LayoutControl Filter(DashboardSession session) => (LayoutControl)session.Dashboard.Layout.Children[0];

    private static LayoutControl Other(DashboardSession session) => (LayoutControl)session.Dashboard.Layout.Children[1];

    [Fact]
    public void ParameterTree_HasTheGroupsRepeatsAndFieldsOfTheMap_WithoutFixedValues()
    {
        using var session = Open();
        var tree = session.Rig.Devices["gp8"].Parameters;

        Assert.Equal(("AddressMap", ParameterNodeKind.Group), (tree.Path, tree.Kind));
        var memory = Node(session, "AddressMap.Memory");
        Assert.Equal((ParameterNodeKind.Repeat, 128), (memory.Kind, memory.ItemCount));
        var sens = Node(session, "AddressMap.Memory.Patch.DynamicFilter.Sens");
        Assert.Equal(ParameterNodeKind.Field, sens.Kind);
        Assert.Equal((0m, 100m), (sens.Info!.Range!.Value.Minimum, sens.Info.Range.Value.Maximum));
        Assert.Equal(["AddressMap", "Memory", "Patch", "DynamicFilter", "Sens"], sens.PathFromRoot.Select(node => node.Name));
        // the constant end of the name is not a parameter
        Assert.Null(Find(tree, "AddressMap.Temporary.EndOfString"));
    }

    [Fact]
    public void Suggest_PicksTheControlByTheKindOfParameter()
    {
        using var session = Open();

        Assert.Equal(ParameterControlKind.Knob, ControlCatalog.Suggest(Node(session, "AddressMap.Temporary.DynamicFilter.Sens").Info!));
        Assert.Equal(ParameterControlKind.Selector, ControlCatalog.Suggest(Node(session, "AddressMap.Temporary.DynamicFilter.UpDown").Info!));
        Assert.Equal(ParameterControlKind.Switch, ControlCatalog.Suggest(Node(session, "AddressMap.Temporary.EffectSwiches1.DelayOn").Info!));
        Assert.Equal(ParameterControlKind.Text, ControlCatalog.Suggest(Node(session, "AddressMap.Temporary.Name").Info!));
    }

    [Fact]
    public void DropField_BelowTheBase_IsBoundRelativeToIt_WithTheVariableOfTheBase()
    {
        using var session = Open();

        var plan = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory.Patch.DynamicFilter.Q"), Filter(session));

        var knob = Assert.IsType<ValueControl>(plan.Control);
        Assert.Equal(ParameterControlKind.Knob, knob.Kind);
        Assert.Equal(GridLayout.DefaultSize(ParameterControlKind.Knob), knob.Size);
        Assert.Equal("DynamicFilter.Q", knob.Bindings[SlotNames.Value].ToString());
        Assert.Empty(plan.Variables);
        Assert.Null(plan.Device);
    }

    [Fact]
    public void DropField_UnderAnotherBase_IsBoundToItsBase()
    {
        using var session = Open();

        var plan = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Temporary.Phaser.Rate"), Other(session));

        Assert.Equal("Phaser.Rate", ((ValueControl)plan.Control!).Bindings[SlotNames.Value].ToString());
    }

    [Fact]
    public void DropField_OutsideTheBase_HasAFullPathWithItsDevice()
    {
        using var session = Open();

        var plan = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Temporary.Phaser.Rate"), Filter(session));

        Assert.Equal("gp8:AddressMap.Temporary.Phaser.Rate", ((ValueControl)plan.Control!).Bindings[SlotNames.Value].ToString());
    }

    [Fact]
    public void DropField_InARepeatWithoutVariable_DeclaresOneOnTheGrid_AndAddsTheDevice()
    {
        using var session = Open(Empty);
        var root = session.Dashboard.Layout;

        var plan = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory.Patch.MasterVolume"), root);

        Assert.Equal("gp8:AddressMap.Memory[$memory].Patch.MasterVolume", ((ValueControl)plan.Control!).Bindings[SlotNames.Value].ToString());
        Assert.Equal([new DeclaredVariable(root, new IndexVariable("memory"))], plan.Variables);
        Assert.Equal(new DashboardDevice("gp8", "Gp8.AddressMap"), plan.Device);
    }

    [Fact]
    public void DropField_InARepeat_UsesAVariableInScopeThatIndexesIt()
    {
        // the variable is used in a binding, not in a base
        const string dashboard = """
            <dashboard format="jacobi.midi.dashboard/1" name="Test">
              <device alias="gp8" mapRoot="Gp8.AddressMap" />
              <grid device="gp8" size="40,30">
                <variable name="p" value="0" />
                <knob at="0,0" size="5,7"><bind slot="value" parameter="AddressMap.Memory[$p].Patch.MasterVolume" /></knob>
              </grid>
            </dashboard>
            """;
        using var session = Open(dashboard);

        var plan = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory.Patch.Phaser.Rate"), session.Dashboard.Layout);

        Assert.Equal("AddressMap.Memory[$p].Patch.Phaser.Rate", ((ValueControl)plan.Control!).Bindings[SlotNames.Value].ToString());
        Assert.Empty(plan.Variables);
    }

    [Fact]
    public void DropGroup_IsASectionWithBase_AControlPerField_AndLaidOut()
    {
        using var session = Open();

        var plan = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory.Patch.Phaser"), session.Dashboard.Layout);

        var section = Assert.IsType<LayoutControl>(plan.Control);
        Assert.Equal((LayoutControlKind.Section, "Phaser", "AddressMap.Memory[$patch].Patch.Phaser", null), (section.Kind, section.Label, section.Base, section.Device));
        Assert.Empty(section.Variables);
        Assert.All(section.Children, child => Assert.NotNull(child.At));
        Assert.NotNull(section.Size);
        var bindings = section.Children.OfType<ParameterControl>().Select(control => control.Slots[SlotNames.Value].ToString());
        Assert.Equal(Node(session, "AddressMap.Memory.Patch.Phaser").Children.Select(child => child.Name), bindings);
    }

    [Fact]
    public void DropRepeat_DeclaresItsVariableOnTheSection_WithANumberBox_AndNestsTheGroups()
    {
        using var session = Open(Empty);

        var plan = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory"), session.Dashboard.Layout);

        var section = Assert.IsType<LayoutControl>(plan.Control);
        Assert.Equal(("gp8", "AddressMap.Memory[$memory]"), (section.Device, section.Base));
        Assert.Equal([new IndexVariable("memory")], section.Variables);
        Assert.Empty(plan.Variables);
        var numberBox = Assert.IsType<ValueControl>(section.Children[0]);
        Assert.Equal((ParameterControlKind.NumberBox, "Memory", "$memory"), (numberBox.Kind, numberBox.Label, numberBox.Bindings[SlotNames.Value].ToString()));
        var patch = Assert.IsType<LayoutControl>(section.Children[1]);
        Assert.Equal("AddressMap.Memory[$memory].Patch", patch.Base);
        var filter = patch.Children.OfType<LayoutControl>().First(child => child.Label == "DynamicFilter");
        Assert.Equal("AddressMap.Memory[$memory].Patch.DynamicFilter", filter.Base);
    }

    [Fact]
    public void Drops_ResolveWithoutProblems_OnceApplied()
    {
        using var session = Open(Empty);
        var root = session.Dashboard.Layout;
        var field = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory.Patch.MasterVolume"), root);
        session.Dashboard.Devices = [field.Device!];
        root.Variables = [.. field.Variables.Select(variable => variable.Variable)];
        root.Children = [field.Control!];
        session.Refresh();
        var group = ControlDrops.Drop(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory"), root);
        root.Children = [.. root.Children, group.Control!];

        session.Refresh();

        Assert.Empty(session.Problems);
        Assert.NotNull(session.ValueOf((ParameterControl)root.Children[0], SlotNames.Value));
    }

    [Fact]
    public void BindSlot_BindsTheValueOfTheControlInItsScope()
    {
        using var session = Open();
        var sens = (ParameterControl)Filter(session).Children[0];

        var plan = ControlDrops.Bind(session.Dashboard, session.Resolution, Source(session, "AddressMap.Memory.Patch.DynamicFilter.Q"), sens);

        Assert.Equal((SlotNames.Value, "DynamicFilter.Q"), (plan.Slot, plan.Binding!.ToString()));
        Assert.Null(plan.Control);
    }

    [Fact]
    public void WithKind_KeepsWhatTheControlHas_AndTheSlotsOfTheKind()
    {
        var slider = new ValueControl(ParameterControlKind.Slider)
        {
            Label = "Range", Size = new CellSize(4, 9), At = new CellPosition(1, 2), Device = "gp8",
            Display = new DisplaySettings { Unit = "ms" },
            Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new("A"), [SlotNames.Min] = new("B") },
        };

        var knob = Assert.IsType<ValueControl>(ControlCatalog.WithKind(slider, ParameterControlKind.Knob));

        Assert.Equal((ParameterControlKind.Knob, "Range", new CellSize(4, 9), new CellPosition(1, 2), "gp8", "ms"),
            (knob.Kind, knob.Label, knob.Size, knob.At, knob.Device, knob.Display.Unit));
        Assert.Equal([SlotNames.Value], knob.Bindings.Keys);
        Assert.Throws<ArgumentException>(() => ControlCatalog.WithKind(slider, LayoutControlKind.Row));
        Assert.Equal([LayoutControlKind.Grid, LayoutControlKind.Section], ControlCatalog.KindsOf(new LayoutControl { Kind = LayoutControlKind.Section }));
    }

    [Fact]
    public void PageFor_Tabs_IsTheGridOrSection_OrAGridPageAroundTheControl()
    {
        var tabs = new LayoutControl { Kind = LayoutControlKind.Tabs, Children = [new LayoutControl { Kind = LayoutControlKind.Grid }] };
        var section = new LayoutControl { Kind = LayoutControlKind.Section };
        var knob = ControlCatalog.Create(ParameterControlKind.Knob);

        Assert.Same(section, ControlCatalog.PageFor(tabs, section));
        var page = ControlCatalog.PageFor(tabs, knob);
        Assert.Equal((LayoutControlKind.Grid, "Page 2", knob.Size), (page.Kind, page.Label, page.Size));
        Assert.Same(knob, Assert.Single(page.Children));
    }

    [Fact]
    public void Switch_AcceptsOnlyABool()
    {
        using var session = Open();
        var delayOn = Node(session, "AddressMap.Temporary.EffectSwiches1.DelayOn").Info!;
        var sens = Node(session, "AddressMap.Temporary.DynamicFilter.Sens").Info!;

        Assert.True(ControlCatalog.Accepts(ParameterControlKind.Switch, delayOn));
        Assert.False(ControlCatalog.Accepts(ParameterControlKind.Switch, sens));
        Assert.True(ControlCatalog.Accepts(ParameterControlKind.Knob, sens));
        Assert.NotNull(ControlCatalog.RefusalOf(ParameterControlKind.Switch, sens));
    }

    [Fact]
    public void Session_DoesNotBindASwitchToANumber_AndReportsIt()
    {
        const string dashboard = """
            <dashboard format="jacobi.midi.dashboard/1" name="Test">
              <device alias="gp8" mapRoot="Gp8.AddressMap" />
              <grid device="gp8" base="AddressMap.Temporary" size="20,10">
                <switch label="Sens" at="0,0" size="4,7"><bind slot="value" parameter="DynamicFilter.Sens" /></switch>
                <switch label="On" at="4,0" size="4,7"><bind slot="value" parameter="EffectSwiches1.DelayOn" /></switch>
              </grid>
            </dashboard>
            """;
        using var session = Open(dashboard);
        var children = session.Dashboard.Layout.Children.Cast<ParameterControl>().ToList();

        Assert.Null(session.ValueOf(children[0], SlotNames.Value));
        Assert.NotNull(session.ValueOf(children[1], SlotNames.Value));
        Assert.Contains("on/off", Assert.Single(session.Problems));
    }

    [Fact]
    public void Bool_OnAKnob_IsANumberFromOffToOn()
    {
        const string dashboard = """
            <dashboard format="jacobi.midi.dashboard/1" name="Test">
              <device alias="gp8" mapRoot="Gp8.AddressMap" />
              <grid device="gp8" base="AddressMap.Temporary" size="20,10">
                <knob label="On" at="0,0" size="5,7"><bind slot="value" parameter="EffectSwiches1.DelayOn" /></knob>
              </grid>
            </dashboard>
            """;
        using var session = Open(dashboard);
        var value = session.ValueOf((ParameterControl)session.Dashboard.Layout.Children[0], SlotNames.Value)!;

        Assert.Equal((0m, 1m, 1m, false), (value.Info.Range!.Value.Minimum, value.Info.Range.Value.Maximum, value.Info.Step, value.Info.Default));
        Assert.Equal(ParameterControlKind.Switch, ControlCatalog.Suggest(value.Info));
        value.Set(1m);
        Assert.Equal((true, 1m), (value.Value, value.Number));
        value.Set(0m);
        Assert.Equal((false, 0m), (value.Value, value.Number));
    }

    [Fact]
    public void Choice_IsTheNumberOfItsOption()
    {
        using var session = Open();
        var upDown = Node(session, "AddressMap.Temporary.DynamicFilter.UpDown").Info!;

        Assert.Equal((0m, 1m, 1m), (upDown.Range!.Value.Minimum, upDown.Range.Value.Maximum, upDown.Step));
        Assert.Equal("Up", upDown.ToValue(1m));
        Assert.Equal(1m, upDown.ToNumber("Up"));
        Assert.Null(upDown.ToNumber("Sideways"));
        Assert.Equal(("Down", null), (upDown.OptionAt(0), upDown.OptionAt(2)));
    }

    [Fact]
    public void Accepts_WhatEachKindCanShow()
    {
        using var session = Open();
        var choice = Node(session, "AddressMap.Temporary.DynamicFilter.UpDown").Info!;
        var number = Node(session, "AddressMap.Temporary.DynamicFilter.Sens").Info!;
        var text = Node(session, "AddressMap.Temporary.Name").Info!;

        Assert.True(ControlCatalog.Accepts(ParameterControlKind.Selector, choice));
        Assert.False(ControlCatalog.Accepts(ParameterControlKind.Selector, number));
        Assert.True(ControlCatalog.Accepts(ParameterControlKind.Knob, choice));
        Assert.True(ControlCatalog.Accepts(ParameterControlKind.XyPad, number));
        Assert.True(ControlCatalog.Accepts(ParameterControlKind.Envelope, number));
        Assert.False(ControlCatalog.Accepts(ParameterControlKind.Knob, text));
        Assert.True(ControlCatalog.Accepts(ParameterControlKind.Text, number));
        Assert.True(ControlCatalog.Accepts(ParameterControlKind.NumberBox, new ParameterInfo("n", typeof(int), null, 1, null)));
    }

    [Fact]
    public void EnvelopeShape_GivesEachSegmentAShare_AndTheHoldOneMore()
    {
        // attack full time to the top, decay half its time to half level and holds, release a quarter to the bottom
        var state = new EnvelopeFaceState([new(1, 1), new(0.5, 0.5), new(0.25, 0)], HoldSegment: 1);

        var points = EnvelopeShape.Points(state);

        Assert.Equal(
            [new(0, 0, null), new(0.25, 1, 0), new(0.375, 0.5, 1), new(0.625, 0.5, null), new(0.6875, 0, 2)],
            points);
        Assert.Equal(0.5, EnvelopeShape.TimeAt(state, 1, 0.375), 6);
        Assert.Equal(1, EnvelopeShape.TimeAt(state, 0, 0.9));
    }

    [Fact]
    public void FirstSlotOf_IsTheFirstShownSlotThatIsNotBound()
    {
        var xy = ControlCatalog.Create(ParameterControlKind.XyPad);
        Assert.Equal(SlotNames.X, ControlDrops.FirstSlotOf(xy));
        xy.Slots = new Dictionary<string, Binding> { [SlotNames.X] = new("A") };
        Assert.Equal(SlotNames.Y, ControlDrops.FirstSlotOf(xy));

        var envelope = (EnvelopeControl)ControlCatalog.Create(ParameterControlKind.Envelope);
        Assert.Equal(ControlCatalog.Adsr, envelope.Segments);
        envelope.Slots = new Dictionary<string, Binding> { [SlotNames.Time(0)] = new("Attack") };
        Assert.Equal(SlotNames.Level(0), ControlDrops.FirstSlotOf(envelope));
    }

    [Fact]
    public void FromFraction_IsOnAWholeNumberOfSteps()
    {
        var range = new ValueRange(-50, 50);

        Assert.Equal(0m, range.FromFraction(0.5, 1));
        Assert.Equal(-50m, range.FromFraction(-1, 1));
        Assert.Equal(26m, range.FromFraction(0.762, 2));
    }

    [Fact]
    public void IsWide_WhenTheCellsAreWiderThanTall()
    {
        Assert.True(GridLayout.IsWide(new CellSize(9, 4)));
        Assert.False(GridLayout.IsWide(GridLayout.DefaultSize(ParameterControlKind.Slider)));
        Assert.False(GridLayout.IsWide(new CellSize(5, 5)));
    }

    [Fact]
    public void EnvelopeSlots_AreTheTimesAndLevelsOfItsSegments()
    {
        var envelope = new EnvelopeControl { Segments = [new(new Binding("Attack")), new(new Binding("Decay"), new Binding("Sustain"), Hold: true)] };

        Assert.Equal([SlotNames.Time(0), SlotNames.Time(1), SlotNames.Level(1)], envelope.Slots.Keys);
        envelope.Slots = new Dictionary<string, Binding> { [SlotNames.Time(0)] = new("A"), [SlotNames.Level(2)] = new("L") };

        Assert.Equal([new(new Binding("A")), new(null, null, Hold: true), new EnvelopeSegment(null, new Binding("L"))], envelope.Segments);
        Assert.Equal(1, SlotNames.SegmentOf("segment2.level"));
        Assert.Null(SlotNames.SegmentOf(SlotNames.Value));
    }

    [Fact]
    public void Move_ToAnotherBase_BindsTheSameParameter()
    {
        using var session = Open();
        var sens = Filter(session).Children[0];

        var plan = ControlMoves.Move(session.Dashboard.Layout, sens, Other(session));

        var change = Assert.Single(plan.Slots);
        Assert.Same(sens, change.Control);
        // the base of Other is the temporary patch: the stored patch needs a full path
        Assert.Equal("gp8:AddressMap.Memory[$patch].Patch.DynamicFilter.Sens", change.Slots[SlotNames.Value].ToString());
        Assert.Empty(plan.Problems);
    }

    [Fact]
    public void Move_OfASection_KeepsTheBaseItInherited()
    {
        using var session = Open();
        var empty = new LayoutControl { Kind = LayoutControlKind.Grid, Size = new CellSize(10, 10), Base = "AddressMap.Temporary" };
        session.Dashboard.Layout.Children = [.. session.Dashboard.Layout.Children, empty];
        var inherits = new LayoutControl
        {
            Kind = LayoutControlKind.Section,
            Children = [new ValueControl(ParameterControlKind.Knob) { Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new("Phaser.Rate") } }],
        };
        Filter(session).Children = [.. Filter(session).Children, inherits];

        var plan = ControlMoves.Move(session.Dashboard.Layout, inherits, empty);

        Assert.Equal("AddressMap.Memory[$patch].Patch", plan.Base);
        Assert.Null(plan.Device);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void Move_OutOfTheScopeOfAVariable_IsReported()
    {
        using var session = Open();
        var outside = new LayoutControl { Kind = LayoutControlKind.Grid, Variables = [new IndexVariable("patch")] };
        session.Dashboard.Layout.Children = [.. session.Dashboard.Layout.Children, outside];

        var plan = ControlMoves.Move(session.Dashboard.Layout, Filter(session).Children[0], outside);

        Assert.Contains("'$patch'", Assert.Single(plan.Problems));
        Assert.Throws<ArgumentException>(() => ControlMoves.Move(session.Dashboard.Layout, Filter(session), Filter(session)));
    }

    [Fact]
    public void Refresh_FollowsTheEdits_AndKeepsTheIndexOfAVariable()
    {
        using var session = Open();
        var sens = (ValueControl)Filter(session).Children[0];
        var variable = new VariableReference(session.Dashboard.Layout, "patch");
        session.SetIndex(variable, 10);
        var before = session.ValueOf(sens, SlotNames.Value);

        sens.Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new("DynamicFilter.Q") };
        session.Refresh();

        var after = Assert.IsType<ParameterValue>(session.ValueOf(sens, SlotNames.Value));
        Assert.NotSame(before, after);
        Assert.Equal("AddressMap.Memory.Patch.DynamicFilter.Q", after.Parameter.Path);
        Assert.Equal(10, session.IndexOf(variable));

        Filter(session).Children = [];
        session.Refresh();
        Assert.Null(session.ValueOf(sens, SlotNames.Value));
    }

    [Fact]
    public void ReopenedRig_KeepsTheStatesOfTheDevicesThatAreTheSame()
    {
        var first = OpenRig();
        first.Rig.Devices = [.. first.Rig.Devices, new DeviceInstance { Alias = "second", MapRoot = "Gp8.AddressMap" }];

        var second = RigSession.Open(first.Rig, first.Library, first);
        Assert.Same(first.Devices["gp8"], second.Devices["gp8"]);

        second.Rig.Devices[0].DeviceValues = new Dictionary<string, long> { ["deviceId"] = 1 };
        var third = RigSession.Open(second.Rig, second.Library, second);
        Assert.NotSame(second.Devices["gp8"], third.Devices["gp8"]);
        Assert.Same(second.Devices["second"], third.Devices["second"]);
    }

    [Fact]
    public void Scope_IsInheritedFromTheLayoutControlsAround()
    {
        using var session = Open();
        var sens = Filter(session).Children[0];

        var scope = BindingScope.Of(session.Dashboard.Layout, sens)!;

        Assert.Equal(("gp8", "AddressMap.Memory[$patch].Patch"), (scope.Device, scope.Base));
        Assert.Equal(new VariableReference(session.Dashboard.Layout, "patch"), scope.Variables["patch"]);
        Assert.Equal(("gp8", "AddressMap.Memory[$patch].Patch.Level"), scope.FullPathOf(new Binding("Level")));
        Assert.Equal("Level", scope.BindingOf("GP8", "AddressMap.Memory[$patch].Patch.Level").ToString());
        Assert.Equal("other:AddressMap.X", scope.BindingOf("other", "AddressMap.X").ToString());
        Assert.Equal(new Dictionary<VariableReference, string> { [scope.Variables["patch"]] = "AddressMap.Memory" }, session.Resolution.Repeats);
    }
}
