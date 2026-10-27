using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The schema library of the console tests: the core assemblies and the Roland assembly, as the console host preloads
/// them; built once.
/// </summary>
internal static class ConsoleLibrary
{
    private static readonly Lazy<SchemaLibrary> Library = new(() => BuiltIn().Build());

    public static SchemaLibrary BuiltInLibrary => Library.Value;

    /// <summary>A builder with the built-in assemblies, to add more sources to.</summary>
    public static SchemaLibraryBuilder BuiltIn()
    {
        var builder = new SchemaLibraryBuilder();
        foreach (var assembly in SchemaLibraryBuilder.CoreAssemblies)
            builder.AddAssembly(assembly);
        return builder.AddAssembly(typeof(Jacobi.Midi.Roland.Processors.ProcessorFactory).Assembly);
    }
}
