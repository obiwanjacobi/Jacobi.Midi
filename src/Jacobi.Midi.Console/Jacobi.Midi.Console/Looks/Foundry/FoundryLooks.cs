using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// The Foundry module: the industrial kit of faces and the looks built on it. Everything the module offers (faces,
/// palette, chrome, swatches) is in its folder; <see cref="ConsoleLooks"/> lists its <see cref="All"/>.
/// </summary>
public static class FoundryLooks
{
    /// <summary>Industrial: engraved tracks, bakelite and brass, pilot lamps and amber readouts.</summary>
    public static Kit Kit { get; } = new("foundry", new Dictionary<ParameterControlKind, Func<Face>>
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

    /// <summary>
    /// Accents of metals, patina and lamps; plates of steel and enamel; panels of cast iron and painted metal.
    /// </summary>
    public static Look Foundry { get; } = new("Foundry", Kit,
        Look.ThemeOf(typeof(FoundryLooks), "Looks/Foundry/Foundry.axaml"),
        RgbColor.Parse("#D4A838"),
        new Dictionary<ColorArea, IReadOnlyList<RgbColor>>
        {
            // brass, copper, verdigris, oxblood, steel blue, bone, amber, olive
            [ColorArea.Accent] = Look.Colors("#D4A838", "#C0643A", "#5FB3A1", "#A8423A", "#6E8FB3", "#E2D3A6", "#FFB23E", "#8E9A3C"),
            // steel, cream enamel, brass, hammertone green, navy enamel, oxide red, olive drab, black
            [ColorArea.Plate] = Look.Colors("#B4B8BA", "#E6DCC0", "#C9A55A", "#4E6E5D", "#2E3F5C", "#8E3B2E", "#5B6136", "#2A2A2A"),
            // cast iron, gunmetal, oxblood, bottle green, navy, walnut, slate, bronze
            [ColorArea.Panel] = Look.Colors("#34302C", "#3A3F44", "#4A2622", "#263B30", "#1F2A3A", "#4A3424", "#44474A", "#5A4A30"),
        });

    /// <summary>The looks of the module.</summary>
    public static IReadOnlyList<Look> All { get; } = [Foundry];
}
