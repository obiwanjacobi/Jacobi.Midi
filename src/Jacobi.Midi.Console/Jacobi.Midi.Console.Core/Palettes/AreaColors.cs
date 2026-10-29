namespace Jacobi.Midi.Console.Core.Palettes;

/// <summary>The parts of controls and sections that can be given a colour.</summary>
public enum ColorArea
{
    /// <summary>What shows the value and the device: arcs, LEDs, a section's tag or strip.</summary>
    Accent,

    /// <summary>The material the control is mounted on (e.g. a steel plate).</summary>
    Plate,

    /// <summary>The material of a section (e.g. cast iron, aluminium).</summary>
    Panel,
}

/// <summary>
/// The colours of a control, a section or a device, one per <see cref="ColorArea"/> (<c>#RRGGBB</c>); null where it
/// has none, so the colour of that area is inherited. In the files: <c>color</c> (the accent), <c>plateColor</c> and
/// <c>panelColor</c>.
/// </summary>
public sealed record AreaColors(string? Accent = null, string? Plate = null, string? Panel = null)
{
    public static AreaColors None { get; } = new();

    public static IReadOnlyList<ColorArea> Areas { get; } = Enum.GetValues<ColorArea>();

    public string? this[ColorArea area]
        => area switch
        {
            ColorArea.Accent => Accent,
            ColorArea.Plate => Plate,
            ColorArea.Panel => Panel,
            _ => throw new ArgumentOutOfRangeException(nameof(area)),
        };

    /// <summary>A copy with another colour (or none) for one area.</summary>
    public AreaColors With(ColorArea area, string? color)
        => area switch
        {
            ColorArea.Accent => this with { Accent = color },
            ColorArea.Plate => this with { Plate = color },
            ColorArea.Panel => this with { Panel = color },
            _ => throw new ArgumentOutOfRangeException(nameof(area)),
        };

    /// <summary>The colours of this one, else of the other: an override on top of <paramref name="under"/>.</summary>
    public AreaColors Over(AreaColors under)
        => new(Accent ?? under.Accent, Plate ?? under.Plate, Panel ?? under.Panel);

    /// <summary>The name of an area as the user sees it.</summary>
    public static string NameOf(ColorArea area)
        => area switch
        {
            ColorArea.Accent => "Accent",
            ColorArea.Plate => "Plate",
            ColorArea.Panel => "Panel",
            _ => area.ToString(),
        };
}
