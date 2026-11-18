using Jacobi.Midi.Console.Core.Dashboards;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Faces that belong together: one face per parameter control kind, in one style. A look selects a kit; the faces
/// are created per control, because a face is a visual with its own state. The kits are defined by the modules of
/// the looks (see <c>Looks</c>).
/// </summary>
public sealed class Kit(string name, IReadOnlyDictionary<ParameterControlKind, Func<Face>> faces)
{
    /// <summary>The resource key of the kit of the current look.</summary>
    public const string ResourceKey = "ConsoleKit";

    public string Name => name;

    /// <summary>A new face for the control kind; null when the kit has none.</summary>
    public Face? Create(ParameterControlKind kind)
        => faces.TryGetValue(kind, out var create) ? create() : null;

    /// <summary>
    /// Another kit (<paramref name="newName"/>): the faces of this one, with the face of <paramref name="kind"/>
    /// replaced; for a look that overrides the face of a single control kind.
    /// </summary>
    public Kit With(string newName, ParameterControlKind kind, Func<Face> create)
        => new(newName, new Dictionary<ParameterControlKind, Func<Face>>(faces) { [kind] = create });
}
