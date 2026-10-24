using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Schema;

public class RolandSchemaTests
{
    private readonly ITestOutputHelper _output;

    public RolandSchemaTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Roland_MessagePlan()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(CreateSchemas())
            .AddProcessors(CreateProcessors())
            .Build();

        var plan = serializer.GetPlan(new SchemaName("Roland.Message16"));
        Assert.NotNull(plan);
    }

    [Fact]
    public void Roland_Message_Deserialize()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(CreateSchemas())
            .AddProcessors(CreateProcessors())
            .AddServices(CreateServices(_output))
            .Build();

        var valueModel = new AddressMapReadModel(AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap")));

        var fileBytes = File.ReadAllBytes(PathOf("Schema/gp8-sysex.syx"));
        var reader = new ReadOnlySequence<byte>(fileBytes);

        // the open-choice of the message defines a memberNodeDef
        // 1. any possible group specifies that NodeDef, so they can be used as content for the open-choice.
        // 2. the pipeline at choice-level, is the basis for the message body group.
        // The value model implements the IOpenChoiceXxxx interfaces and has already received the message header
        //  to determine the correct group to return from the OpenChoice calls.
        //      Use the SchemaSet to find the correct group by custom properties (address, size etc)

        var plan = serializer.GetPlan(new SchemaName("Roland.Message16"));
        serializer.Deserialize(plan.CreateRange("Message16.DT1"), reader, valueModel);

        foreach (var key in valueModel.Values.Keys)
        {
            _output.WriteLine($"{key} = {valueModel.Values[key]}");
        }
    }
}

internal class AddressMapReadModel(AddressMap map) : IValueSink, IOpenChoiceSink
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.CurrentCultureIgnoreCase);
    public IReadOnlyDictionary<string, object?> Values => _values;

    public void SetField(FieldContext context, LogicalField value)
    {
        //var key = $"{context.Path}-{context.Instance}";
        var key = context.Path;
        _values[key] = value.Value;
    }

    public IValueSink EnterGroup(GroupContext context)
    {
        return this;
    }

    public void ExitGroup(GroupContext context)
    {
    }

    public IValueSink EnterItem(RepeatContext context, int index, int count)
    {
        return this;
    }

    public void ExitItem(RepeatContext context, int index, int count)
    {
    }

    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex)
    {
        return this;
    }

    public void ExitChoice(ChoiceContext context, int selectedIndex)
    {
    }

    public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
    {
        // the first DT1 of the dump holds memory patch 1 (address 40 00)
        // the map ends its repeats after that patch
        return new(map.WrapBody(this, "AddressMap.Memory.Patch", new InstancePath([0])), map.Root, "AddressMap.Memory.Patch", new InstancePath([0]));
    }
}