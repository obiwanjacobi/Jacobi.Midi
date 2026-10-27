using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Jacobi.Midi.Console.Core.Palettes;

/// <summary>
/// A colour as written in the console's files (<c>#RRGGBB</c>, or <c>#AARRGGBB</c> with transparency), with the
/// colour arithmetic of the palettes: relative luminance and contrast (WCAG 2) and hue, saturation and lightness.
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B, byte A = 255)
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out RgbColor? color)
    {
        color = null;
        if (text is null || text.Length is not (7 or 9) || text[0] != '#'
            || !UInt32.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }
        var alpha = text.Length == 9 ? (byte)(value >> 24) : (byte)255;
        color = new RgbColor((byte)(value >> 16), (byte)(value >> 8), (byte)value, alpha);
        return true;
    }

    public static RgbColor Parse(string text)
        => TryParse(text, out var color) ? color.Value : throw new FormatException($"'{text}' is not a colour (#RRGGBB or #AARRGGBB).");

    /// <summary>The colour as written in a file: <c>#RRGGBB</c>, or <c>#AARRGGBB</c> when it is transparent.</summary>
    public override string ToString()
        => A == 255
            ? String.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
            : String.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");

    /// <summary>The relative luminance (0 black - 1 white) as WCAG 2 defines it.</summary>
    public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

    /// <summary>The contrast ratio with another colour (1-21) as WCAG 2 defines it; 4.5 is enough for text.</summary>
    public double ContrastWith(RgbColor other)
    {
        var (lighter, darker) = Luminance >= other.Luminance ? (Luminance, other.Luminance) : (other.Luminance, Luminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Hue (0-360), saturation and lightness (0-1).</summary>
    public (double Hue, double Saturation, double Lightness) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max == min)
            return (0, 0, lightness);

        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        var hue = max == r ? (g - b) / delta + (g < b ? 6 : 0)
            : max == g ? (b - r) / delta + 2
            : (r - g) / delta + 4;
        return (hue * 60, saturation, lightness);
    }

    public static RgbColor FromHsl(double hue, double saturation, double lightness, byte alpha = 255)
    {
        lightness = Math.Clamp(lightness, 0, 1);
        saturation = Math.Clamp(saturation, 0, 1);
        if (saturation == 0)
        {
            var gray = ToByte(lightness);
            return new RgbColor(gray, gray, gray, alpha);
        }

        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        var h = hue / 360;
        return new RgbColor(ToByte(Channel(p, q, h + 1.0 / 3)), ToByte(Channel(p, q, h)), ToByte(Channel(p, q, h - 1.0 / 3)), alpha);
    }

    /// <summary>The same hue and saturation with another lightness (0-1).</summary>
    public RgbColor WithLightness(double lightness)
    {
        var (hue, saturation, _) = ToHsl();
        return FromHsl(hue, saturation, lightness, A);
    }

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        return t < 1.0 / 6 ? p + (q - p) * 6 * t
            : t < 1.0 / 2 ? q
            : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
            : p;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
