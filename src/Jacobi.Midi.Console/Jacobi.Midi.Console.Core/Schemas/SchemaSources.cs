namespace Jacobi.Midi.Console.Core.Schemas;

/// <summary>
/// Where schemas, exchange files and processors come from besides the built-in assemblies: device assemblies (with
/// embedded schemas, exchange files and processor factories), schema files and exchange files. A rig and a dashboard
/// each have their own sources, so a dashboard can bring its own customisations; a device of the rig has none (it uses
/// the schemas of the library). Relative paths are relative to the file that lists them.
/// </summary>
public sealed record SchemaSources
{
    public static SchemaSources None { get; } = new();

    /// <summary>Paths of assemblies (.dll) with embedded schemas, exchange files and processor factories.</summary>
    public IReadOnlyList<string> Assemblies { get; init; } = [];

    /// <summary>Paths of schema files (.xml, .json).</summary>
    public IReadOnlyList<string> Schemas { get; init; } = [];

    /// <summary>Paths of exchange files (.exchange.xml, .exchange.json): the message exchange patterns of a map root.</summary>
    public IReadOnlyList<string> Exchanges { get; init; } = [];

    /// <summary>All paths: the assemblies, schema files and exchange files.</summary>
    public IEnumerable<string> All => Assemblies.Concat(Schemas).Concat(Exchanges);

    public bool IsEmpty => Assemblies.Count == 0 && Schemas.Count == 0 && Exchanges.Count == 0;
}
