using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Looks;

/// <summary>
/// A look: a kit of faces and a resource dictionary with the palette and the chrome of controls and sections.
/// The control templates (chrome around a face host) and the face themes are shared by all looks.
/// </summary>
/// <param name="Theme">The resource dictionary of the look (palette and chrome themes).</param>
/// <param name="Accent">The accent of controls without a colour of their own or of their device.</param>
/// <param name="Swatches">Per area, colours that suit the look, offered first when the user picks a colour.</param>
/// <param name="ControlsOnPanel">
/// True when the controls have no plate of their own (it is transparent): their labels are on the panel, until the
/// plate is given a colour.
/// </param>
public sealed record Look(string Name, Kit Kit, Uri Theme, RgbColor Accent,
    IReadOnlyDictionary<ColorArea, IReadOnlyList<RgbColor>> Swatches, bool ControlsOnPanel = false)
{
    public override string ToString() => Name;
}

/// <summary>
/// The looks of the console, and applying one to the application.
/// </summary>
public static class ConsoleLooks
{
    private static readonly Uri BaseUri = new("avares://Jacobi.Midi.Console/");

    /// <summary>
    /// Accents of metals, patina and lamps; plates of steel and enamel; panels of cast iron and painted metal.
    /// </summary>
    public static Look Foundry { get; } = new("Foundry", Kits.Foundry, new Uri(BaseUri, "Themes/Foundry.axaml"),
        RgbColor.Parse("#D4A838"),
        new Dictionary<ColorArea, IReadOnlyList<RgbColor>>
        {
            // brass, copper, verdigris, oxblood, steel blue, bone, amber, olive
            [ColorArea.Accent] = Colors("#D4A838", "#C0643A", "#5FB3A1", "#A8423A", "#6E8FB3", "#E2D3A6", "#FFB23E", "#8E9A3C"),
            // steel, cream enamel, brass, hammertone green, navy enamel, oxide red, olive drab, black
            [ColorArea.Plate] = Colors("#B4B8BA", "#E6DCC0", "#C9A55A", "#4E6E5D", "#2E3F5C", "#8E3B2E", "#5B6136", "#2A2A2A"),
            // cast iron, gunmetal, oxblood, bottle green, navy, walnut, slate, bronze
            [ColorArea.Panel] = Colors("#34302C", "#3A3F44", "#4A2622", "#263B30", "#1F2A3A", "#4A3424", "#44474A", "#5A4A30"),
        });

    /// <summary>Accents of LEDs; plates and panels of anodized aluminium.</summary>
    public static Look Anodized { get; } = new("Anodized", Kits.Anodized, new Uri(BaseUri, "Themes/Anodized.axaml"),
        RgbColor.Parse("#3CC8F0"),
        new Dictionary<ColorArea, IReadOnlyList<RgbColor>>
        {
            // cyan, amber, magenta, lime, red, violet, white, orange
            [ColorArea.Accent] = Colors("#3CC8F0", "#FFB347", "#E05FD0", "#9BE564", "#FF5A5A", "#9C8CFF", "#E8ECF0", "#FF8A3C"),
            // graphite, silver, blue, red, gold, purple, teal, black
            [ColorArea.Plate] = Colors("#3A3E44", "#A9B0B8", "#2D4F7A", "#7A2D2D", "#8C7440", "#4F3A6E", "#2D6A6A", "#1E2023"),
            // aluminium, blue, red, green, purple, grey, light grey, black
            [ColorArea.Panel] = Colors("#34373B", "#1E2A3A", "#3A1F24", "#1F3A33", "#33293F", "#3A3A3A", "#4A4F55", "#121315"),
        },
        ControlsOnPanel: true);

    public static IReadOnlyList<Look> All { get; } = [Foundry, Anodized];

    private static ResourceInclude? _applied;

    /// <summary>
    /// Replaces the look's resources (palette, chrome), accent and kit in the application resources. Faces that
    /// exist keep their kit: rebuild the controls to get the faces of the new kit.
    /// </summary>
    public static void Apply(Application application, Look look)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(look);
        var dictionaries = application.Resources.MergedDictionaries;
        if (_applied is not null)
            dictionaries.Remove(_applied);
        _applied = new ResourceInclude(BaseUri) { Source = look.Theme };
        dictionaries.Add(_applied);
        PaletteBrushes.ApplyAccent(application.Resources, look.Accent);
        application.Resources[Kits.ResourceKey] = look.Kit;
    }

    private static IReadOnlyList<RgbColor> Colors(params string[] colors) => [.. colors.Select(RgbColor.Parse)];
}
