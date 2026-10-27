namespace Jacobi.Midi.Console.Core.Palettes;

/// <summary>
/// The colours a control is drawn with, derived from one colour (a device's, a control's own or the look's accent):
/// the accent itself, the text on the accent (a label on a coloured tag) and the accent for text and figures in the
/// displays (readouts, OLEDs), lightened where needed to stay readable on their dark background. For the materials
/// (plates, panels): <see cref="Tint"/> and the text on them, <see cref="OnAccent"/> of their colour.
/// </summary>
public sealed record Palette(RgbColor Accent, RgbColor OnAccent, RgbColor AccentOnDisplay)
{
    /// <summary>The contrast text needs with its background (WCAG 2, normal text).</summary>
    public const double TextContrast = 4.5;

    /// <summary>The background the accent's text in displays is made readable on: the displays of the looks are dark.</summary>
    public static RgbColor DisplayBackground { get; } = new(0x10, 0x10, 0x10);

    private static readonly RgbColor DarkText = new(0x1A, 0x1A, 0x1A);
    private static readonly RgbColor LightText = new(0xF5, 0xF5, 0xF5);

    public static Palette Derive(RgbColor accent)
    {
        var onAccent = accent.ContrastWith(DarkText) >= accent.ContrastWith(LightText) ? DarkText : LightText;

        var onDisplay = accent with { A = 255 };
        var (_, _, lightness) = onDisplay.ToHsl();
        while (onDisplay.ContrastWith(DisplayBackground) < TextContrast && lightness < 1)
        {
            lightness = Math.Min(1, lightness + 0.05);
            onDisplay = onDisplay.WithLightness(lightness);
        }
        return new Palette(accent, onAccent, onDisplay);
    }

    /// <summary>
    /// Gives a material (the colours of a gradient: steel, cast iron) another colour and keeps its shading: each colour
    /// gets the hue and saturation of <paramref name="color"/>, and its lightness differs from that of
    /// <paramref name="color"/> as much as it differs from the material's average. Transparency is kept.
    /// </summary>
    public static IReadOnlyList<RgbColor> Tint(IReadOnlyList<RgbColor> material, RgbColor color)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.Count == 0)
            return [];
        var (hue, saturation, lightness) = color.ToHsl();
        var average = material.Average(stop => stop.ToHsl().Lightness);
        return [.. material.Select(stop => RgbColor.FromHsl(hue, saturation, lightness + stop.ToHsl().Lightness - average, stop.A))];
    }

    /// <summary>A plain material in a colour, for a look without one: lit from above, lighter to darker.</summary>
    public static IReadOnlyList<RgbColor> Shading(RgbColor color)
    {
        var (_, _, lightness) = color.ToHsl();
        return [color.WithLightness(lightness + 0.06), color.WithLightness(lightness - 0.06)];
    }

    /// <summary>
    /// Lighter and darker colours of the same hue: <paramref name="steps"/> on each side of the colour (which is in
    /// the middle), evenly spread between it and almost white and almost black; darkest first.
    /// </summary>
    public static IReadOnlyList<RgbColor> TintsAndShades(RgbColor color, int steps = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(steps);
        var (_, _, lightness) = color.ToHsl();
        var colors = new List<RgbColor>();
        for (var step = steps; step > 0; step--)
            colors.Add(color.WithLightness(lightness - (lightness - 0.12) * step / (steps + 1)));
        colors.Add(color);
        for (var step = 1; step <= steps; step++)
            colors.Add(color.WithLightness(lightness + (0.9 - lightness) * step / (steps + 1)));
        return colors;
    }
}
