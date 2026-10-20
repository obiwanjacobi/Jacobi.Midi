using Avalonia.Styling;

namespace Jacobi.Midi.SchemaEditor.Themes;

/// <summary>
/// The custom theme variants of the Schema Editor.
/// Each variant selects its own set of brushes in Themes/Palette.axaml and
/// inherits from a built-in variant so the Fluent controls keep a matching look.
/// </summary>
public static class AppThemeVariants
{
    /// <summary>
    /// The deep blue 'night' look. Falls back to <see cref="ThemeVariant.Dark"/> for the Fluent controls.
    /// </summary>
    public static ThemeVariant Night { get; } = new("Night", ThemeVariant.Dark);
}
