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
/// The kits of the console, and the resource key under which the kit of the current look is found.
/// </summary>
public static class Kits
{
    /// <summary>The resource key of the kit of the current look.</summary>
    public const string ResourceKey = "ConsoleKit";

    /// <summary>Industrial: engraved tracks, bakelite and brass, pilot lamps and amber readouts.</summary>
    public static Kit Foundry { get; } = new("foundry", new Dictionary<ParameterControlKind, Func<Face>>
    {
        [ParameterControlKind.Knob] = () => new FoundryKnobFace(),
        [ParameterControlKind.Slider] = () => new FoundryFaderFace(),
        [ParameterControlKind.Switch] = () => new FoundrySwitchFace(),
        [ParameterControlKind.NumberBox] = () => new FoundryNumberBoxFace(),
        [ParameterControlKind.Text] = () => new FoundryTextFace(),
        [ParameterControlKind.Selector] = () => new FoundrySelectorFace(),
        [ParameterControlKind.XyPad] = () => new FoundryXyPadFace(),
        [ParameterControlKind.Envelope] = () => new FoundryEnvelopeFace(),
    });

    /// <summary>Machined aluminium: LED rings and meters, machined caps and OLED displays.</summary>
    public static Kit Anodized { get; } = new("anodized", new Dictionary<ParameterControlKind, Func<Face>>
    {
        [ParameterControlKind.Knob] = () => new AnodizedKnobFace(),
        [ParameterControlKind.Slider] = () => new AnodizedFaderFace(),
        [ParameterControlKind.Switch] = () => new AnodizedSwitchFace(),
        [ParameterControlKind.NumberBox] = () => new AnodizedNumberBoxFace(),
        [ParameterControlKind.Text] = () => new AnodizedTextFace(),
        [ParameterControlKind.Selector] = () => new AnodizedSelectorFace(),
        [ParameterControlKind.XyPad] = () => new AnodizedXyPadFace(),
        [ParameterControlKind.Envelope] = () => new AnodizedEnvelopeFace(),
    });
}
