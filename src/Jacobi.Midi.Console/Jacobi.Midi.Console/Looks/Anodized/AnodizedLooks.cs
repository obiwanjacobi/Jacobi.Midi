using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Looks.Anodized;

/// <summary>
/// The Anodized module: the machined aluminium kit of faces and the looks built on it. Everything the module offers
/// (faces, palette, chrome, swatches) is in its folder; <see cref="ConsoleLooks"/> lists its <see cref="All"/>.
/// </summary>
public static class AnodizedLooks
{
    /// <summary>Machined aluminium: LED rings and meters, machined caps and OLED displays.</summary>
    public static Kit Kit { get; } = new("anodized", new Dictionary<ParameterControlKind, Func<Face>>
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

    /// <summary>Accents of LEDs; plates and panels of anodized aluminium.</summary>
    public static Look Anodized { get; } = new("Anodized", Kit,
        Look.ThemeOf(typeof(AnodizedLooks), "Looks/Anodized/Anodized.axaml"),
        RgbColor.Parse("#3CC8F0"),
        new Dictionary<ColorArea, IReadOnlyList<RgbColor>>
        {
            // cyan, amber, magenta, lime, red, violet, white, orange
            [ColorArea.Accent] = Look.Colors("#3CC8F0", "#FFB347", "#E05FD0", "#9BE564", "#FF5A5A", "#9C8CFF", "#E8ECF0", "#FF8A3C"),
            // graphite, silver, blue, red, gold, purple, teal, black
            [ColorArea.Plate] = Look.Colors("#3A3E44", "#A9B0B8", "#2D4F7A", "#7A2D2D", "#8C7440", "#4F3A6E", "#2D6A6A", "#1E2023"),
            // aluminium, blue, red, green, purple, grey, light grey, black
            [ColorArea.Panel] = Look.Colors("#34373B", "#1E2A3A", "#3A1F24", "#1F3A33", "#33293F", "#3A3A3A", "#4A4F55", "#121315"),
        },
        ControlsOnPanel: true);

    /// <summary>The looks of the module.</summary>
    public static IReadOnlyList<Look> All { get; } = [Anodized];
}
