using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Rigs;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The inheritance of device and base by the bindings of a dashboard: the nearest device and base of the control and
/// the layout controls around it, a binding with its own device has a full path, and unresolvable bindings are reported.
/// </summary>
public class BindingResolverTests
{
    private static readonly DashboardDevice Synth = new("synth", "Acme.AddressMap");
    private static readonly DashboardDevice Pedalboard = new("pedalboard", "Gx.AddressMap");

    [Fact]
    public void Binding_IsRelativeToTheBaseOfTheSection()
    {
        var knob = Knob(new Binding("Cutoff"));
        var dashboard = Dashboard(Section("synth", "AddressMap.Edit.Filter", knob));

        var resolution = BindingResolver.Resolve(dashboard);

        Assert.Empty(resolution.Problems);
        var binding = Assert.Single(resolution.Bindings);
        Assert.Same(knob, binding.Control);
        Assert.Equal(SlotNames.Value, binding.Slot);
        Assert.Equal(new ParameterReference("synth", "AddressMap.Edit.Filter.Cutoff", InstancePath.Empty), binding.Parameter);
    }

    [Fact]
    public void NearestBase_Wins_BasesDoNotStack()
    {
        var knob = new ValueControl(ParameterControlKind.Knob)
        {
            Base = "AddressMap.Edit.Amp",
            Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new("Level") },
        };
        var dashboard = Dashboard(Section("synth", "AddressMap.Edit.Filter", knob));

        var binding = Assert.Single(BindingResolver.Resolve(dashboard).Bindings);

        Assert.Equal("AddressMap.Edit.Amp.Level", binding.Parameter!.Path);
    }

    [Fact]
    public void BindingWithItsOwnDevice_HasAFullPath()
    {
        var knob = Knob(new Binding("AddressMap.Temporary.Compressor.Attack") { Device = "pedalboard" });
        var dashboard = Dashboard(Section("synth", "AddressMap.Edit.Filter", knob));

        var binding = Assert.Single(BindingResolver.Resolve(dashboard).Bindings);

        Assert.Equal(new ParameterReference("pedalboard", "AddressMap.Temporary.Compressor.Attack", InstancePath.Empty), binding.Parameter);
    }

    [Fact]
    public void RepeatIndices_InThePath_AreTheInstance()
    {
        var knob = Knob(new Binding("AddressMap.Programs[5].Program.Level"));
        var dashboard = Dashboard(Section("synth", null, knob));

        var binding = Assert.Single(BindingResolver.Resolve(dashboard).Bindings);

        Assert.Equal(new ParameterReference("synth", "AddressMap.Programs.Program.Level", new InstancePath([5])), binding.Parameter);
    }

    [Fact]
    public void RepeatIndices_OfBaseAndPath_AreCombined()
    {
        var knob = Knob(new Binding("Zones[2].Level"));
        var dashboard = Dashboard(Section("synth", "AddressMap.Programs[5].Program", knob));

        var binding = Assert.Single(BindingResolver.Resolve(dashboard).Bindings);

        Assert.Equal("AddressMap.Programs.Program.Zones.Level", binding.Parameter!.Path);
        Assert.Equal(new InstancePath([5, 2]), binding.Parameter!.Instance);
    }

    [Fact]
    public void InvalidPath_IsAProblem()
    {
        var knob = Knob(new Binding("Level[x]"));

        var resolution = BindingResolver.Resolve(Dashboard(Section("synth", "AddressMap.Edit", knob)));

        Assert.Empty(resolution.Bindings);
        Assert.Contains("not a valid schema path", Assert.Single(resolution.Problems).Message);
    }

    [Theory]
    [InlineData("Cutoff", null, "Cutoff")]
    [InlineData("synth:AddressMap.Edit.Cutoff", "synth", "AddressMap.Edit.Cutoff")]
    [InlineData("Programs[5].Program.Level", null, "Programs[5].Program.Level")]
    public void Binding_ParsesDeviceAndPath(string text, string? device, string path)
    {
        Assert.True(Binding.TryParse(text, out var binding, out _));
        Assert.Equal(device, binding.Device);
        Assert.Equal(path, binding.Path);
        Assert.Equal(text, binding.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(":Cutoff")]
    [InlineData("Level[x]")]
    [InlineData("^.Sibling")]
    [InlineData("Programs[].Level")]
    public void Binding_RejectsInvalidText(string text)
    {
        Assert.False(Binding.TryParse(text, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void EnvelopeSegments_AreSlotsRelativeToTheEnvelopesBase()
    {
        var envelope = new EnvelopeControl
        {
            Device = "synth",
            Base = "AddressMap.Edit.AmpEnv",
            Segments =
            [
                new(new Binding("Attack")),
                new(new Binding("Decay"), new Binding("Sustain"), Hold: true),
                new(new Binding("Release")),
            ],
        };

        var resolution = BindingResolver.Resolve(Dashboard(envelope));

        Assert.Empty(resolution.Problems);
        Assert.Equal(
            [
                (SlotNames.Time(0), "AddressMap.Edit.AmpEnv.Attack"),
                (SlotNames.Time(1), "AddressMap.Edit.AmpEnv.Decay"),
                (SlotNames.Level(1), "AddressMap.Edit.AmpEnv.Sustain"),
                (SlotNames.Time(2), "AddressMap.Edit.AmpEnv.Release"),
            ],
            resolution.Bindings.Select(binding => (binding.Slot, binding.Parameter!.Path)));
    }

    [Fact]
    public void BindingWithoutDevice_IsAProblem()
    {
        var knob = Knob(new Binding("AddressMap.Edit.Filter.Cutoff"));

        var resolution = BindingResolver.Resolve(Dashboard(knob));

        Assert.Empty(resolution.Bindings);
        var problem = Assert.Single(resolution.Problems);
        Assert.Same(knob, problem.Control);
        Assert.Contains("no device", problem.Message);
    }

    [Fact]
    public void DeviceTheDashboardDoesNotList_IsAProblem()
    {
        var knob = Knob(new Binding("Cutoff"));

        var resolution = BindingResolver.Resolve(Dashboard(Section("drums", "AddressMap.Kit", knob)));

        Assert.Empty(resolution.Bindings);
        Assert.Contains("'drums'", Assert.Single(resolution.Problems).Message);
    }

    [Fact]
    public void IndexVariable_GivesTheInstance()
    {
        var knob = Knob(new Binding("AddressMap.Memory[$patch].Patch.Level"));
        var row = Row([new IndexVariable("patch", 5)], Section("synth", null, knob));

        var binding = Assert.Single(BindingResolver.Resolve(Dashboard(row)).Bindings);

        Assert.Equal(new ParameterReference("synth", "AddressMap.Memory.Patch.Level", new InstancePath([5])), binding.Parameter);
        Assert.Equal([new VariableReference(row, "patch")], binding.Variables);
    }

    [Fact]
    public void CurrentValue_OfAVariable_ReplacesItsStartingValue()
    {
        var knob = Knob(new Binding("Memory[$patch].Patch.Level"));
        var row = Row([new IndexVariable("patch", 5)], Section("synth", "AddressMap", knob));
        var context = new BindingContext { Values = new Dictionary<VariableReference, int> { [new(row, "patch")] = 7 } };

        var binding = Assert.Single(BindingResolver.Resolve(Dashboard(row), context).Bindings);

        Assert.Equal(new InstancePath([7]), binding.Parameter!.Instance);
    }

    [Fact]
    public void NearestDeclaration_OfAVariable_Wins()
    {
        var knob = Knob(new Binding("AddressMap.Memory[$patch].Patch.Level"));
        var inner = new LayoutControl
        {
            Kind = LayoutControlKind.Section,
            Device = "synth",
            Variables = [new IndexVariable("patch", 2)],
            Children = [knob],
        };
        var row = Row([new IndexVariable("patch", 5)], inner);

        var binding = Assert.Single(BindingResolver.Resolve(Dashboard(row)).Bindings);

        Assert.Equal(new InstancePath([2]), binding.Parameter!.Instance);
        Assert.Equal([new VariableReference(inner, "patch")], binding.Variables);
    }

    [Fact]
    public void UndeclaredVariable_IsAProblem()
    {
        var knob = Knob(new Binding("AddressMap.Memory[$patch].Patch.Level"));

        var resolution = BindingResolver.Resolve(Dashboard(Section("synth", null, knob)));

        Assert.Empty(resolution.Bindings);
        Assert.Contains("'$patch' is not declared", Assert.Single(resolution.Problems).Message);
    }

    [Fact]
    public void VariableBinding_IsToTheNearestDeclaration()
    {
        var numberBox = new ValueControl(ParameterControlKind.NumberBox)
        {
            Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = new("$patch") },
        };
        var row = Row([new IndexVariable("patch", 5)], numberBox);

        var binding = Assert.Single(BindingResolver.Resolve(Dashboard(row)).Bindings);

        Assert.Null(binding.Parameter);
        Assert.Equal(new VariableReference(row, "patch"), binding.Variable);
        Assert.Equal([new VariableReference(row, "patch")], binding.Variables);
    }

    [Fact]
    public void VariableBinding_OfAnUndeclaredVariable_IsAProblem()
    {
        var numberBox = Knob(new Binding("$patch"));

        var resolution = BindingResolver.Resolve(Dashboard(numberBox));

        Assert.Empty(resolution.Bindings);
        var problem = Assert.Single(resolution.Problems);
        Assert.Same(numberBox, problem.Control);
        Assert.Contains("'$patch' is not declared", problem.Message);
    }

    [Theory]
    [InlineData("$patch", null, "patch")]
    [InlineData("Programs[$patch].Level", null, null)]
    public void Binding_Variable_IsTheNameAfterTheDollar(string text, string? device, string? variable)
    {
        Assert.True(Binding.TryParse(text, out var binding, out _));
        Assert.Equal(device, binding.Device);
        Assert.Equal(variable, binding.Variable);
    }

    [Theory]
    [InlineData("$1patch")]
    [InlineData("synth:$patch")]
    public void Binding_RejectsAnInvalidVariable(string text)
    {
        Assert.False(Binding.TryParse(text, out _, out _));
    }

    [Fact]
    public void DevicesOfTheDashboard_AreMatchedWithTheRig()
    {
        var rig = new Rig
        {
            Devices = [new DeviceInstance { Alias = "synth", MapRoot = "Other.AddressMap" }],
        };

        var problems = BindingResolver.Resolve(Dashboard(), new BindingContext { Rig = rig }).Problems;

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.Message.Contains("rig is a 'Other.AddressMap', the dashboard needs a 'Acme.AddressMap'"));
        Assert.Contains(problems, problem => problem.Message.Contains("'pedalboard' is not in the rig"));
    }

    [Fact]
    public void ValueControl_IsNotAnEnvelope()
    {
        Assert.Throws<ArgumentException>(() => new ValueControl(ParameterControlKind.Envelope));
    }

    private static ValueControl Knob(Binding binding)
        => new(ParameterControlKind.Knob) { Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = binding } };

    private static LayoutControl Section(string device, string? basePath, params DashboardControl[] children)
        => new() { Kind = LayoutControlKind.Section, Device = device, Base = basePath, Children = children };

    private static LayoutControl Row(IReadOnlyList<IndexVariable> variables, params DashboardControl[] children)
        => new() { Kind = LayoutControlKind.Row, Variables = variables, Children = children };

    private static Dashboard Dashboard(params DashboardControl[] children)
        => Dashboard(new LayoutControl { Kind = LayoutControlKind.Row, Children = children });

    private static Dashboard Dashboard(LayoutControl layout)
        => new()
        {
            Name = "Test",
            Devices = [Synth, Pedalboard],
            Layout = layout,
        };
}
