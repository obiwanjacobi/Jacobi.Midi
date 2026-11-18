using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Faces;
using Jacobi.Midi.Console.Looks.Foundry;

namespace Jacobi.Midi.Console.Looks.Lcars;

/// <summary>
/// The LCARS module: the starship console look of flat pastel bars, rounded caps and condensed capitals on black.
/// Everything the module offers (palette, chrome, swatches, kit and option defaults) is in its folder;
/// <see cref="ConsoleLooks"/> lists its <see cref="All"/>.
/// </summary>
internal static class LcarsLooks
{
    /// <summary>
    /// The faces of the look: those of the Foundry module (the arc knob among them), drawn flat with the LCARS palette
    /// and the option defaults of <see cref="Lcars"/>, and a switch of its own: the ON/OFF pair of keys.
    /// </summary>
    public static Kit Kit { get; } = FoundryLooks.Kit.With("lcars", ParameterControlKind.Switch, () => new PairSwitchFace());

    /// <summary>
    /// Accents of the LCARS pastels; plates in the same pastels (black labels) or dark; panels black or nearly black.
    /// </summary>
    public static Look Lcars { get; } = new("LCARS", Kit,
        Look.ThemeOf(typeof(LcarsLooks), "Looks/Lcars/Lcars.axaml"),
        RgbColor.Parse("#FF9900"),
        new Dictionary<ColorArea, IReadOnlyList<RgbColor>>
        {
            // orange, peach, lavender, blue-violet, red, salmon, gold, sky
            [ColorArea.Accent] = Look.Colors("#FF9900", "#FFCC99", "#CC99CC", "#9999FF", "#CC6666", "#FF9966", "#FFCC66", "#99CCFF"),
            // orange, peach, lavender, blue-violet, red, salmon, violet grey, black
            [ColorArea.Plate] = Look.Colors("#FF9900", "#FFCC99", "#CC99CC", "#9999FF", "#CC6666", "#FF9966", "#2E2633", "#000000"),
            // black, night violet, dusk violet, violet grey, night blue, night red, night brown, night teal
            [ColorArea.Panel] = Look.Colors("#000000", "#120E16", "#1A1420", "#2E2633", "#14182A", "#2A1414", "#1E1A10", "#0E1A1A"),
        })
    {
        // the controls are on the panel, their labels below; flat parts without displays: a knob is a thick flat arc with
        // its value inside, a slider a meter of segments, the keys of a selector a pill
        ChromeOptions = OptionValues.Of("plate=false", "label=below"),
        FaceOptions = new Dictionary<ParameterControlKind, OptionValues>
        {
            [ParameterControlKind.Knob] = OptionValues.Of("body=false", "pointer=false", "width=8", "ends=flat", "value=inside"),
            [ParameterControlKind.Slider] = OptionValues.Of("cap=false", "width=24", "segments=10", "ends=flat"),
            [ParameterControlKind.Switch] = OptionValues.Of("ends=round"),
            [ParameterControlKind.Selector] = OptionValues.Of("display=false", "ends=round"),
            [ParameterControlKind.NumberBox] = OptionValues.Of("display=false"),
            [ParameterControlKind.Text] = OptionValues.Of("display=false"),
            [ParameterControlKind.XyPad] = OptionValues.Of("display=false"),
            [ParameterControlKind.Envelope] = OptionValues.Of("display=false"),
        },
    };

    /// <summary>The looks of the module.</summary>
    public static IReadOnlyList<Look> All { get; } = [Lcars];
}
