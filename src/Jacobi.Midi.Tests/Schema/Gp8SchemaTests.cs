using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

public class Gp8SchemaTests
{
    [Fact]
    public void Gp8_ValidateSchema()
    {
        var schemaSet = TestHelpers.CreateSchemas();
        var processors = TestHelpers.CreateProcessors();

        var serializer = new SerializerBuilder()
            .AddSchemas(schemaSet)
            .AddProcessors(processors)
            .Build();

        var plan = serializer.GetPlan(new SchemaName("Gp8.AddressMap"));
        Assert.NotNull(plan);
    }
}
