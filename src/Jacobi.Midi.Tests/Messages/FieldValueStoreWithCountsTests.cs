using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Messages;

/// <summary>
/// Tests <see cref="FieldValueStoreWithCounts"/>: the item counts it takes as a sink and gives as a source, and forgetting
/// the values and counts of a node.
/// </summary>
public class FieldValueStoreWithCountsTests
{
    private const string Xml = """
        <schema name="Counts">
          <members>
            <group name="Root">
              <members>
                <repeat name="Items">
                  <members>
                    <field name="Value" datatype="sys.uint8" />
                  </members>
                </repeat>
                <repeat name="ItemsToo">
                  <members>
                    <field name="Value" datatype="sys.uint8" />
                  </members>
                </repeat>
              </members>
            </group>
          </members>
        </schema>
        """;

    private static (ExecutionPlan Plan, RepeatInfo Items, RepeatInfo ItemsToo) CreatePlan()
    {
        var plan = TestDevice.CreateSerializer(TestDevice.Parse(Xml)).GetPlan(new SchemaName("Counts.Root"));
        return (plan, (RepeatInfo)plan.Find(new SchemaPath("Root.Items"))!, (RepeatInfo)plan.Find(new SchemaPath("Root.ItemsToo"))!);
    }

    private static RepeatContext Context(RepeatInfo repeat)
        => new() { Node = repeat, Services = EmptyServices.Instance, Instance = InstancePath.Empty };

    private static void Fill(FieldValueStoreWithCounts store, RepeatInfo repeat, params byte[] values)
    {
        store.EnterRepeat(Context(repeat), values.Length);
        var field = (FieldInfo)repeat.Members[0];
        for (var index = 0; index < values.Length; index++)
        {
            store.SetField(new FieldContext { Node = field, Services = EmptyServices.Instance, Instance = new InstancePath([index]) },
                new LogicalField(field.Name, typeof(byte), values[index]));
        }
    }

    [Fact]
    public void ARepeatEnteredWithACount_IsGivenWithThatCount()
    {
        var (_, items, _) = CreatePlan();
        var store = new FieldValueStoreWithCounts();

        Fill(store, items, 1, 2, 3);

        Assert.True(store.TryGetCount("Root.Items", InstancePath.Empty, out var count));
        Assert.Equal(3, count);
        Assert.Equal(3, store.EnterOpenRepeat(Context(items)).Count);
    }

    [Fact]
    public void ARepeatWithoutACount_IsWrittenItemByItem()
    {
        var (_, items, _) = CreatePlan();
        var store = new FieldValueStoreWithCounts();

        store.EnterRepeat(Context(items), -1);

        Assert.False(store.TryGetCount("Root.Items", InstancePath.Empty, out _));
        Assert.Equal(-1, store.EnterOpenRepeat(Context(items)).Count);
    }

    [Fact]
    public void Clear_ANode_ForgetsItsValuesAndCounts_NotThoseOfANodeWithALongerName()
    {
        var (_, items, itemsToo) = CreatePlan();
        var store = new FieldValueStoreWithCounts();
        Fill(store, items, 1, 2);
        Fill(store, itemsToo, 3);

        store.Clear("Root.Items");

        Assert.False(store.TryGetCount("Root.Items", InstancePath.Empty, out _));
        Assert.False(store.TryGet("Root.Items.Value", new InstancePath([0]), out _));
        Assert.True(store.TryGetCount("Root.ItemsToo", InstancePath.Empty, out _));
        Assert.True(store.TryGet("Root.ItemsToo.Value", new InstancePath([0]), out _));
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
