using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Xunit;
using Xunit.Internal;

namespace Jacobi.Midi.Tests.Schema;

internal static class TestHelpers
{
#if DEBUG
    const string Config = "Debug";
#else
    const string Config = "Release";
#endif

    private static string AssemblyPathOf(string assemblyName)
    {
        var thisAssembly = typeof(TestHelpers).Assembly;
        return Path.Combine(
            Path.GetDirectoryName(thisAssembly.Location)!,
            //"bin", Config, "net11.0",
            assemblyName + ".dll"
        );
    }

    public static ProcessorManager CreateProcessors()
    {
        var processors = new ProcessorManager();
        processors.Register(new Jacobi.BinarySerializer.Processors.ProcessorFactory());
        processors.Register(new Jacobi.Midi.Roland.Processors.ProcessorFactory());
        processors.Register(new Jacobi.Midi.Processors.ProcessorFactory());
        return processors;
    }

    public static SchemaSet CreateSchemas(params SchemaDocument[] documents)
    {
        var schemaSet = new SchemaSet();
        var midiDocs = schemaSet.LoadFromAssembly(AssemblyPathOf("Jacobi.Midi.Device"), "Jacobi.Midi.Device");
        Assert.True(midiDocs.Count() > 0);
        var rolandDocs = schemaSet.LoadFromAssembly(AssemblyPathOf("Jacobi.Midi.Roland"), "Jacobi.Midi.Roland.Schemas");
        Assert.True(rolandDocs.Count() > 0);
        documents.ForEach(document => schemaSet.AddDocument(document));
        schemaSet.Compile();
        return schemaSet;
    }
}
