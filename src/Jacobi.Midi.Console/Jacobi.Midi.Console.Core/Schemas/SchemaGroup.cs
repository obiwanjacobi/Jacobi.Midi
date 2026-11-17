using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Console.Core.Schemas;

/// <summary>
/// Schemas that share the first part of their name (before the first <see cref="SchemaName.Separator"/>), e.g.
/// <c>Midi</c>, <c>Midi.GM</c> and <c>Midi.CapabilityInquiry</c> under <c>Midi</c>; to offer many schemas in a menu.
/// </summary>
/// <param name="Name">The first part of the names.</param>
/// <param name="Schemas">The full names of the schemas, in the order they were given.</param>
public sealed record SchemaGroup(string Name, IReadOnlyList<string> Schemas)
{
    /// <summary>
    /// The schemas grouped by the first part of their name (case-insensitive), the groups in the order of their first
    /// schema.
    /// </summary>
    public static IReadOnlyList<SchemaGroup> Of(IEnumerable<string> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        return [.. schemas
            .GroupBy(schema => schema.Split(SchemaName.Separator)[0], StringComparer.OrdinalIgnoreCase)
            .Select(group => new SchemaGroup(group.Key, [.. group]))];
    }
}
