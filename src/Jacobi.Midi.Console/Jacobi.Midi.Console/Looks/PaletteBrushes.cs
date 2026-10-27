using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Palettes;

namespace Jacobi.Midi.Console.Looks;

/// <summary>
/// The resource keys of the colours of each <see cref="ColorArea"/>, and setting them in a resource dictionary: the
/// application's for the look's accent, a control's for its own colours or its device's. From one colour it derives
/// what the area needs (see <see cref="Palette"/>): the accent's text and display colours, a plate's or panel's
/// material (the look's gradient, tinted) and the text on a plate. The looks use these keys, never a colour of their
/// own, for everything drawn in, on or with these areas.
/// </summary>
public static class PaletteBrushes
{
    /// <summary>The accent: values, LEDs, tags and strips.</summary>
    public const string AccentKey = "ConsoleAccentBrush";

    /// <summary>Text on the accent (a section tag).</summary>
    public const string AccentTextKey = "ConsoleAccentTextBrush";

    /// <summary>The accent for text and figures in displays and on dark panels.</summary>
    public const string AccentDisplayKey = "ConsoleAccentDisplayBrush";

    /// <summary>The material of a control's plate.</summary>
    public const string PlateKey = "ConsolePlateBrush";

    /// <summary>Text on a control's plate (its label).</summary>
    public const string PlateTextKey = "ConsolePlateTextBrush";

    /// <summary>The material of a section's panel.</summary>
    public const string PanelKey = "ConsoleSectionBrush";

    /// <summary>The keys an area sets.</summary>
    public static IReadOnlyList<string> KeysOf(ColorArea area)
        => area switch
        {
            ColorArea.Accent => [AccentKey, AccentTextKey, AccentDisplayKey],
            ColorArea.Plate => [PlateKey, PlateTextKey],
            ColorArea.Panel => [PanelKey],
            _ => throw new ArgumentOutOfRangeException(nameof(area)),
        };

    public static void ApplyAccent(IResourceDictionary resources, RgbColor accent)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var palette = Palette.Derive(accent);
        resources[AccentKey] = new SolidColorBrush(ToColor(palette.Accent));
        resources[AccentTextKey] = new SolidColorBrush(ToColor(palette.OnAccent));
        resources[AccentDisplayKey] = new SolidColorBrush(ToColor(palette.AccentOnDisplay));
    }

    /// <summary>
    /// Sets the resources of an area for a colour, or back to those of the look (<paramref name="color"/> null): for
    /// a control whose colour differs from the colour around it.
    /// </summary>
    public static void Apply(IResourceDictionary resources, ColorArea area, RgbColor? color)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (color is not { } value)
        {
            foreach (var key in KeysOf(area))
                resources[key] = LookResource(key);
            return;
        }

        switch (area)
        {
            case ColorArea.Accent:
                ApplyAccent(resources, value);
                break;
            case ColorArea.Plate:
                resources[PlateKey] = Tint(LookResource(PlateKey), value);
                ApplyPlateText(resources, value);
                break;
            case ColorArea.Panel:
                resources[PanelKey] = Tint(LookResource(PanelKey), value);
                break;
        }
    }

    /// <summary>Sets the text on the plate for a background colour, or back to the look's (null).</summary>
    public static void ApplyPlateText(IResourceDictionary resources, RgbColor? background)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resources[PlateTextKey] = background is { } color
            ? new SolidColorBrush(ToColor(Palette.Derive(color).OnAccent))
            : LookResource(PlateTextKey);
    }

    /// <summary>
    /// The colour the look gives an area (for the tints and shades when it has no colour of its own): the accent, or
    /// the middle of a material; a plate without material (transparent) has the colour of the panel.
    /// </summary>
    public static RgbColor LookColorOf(ColorArea area)
    {
        var key = KeysOf(area)[0];
        return LookResource(key) switch
        {
            ISolidColorBrush { Color.A: > 0 } solid => FromColor(solid.Color),
            IGradientBrush { GradientStops.Count: > 0 } gradient when gradient.GradientStops.Any(stop => stop.Color.A > 0)
                => FromColor(gradient.GradientStops[gradient.GradientStops.Count / 2].Color),
            _ => area == ColorArea.Plate ? LookColorOf(ColorArea.Panel) : new RgbColor(0x80, 0x80, 0x80),
        };
    }

    /// <summary>
    /// A material in another colour: the stops of a gradient tinted (<see cref="Palette.Tint"/>), a solid colour
    /// replaced; a material that is not there (transparent) becomes a plain one (<see cref="Palette.Shading"/>).
    /// </summary>
    public static IBrush Tint(object? material, RgbColor color)
    {
        if (material is IGradientBrush gradient && gradient.GradientStops.Any(stop => stop.Color.A > 0))
        {
            var colors = Palette.Tint([.. gradient.GradientStops.Select(stop => FromColor(stop.Color))], color);
            var stops = new GradientStops();
            stops.AddRange(gradient.GradientStops.Select((stop, index) => new GradientStop(ToColor(colors[index]), stop.Offset)));
            GradientBrush? tinted = gradient switch
            {
                ILinearGradientBrush linear => new LinearGradientBrush { StartPoint = linear.StartPoint, EndPoint = linear.EndPoint },
                IRadialGradientBrush radial => new RadialGradientBrush
                {
                    Center = radial.Center, GradientOrigin = radial.GradientOrigin, RadiusX = radial.RadiusX, RadiusY = radial.RadiusY,
                },
                IConicGradientBrush conic => new ConicGradientBrush { Center = conic.Center, Angle = conic.Angle },
                _ => null,
            };
            if (tinted is not null)
            {
                tinted.SpreadMethod = gradient.SpreadMethod;
                tinted.GradientStops = stops;
                return tinted;
            }
        }
        else if (material is ISolidColorBrush { Color.A: > 0 })
        {
            return new SolidColorBrush(ToColor(color));
        }

        var shading = Palette.Shading(color);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(ToColor(shading[0]), 0), new GradientStop(ToColor(shading[1]), 1)],
        };
    }

    public static Color ToColor(RgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static RgbColor FromColor(Color color) => new(color.R, color.G, color.B, color.A);

    // the look's value of a key: the application's resources (the look's dictionary and its accent)
    private static object? LookResource(string key)
        => Application.Current is { } application && application.TryGetResource(key, application.ActualThemeVariant, out var value)
            ? value
            : null;
}
