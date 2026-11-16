using System.Text.Json.Nodes;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Device.PropertyExchange;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.PropertyExchange;

/// <summary>
/// Tests what <see cref="PropertyDeviceState"/> does for a send (Set): the client's values of a range, whether a send is
/// complete (a resource set whole is atomic) and taking the client's values over the state.
/// </summary>
public class PropertyDeviceStateTests
{
    private const string Xml = """
        <schema name="Pe">
          <members>
            <group name="Patch" midi.role="pe" midi.transfer="atomic">
              <members>
                <field name="name" datatype="sys.string" />
                <field name="level" datatype="sys.int64" />
                <repeat name="rates">
                  <members>
                    <field name="item" datatype="sys.int64" />
                  </members>
                </repeat>
                <group name="meta" midi.access="read">
                  <members>
                    <field name="serial" datatype="sys.string" />
                  </members>
                </group>
              </members>
            </group>
            <group name="Edit" midi.role="pe">
              <members>
                <field name="name" datatype="sys.string" />
                <field name="level" datatype="sys.int64" />
                <field name="ab" datatype="sys.bool" midi.key="a/b" />
              </members>
            </group>
            <group name="List" midi.role="pe">
              <members>
                <repeat name="items">
                  <members>
                    <field name="title" datatype="sys.string" />
                    <repeat name="bankPC" count="3">
                      <members>
                        <field name="item" datatype="sys.int64" />
                      </members>
                    </repeat>
                  </members>
                </repeat>
              </members>
            </group>
          </members>
        </schema>
        """;

    private const string PatchData = """{"name":"Lead","level":5,"rates":[1,2,3,4],"meta":{"serial":"X1"}}""";

    private static ExecutionPlan Plan(string root)
        => TestDevice.CreateSerializer(TestDevice.Parse(Xml)).GetPlan(new SchemaName($"Pe.{root}"));

    private static PlanRange Field(ExecutionPlan plan, string path)
        => plan.CreateRange(new SchemaPath(path), new SchemaPath(path));

    private static LogicalField Long(string name, long value) => new(name, typeof(long), value);

    private static FieldValueStore Source(params (string Path, int[] Instance, LogicalField Value)[] values)
    {
        var source = new FieldValueStore();
        foreach (var (path, instance, value) in values)
        {
            source.Set(path, new InstancePath(instance), value);
        }
        return source;
    }

    [Fact]
    public void ClientValues_AreTheSourcesValuesInTheRange_WithTheItemsOfTheState()
    {
        var plan = Plan("Patch");
        var state = new PropertyDeviceState(plan);
        state.Take(null, JsonNode.Parse(PatchData));
        var source = Source(
            ("Patch.level", [], Long("level", 7)),
            ("Patch.rates.item", [3], Long("item", 40)),
            ("Patch.rates.item", [5], Long("item", 60)));

        var level = state.ClientValues(Field(plan, "Patch.level"), source);
        var rates = state.ClientValues(Field(plan, "Patch.rates.item"), source);

        Assert.Equal([("Patch.level", InstancePath.Empty)], level.Values.Select(value => (value.Path, value.Instance)));
        // the state holds four rates: an item the data does not have is not the client's
        Assert.Equal([new InstancePath([3])], rates.Values.Select(value => value.Instance));
    }

    [Fact]
    public void Completes_AnAtomicResource_NeedsEveryValueThatCanBeWritten()
    {
        var plan = Plan("Patch");
        var state = new PropertyDeviceState(plan);
        var level = Field(plan, "Patch.level");
        var client = state.ClientValues(level, Source(("Patch.level", [], Long("level", 7))));

        // without the resource, its fields and items are not known
        Assert.False(state.Completes(level, client, fresh: false));

        state.Take(null, JsonNode.Parse(PatchData));
        Assert.True(state.Completes(level, client, fresh: false));
        Assert.False(state.Completes(level, client, fresh: true));

        // fresh: all from the client, but not the read-only serial
        var all = state.ClientValues(state.Whole, Source(
            ("Patch.name", [], new LogicalField("name", typeof(string), "Pad")),
            ("Patch.level", [], Long("level", 7)),
            ("Patch.rates.item", [0], Long("item", 1)), ("Patch.rates.item", [1], Long("item", 2)),
            ("Patch.rates.item", [2], Long("item", 3)), ("Patch.rates.item", [3], Long("item", 4))));
        Assert.True(state.Completes(level, all, fresh: true));
    }

    [Fact]
    public void Completes_ANonAtomicResource_NeedsOnlyTheRange()
    {
        var plan = Plan("Edit");
        var state = new PropertyDeviceState(plan);
        state.Take(null, JsonNode.Parse("""{"name":"Lead","level":5}"""));
        var level = Field(plan, "Edit.level");

        var client = state.ClientValues(level, Source(("Edit.level", [], Long("level", 7))));

        Assert.True(state.Completes(level, client, fresh: true));
    }

    [Fact]
    public void Merge_TakesTheClientsValues_ButNotTheReadOnlyOnes()
    {
        var plan = Plan("Patch");
        var state = new PropertyDeviceState(plan);
        state.Take(null, JsonNode.Parse(PatchData));
        var client = state.ClientValues(state.Whole, Source(
            ("Patch.level", [], Long("level", 7)),
            ("Patch.meta.serial", [], new LogicalField("serial", typeof(string), "changed"))));

        state.Merge(state.Whole, client);

        var values = new FieldValueStore();
        state.CopyTo(state.Whole, values);
        Assert.True(values.TryGet("Patch.level", InstancePath.Empty, out var levelValue));
        Assert.Equal(7L, levelValue.Value);
        Assert.True(values.TryGet("Patch.meta.serial", InstancePath.Empty, out var serial));
        Assert.Equal("X1", serial.Value);
    }

    [Theory]
    [InlineData("Patch", PatchData)]
    [InlineData("Edit", """{"name":"Lead","level":5,"a/b":true}""")]
    [InlineData("List", """[{"title":"Piano","bankPC":[0,0,1]},{"title":"Organ","bankPC":[0,0,17]}]""")]
    public void Body_IsTheDataTheStateTook(string root, string data)
    {
        var state = new PropertyDeviceState(Plan(root));
        var json = JsonNode.Parse(data);
        state.Take(null, json);

        Assert.True(JsonNode.DeepEquals(json, state.Body(null)), state.Body(null)?.ToJsonString());
    }

    [Fact]
    public void PartialBody_HasTheValuesOfTheRangeByJsonPointer_ButNotTheReadOnlyOnes()
    {
        var plan = Plan("Patch");
        var state = new PropertyDeviceState(plan);
        state.Take(null, JsonNode.Parse(PatchData));

        var rate = state.PartialBody(null, plan.CreateRange(new SchemaPath("Patch.rates.item"), new InstancePath([3]), new SchemaPath("Patch.rates.item"), new InstancePath([3])));
        var whole = state.PartialBody(null, state.Whole);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"/rates/3":4}"""), rate), rate.ToJsonString());
        Assert.Equal(["/name", "/level", "/rates/0", "/rates/1", "/rates/2", "/rates/3"], whole.Select(property => property.Key));
    }

    [Fact]
    public void PartialBody_EscapesAKeyInItsPointer_AndAnArrayResourceIsTheData()
    {
        var edit = new PropertyDeviceState(Plan("Edit"));
        edit.Take(null, JsonNode.Parse("""{"name":"Lead","level":5,"a/b":true}"""));
        var list = Plan("List");
        var items = new PropertyDeviceState(list);
        items.Take(null, JsonNode.Parse("""[{"title":"Piano","bankPC":[0,0,1]}]"""));

        Assert.Equal(["/name", "/level", "/a~1b"], edit.PartialBody(null, edit.Whole).Select(property => property.Key));
        Assert.Equal(["/0/title", "/0/bankPC/0", "/0/bankPC/1", "/0/bankPC/2"], items.PartialBody(null, items.Whole).Select(property => property.Key));
    }
}
