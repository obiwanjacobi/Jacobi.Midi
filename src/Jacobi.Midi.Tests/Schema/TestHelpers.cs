using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    public static string PathOf(string subPath)
    {
        var thisAssembly = typeof(TestHelpers).Assembly;
        return Path.Combine(
            Path.GetDirectoryName(thisAssembly.Location)!,
            subPath);
    }

    public static string AssemblyPathOf(string assemblyName)
        => PathOf(assemblyName + ".dll");

    public static ProcessorManager CreateProcessors()
    {
        var processors = new ProcessorManager();
        processors.Register(new Jacobi.BinarySerializer.Processors.ProcessorFactory());
        processors.Register(new Jacobi.Midi.Roland.Processors.ProcessorFactory());
        processors.Register(new Jacobi.Midi.Device.Processors.ProcessorFactory());
        return processors;
    }

    public static SchemaSet CreateSchemas(params SchemaDocument[] documents)
    {
        var schemaSetBuilder = new SchemaSetBuilder();
        var midiDocs = schemaSetBuilder.LoadFromAssembly(AssemblyPathOf("Jacobi.Midi.Device"), "Jacobi.Midi.Device");
        Assert.True(midiDocs.Count() > 0);
        var rolandDocs = schemaSetBuilder.LoadFromAssembly(AssemblyPathOf("Jacobi.Midi.Roland"), "Jacobi.Midi.Roland.Schemas");
        Assert.True(rolandDocs.Count() > 0);
        documents.ForEach(document => schemaSetBuilder.AddDocument(document));
        return schemaSetBuilder.Compile();
    }

    public static IServiceProvider CreateServices(ITestOutputHelper? output = null, params Type[] serviceTypes)
    {
        var services = new ServiceCollection();
        serviceTypes.ForEach(type => services.AddTransient(type));

        if (output is not null)
        {
            services.AddSingleton(typeof(ILoggerFactory), new XunitLoggerFactory(output));
        }


        return services.BuildServiceProvider();
    }
}
