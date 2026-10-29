using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Faces;
using Jacobi.Midi.Console.Looks.Anodized;
using Jacobi.Midi.Console.Looks.Foundry;

namespace Jacobi.Midi.Console.Looks;

/// <summary>
/// A look: a kit of faces and a resource dictionary with the palette and the chrome of controls and sections.
/// The control templates (chrome around a face host) and the face theme are shared by all looks. A look is defined
/// by its module: the folder under <c>Looks</c> with its kit, faces and resources.
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

    /// <summary>
    /// The URI of a resource dictionary of a module: <paramref name="path"/> in the assembly of
    /// <paramref name="moduleType"/>, so the module does not depend on the assembly it is in.
    /// </summary>
    public static Uri ThemeOf(Type moduleType, string path)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        return new Uri($"avares://{moduleType.Assembly.GetName().Name}/{path}");
    }

    /// <summary>Colours (<c>#RRGGBB</c>) for the swatches of a look.</summary>
    public static IReadOnlyList<RgbColor> Colors(params string[] colors) => [.. colors.Select(RgbColor.Parse)];
}

/// <summary>
/// The looks of the console (the looks of all modules), and applying one to the application.
/// </summary>
public static class ConsoleLooks
{
    public static IReadOnlyList<Look> All { get; } = [.. FoundryLooks.All, .. AnodizedLooks.All];

    /// <summary>The look the console starts with.</summary>
    public static Look Default => All[0];

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
        _applied = new ResourceInclude(look.Theme) { Source = look.Theme };
        dictionaries.Add(_applied);
        PaletteBrushes.ApplyAccent(application.Resources, look.Accent);
        application.Resources[Kit.ResourceKey] = look.Kit;
    }
}
