using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Looks;

/// <summary>
/// A look: a kit of faces and a resource dictionary with the palette and the chrome of controls and sections.
/// The control templates (chrome around a face host) and the face themes are shared by all looks.
/// </summary>
/// <param name="Theme">The resource dictionary of the look (palette and chrome themes).</param>
public sealed record Look(string Name, Kit Kit, Uri Theme)
{
    public override string ToString() => Name;
}

/// <summary>
/// The looks of the console, and applying one to the application.
/// </summary>
public static class ConsoleLooks
{
    private static readonly Uri BaseUri = new("avares://Jacobi.Midi.Console/");

    public static Look Foundry { get; } = new("Foundry", Kits.Foundry, new Uri(BaseUri, "Themes/Foundry.axaml"));

    public static Look Anodized { get; } = new("Anodized", Kits.Anodized, new Uri(BaseUri, "Themes/Anodized.axaml"));

    public static IReadOnlyList<Look> All { get; } = [Foundry, Anodized];

    private static ResourceInclude? _applied;

    /// <summary>
    /// Replaces the look's resources (palette, chrome) and kit in the application resources. Faces that exist keep
    /// their kit: rebuild the controls to get the faces of the new kit.
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
        application.Resources[Kits.ResourceKey] = look.Kit;
    }
}
