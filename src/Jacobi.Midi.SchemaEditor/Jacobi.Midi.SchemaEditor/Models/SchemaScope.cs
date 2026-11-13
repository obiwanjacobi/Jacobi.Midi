namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// The schemas a document can refer to by name ('schema.name'): the document itself and the schemas it includes (transitively),
/// as loaded (see <see cref="SchemaSetWorkspace.GetScope"/>). Also holds the loaded schemas it does not include,
/// so a reference to one of those can be reported as not included instead of unknown.
/// </summary>
/// <param name="Document">The document; names without namespace are resolved in it.</param>
/// <param name="Includes">The loaded schemas the document includes, directly or through another include; without the document.</param>
/// <param name="NotIncluded">The other loaded schemas, one per name.</param>
internal sealed record SchemaScope(EditDocument Document, IReadOnlyList<EditDocument> Includes, IReadOnlyList<EditDocument> NotIncluded)
{
    /// <summary>
    /// The scope of <paramref name="document"/> in the loaded <paramref name="schemas"/>;
    /// without loaded schemas the scope is only the document itself.
    /// </summary>
    public static SchemaScope Of(EditDocument document, SchemaSetWorkspace? schemas = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return schemas?.GetScope(document) ?? new(document, [], []);
    }

    /// <summary>The document followed by its <see cref="Includes"/>.</summary>
    public IReadOnlyList<EditDocument> Documents => [Document, .. Includes];

    /// <summary>The schemas the document includes itself (not through another include), in the order of its includes.</summary>
    public IReadOnlyList<EditDocument> DirectIncludes
        => [.. Document.Includes
            .Select(include => Includes.FirstOrDefault(schema => SameName(schema.Name, include.Schema)))
            .OfType<EditDocument>()
            .Distinct()];

    /// <summary>The loaded schema named <paramref name="name"/> that the document does not include; null when there is none.</summary>
    public EditDocument? FindNotIncluded(string name)
        => Documents.Any(schema => SameName(schema.Name, name)) ? null
            : NotIncluded.FirstOrDefault(schema => SameName(schema.Name, name));

    private static bool SameName(string left, string right) => String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
