using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

public class RolandSchemaTests
{
    [Fact]
    public void Roland_MessagePlan()
    {
        var schemaSet = TestHelpers.CreateSchemas();
        var processors = TestHelpers.CreateProcessors();

        var serializer = new SerializerBuilder()
            .AddSchemas(schemaSet)
            .AddProcessors(processors)
            .Build();

        var plan = serializer.GetPlan(new SchemaName("Roland.Message"));
        Assert.NotNull(plan);
    }
}
