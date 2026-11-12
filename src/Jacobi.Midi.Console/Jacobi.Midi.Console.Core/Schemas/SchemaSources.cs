namespace Jacobi.Midi.Console.Core.Schemas;

/// <summary>
/// Where schemas and processors come from besides the built-in assemblies: device assemblies (with embedded schemas
/// and processor factories) and schema files. A rig and a dashboard each have their own sources, so a dashboard can
/// bring its own customisations; a device of the rig has none (it uses the schemas of the library). Relative paths are
/// relative to the file that lists them.
/// </summary>
public sealed record SchemaSources
{
    public static SchemaSources None { get; } = new();

    /// <summary>Paths of assemblies (.dll) with embedded schemas and processor factories.</summary>
    public IReadOnlyList<string> Assemblies { get; init; } = [];

    /// <summary>Paths of schema files (.xml, .json).</summary>
    public IReadOnlyList<string> Schemas { get; init; } = [];

    public bool IsEmpty => Assemblies.Count == 0 && Schemas.Count == 0;
}
