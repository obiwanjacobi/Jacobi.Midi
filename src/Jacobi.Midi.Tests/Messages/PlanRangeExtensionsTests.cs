using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Messages;

/// <summary>
/// Tests <see cref="PlanRangeExtensions"/>: the widening of a range to its atomic nodes, and the access and transfer
/// of plan nodes it builds on (<see cref="MidiProperties"/>), on a root without addresses (as a Property Exchange root).
/// </summary>
public class PlanRangeExtensionsTests
{
    private const string Xml = """
        <schema name="Tags">
          <members>
            <group name="Root" midi.access="read">
              <members>
                <field name="Volume" datatype="sys.uint8" />
                <repeat name="Patches" count="4">
                  <members>
                    <group name="Patch" midi.transfer="atomic">
                      <members>
                        <field name="Level" datatype="sys.uint8" />
                        <group name="Filter">
                          <members>
                            <field name="Cutoff" datatype="sys.uint8" />
                            <field name="Resonance" datatype="sys.uint8" midi.access="readwrite" />
                          </members>
                        </group>
                      </members>
                    </group>
                  </members>
                </repeat>
                <repeat name="Chain" count="3" midi.transfer="atomic">
                  <members>
                    <field name="Effect" datatype="sys.uint8" />
                  </members>
                </repeat>
                <field name="Last" datatype="sys.uint8" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private static ExecutionPlan CreatePlan()
        => TestDevice.CreateSerializer(TestDevice.Parse(Xml)).GetPlan(new SchemaName("Tags.Root"));

    private static PlanRange Range(ExecutionPlan plan, string path, params int[] instance)
        => Range(plan, path, instance, path, instance);

    private static PlanRange Range(ExecutionPlan plan, string begin, int[] beginInstance, string end, int[] endInstance)
        => plan.CreateRange(new SchemaPath(begin), new InstancePath(beginInstance), new SchemaPath(end), new InstancePath(endInstance));

    [Fact]
    public void WidenToAtomic_FieldInAnAtomicGroup_BecomesTheGroupOfItsItem()
    {
        var plan = CreatePlan();

        var widened = Range(plan, "Root.Patches.Patch.Filter.Cutoff", 2).WidenToAtomic();

        Assert.Equal(Range(plan, "Root.Patches.Patch", 2), widened);
    }

    [Fact]
    public void WidenToAtomic_ItemOfAnAtomicRepeat_BecomesTheRepeat()
    {
        var plan = CreatePlan();

        var widened = Range(plan, "Root.Chain.Effect", 1).WidenToAtomic();

        Assert.Equal(Range(plan, "Root.Chain"), widened);
    }

    [Fact]
    public void WidenToAtomic_NotAtomic_Unchanged()
    {
        var plan = CreatePlan();
        var volume = Range(plan, "Root.Volume");

        Assert.Same(volume, volume.WidenToAtomic());
    }

    [Fact]
    public void WidenToAtomic_OnlyTheBoundInsideAnAtomicNodeMoves()
    {
        var plan = CreatePlan();

        var widened = Range(plan, "Root.Volume", [], "Root.Patches.Patch.Level", [1]).WidenToAtomic();

        Assert.Equal(Range(plan, "Root.Volume", [], "Root.Patches.Patch", [1]), widened);
    }

    [Fact]
    public void AccessOf_IsInherited_UnlessTheNodeHasItsOwn()
    {
        var plan = CreatePlan();

        Assert.Equal(NodeAccess.Read, MidiProperties.AccessOf(plan.Find(new SchemaPath("Root.Volume"))!));
        Assert.Equal(NodeAccess.Read, MidiProperties.AccessOf(plan.Find(new SchemaPath("Root.Patches.Patch.Filter.Cutoff"))!));
        Assert.Equal(NodeAccess.ReadWrite, MidiProperties.AccessOf(plan.Find(new SchemaPath("Root.Patches.Patch.Filter.Resonance"))!));
    }

    [Fact]
    public void TransferOf_IsTheNodesOwn_NotInherited()
    {
        var plan = CreatePlan();

        Assert.Equal(NodeTransfer.Atomic, MidiProperties.TransferOf(plan.Find(new SchemaPath("Root.Patches.Patch"))!, out var error));
        Assert.Null(error);
        Assert.Null(MidiProperties.TransferOf(plan.Find(new SchemaPath("Root.Patches.Patch.Level"))!, out error));
        Assert.Null(error);
    }
}
