using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Dashboards;
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

        Assert.Equal("AddressMap.Edit.Amp.Level", binding.Parameter.Path);
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
    public void Instance_IsKept()
    {
        var instance = new InstancePath([5]);
        var knob = Knob(new Binding("AddressMap.Programs.Program.Level") { Instance = instance });
        var dashboard = Dashboard(Section("synth", null, knob));

        var binding = Assert.Single(BindingResolver.Resolve(dashboard).Bindings);

        Assert.Equal(instance, binding.Parameter.Instance);
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
            resolution.Bindings.Select(binding => (binding.Slot, binding.Parameter.Path)));
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
    public void ValueControl_IsNotAnEnvelope()
    {
        Assert.Throws<ArgumentException>(() => new ValueControl(ParameterControlKind.Envelope));
    }

    private static ValueControl Knob(Binding binding)
        => new(ParameterControlKind.Knob) { Bindings = new Dictionary<string, Binding> { [SlotNames.Value] = binding } };

    private static LayoutControl Section(string device, string? basePath, params DashboardControl[] children)
        => new() { Kind = LayoutControlKind.Section, Device = device, Base = basePath, Children = children };

    private static Dashboard Dashboard(params DashboardControl[] children)
        => new()
        {
            Name = "Test",
            Devices = [Synth, Pedalboard],
            Layout = new LayoutControl { Kind = LayoutControlKind.Row, Children = children },
        };
}
