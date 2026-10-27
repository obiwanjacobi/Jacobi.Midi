using Jacobi.Midi.Console.Core.Dashboards;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Faces that belong together: one face per parameter control kind, in one style. A look selects a kit; the faces
/// are created per control, because a face is a visual with its own state.
/// </summary>
public sealed class Kit(string name, IReadOnlyDictionary<ParameterControlKind, Func<Face>> faces)
{
    public string Name => name;

    /// <summary>A new face for the control kind; null when the kit has none.</summary>
    public Face? Create(ParameterControlKind kind)
        => faces.TryGetValue(kind, out var create) ? create() : null;
}

/// <summary>
/// The kits of the console, and the resource key under which the current kit is found.
/// </summary>
public static class Kits
{
    /// <summary>The resource key of the kit of the current look.</summary>
    public const string ResourceKey = "ConsoleKit";

    /// <summary>The flat kit: arcs, bars and tiles in the device's accent.</summary>
    public static Kit Flat { get; } = new("flat", new Dictionary<ParameterControlKind, Func<Face>>
    {
        [ParameterControlKind.Knob] = () => new ArcKnobFace(),
    });
}
