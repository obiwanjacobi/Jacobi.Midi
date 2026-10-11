using System.Runtime.CompilerServices;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

public class Gp8SchemaTests
{
    private static string RolandAssembly([CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..", "..", "Jacobi.Midi.Roland", "bin", "Debug", "net11.0", "Jacobi.Midi.Roland.dll"));

    private static string MidiDeviceAssembly([CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..", "..", "Jacobi.Midi.Device", "bin", "Debug", "net11.0", "Jacobi.Midi.Device.dll"));


    [Fact]
    public void Gp8_LoadsIntoSchemaSet_AndCompiles()
    {
        var schemaSet = new SchemaSet();
        var documents = schemaSet.LoadFromAssembly(RolandAssembly(), "Jacobi.Midi.Roland.Schemas");
        var document = documents.FirstOrDefault(d => d.Name == "Gp8");

        Assert.NotNull(document);
        Assert.Equal(2, schemaSet.Documents.Count);

        schemaSet.Compile();
    }

    [Fact]
    public void Gp8_GetPlan()
    {
        var schemaSet = new SchemaSet();
        var midiDocs = schemaSet.LoadFromAssembly(MidiDeviceAssembly(), "Jacobi.Midi.Device");
        Assert.True(midiDocs.Count() > 0);
        var rolandDocs = schemaSet.LoadFromAssembly(RolandAssembly(), "Jacobi.Midi.Roland.Schemas");
        Assert.True(rolandDocs.Count() > 0);
        schemaSet.Compile();

        var processors = new ProcessorManager();
        processors.Register(new Jacobi.BinarySerializer.Processors.ProcessorFactory());
        processors.Register(new Jacobi.Midi.Roland.Processors.ProcessorFactory());
        processors.Register(new Jacobi.Midi.Processors.ProcessorFactory());

        var serializer = new SerializerBuilder()
            .AddSchemas(schemaSet)
            .AddProcessors(processors)
            .Build();
        var plan = serializer.GetPlan(new SchemaName("Gp8.Patch"));
        Assert.NotNull(plan);
    }
}
