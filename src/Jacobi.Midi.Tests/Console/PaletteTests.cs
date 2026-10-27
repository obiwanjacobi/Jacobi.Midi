using Jacobi.Midi.Console.Core.Palettes;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The colours of the console: their notation, contrast and lightness, and the palette, tints and shades derived
/// from one colour.
/// </summary>
public class PaletteTests
{
    [Theory]
    [InlineData("#D4A838", 0xD4, 0xA8, 0x38, 0xFF)]
    [InlineData("#80102030", 0x10, 0x20, 0x30, 0x80)]
    [InlineData("#d4a838", 0xD4, 0xA8, 0x38, 0xFF)]
    public void TryParse_ReadsRgbAndArgb(string text, int r, int g, int b, int a)
    {
        Assert.True(RgbColor.TryParse(text, out var color));
        Assert.Equal(new RgbColor((byte)r, (byte)g, (byte)b, (byte)a), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("red")]
    [InlineData("D4A838")]
    [InlineData("#D4A83")]
    [InlineData("#D4A83G")]
    public void TryParse_RejectsWhatIsNoColour(string? text)
        => Assert.False(RgbColor.TryParse(text, out _));

    [Theory]
    [InlineData("#D4A838")]
    [InlineData("#80102030")]
    public void ToString_IsTheNotationOfTheFiles(string text)
        => Assert.Equal(text, RgbColor.Parse(text).ToString());

    [Fact]
    public void ContrastWith_IsOneToTwentyOne()
    {
        var black = new RgbColor(0, 0, 0);
        var white = new RgbColor(255, 255, 255);

        Assert.Equal(21, black.ContrastWith(white), 3);
        Assert.Equal(21, white.ContrastWith(black), 3);
        Assert.Equal(1, white.ContrastWith(white), 3);
    }

    [Theory]
    [InlineData("#D4A838")]
    [InlineData("#3CC8F0")]
    [InlineData("#A8423A")]
    [InlineData("#808080")]
    public void Hsl_RoundTrips(string text)
    {
        var color = RgbColor.Parse(text);
        var (hue, saturation, lightness) = color.ToHsl();

        Assert.Equal(color, RgbColor.FromHsl(hue, saturation, lightness));
    }

    [Theory]
    [InlineData("#D4A838")]     // light: dark text on it
    [InlineData("#E8ECF0")]
    [InlineData("#1B3A8C")]     // dark: light text on it
    [InlineData("#A8423A")]
    public void Derive_GivesTextOnTheAccentWithContrast(string text)
    {
        var palette = Palette.Derive(RgbColor.Parse(text));

        Assert.True(palette.OnAccent.ContrastWith(palette.Accent) >= Palette.TextContrast);
    }

    [Fact]
    public void Derive_LightensTheAccentForDisplays_OnlyWhenNeeded()
    {
        var light = RgbColor.Parse("#3CC8F0");
        var dark = RgbColor.Parse("#1B3A8C");

        Assert.Equal(light, Palette.Derive(light).AccentOnDisplay);
        var onDisplay = Palette.Derive(dark).AccentOnDisplay;
        Assert.True(onDisplay.ContrastWith(Palette.DisplayBackground) >= Palette.TextContrast);
        Assert.True(onDisplay.ToHsl().Lightness > dark.ToHsl().Lightness);
        Assert.InRange(onDisplay.ToHsl().Hue - dark.ToHsl().Hue, -2, 2);     // the same hue, but for rounding to bytes
    }

    [Fact]
    public void Tint_GivesTheMaterialTheColour_AndKeepsItsShading()
    {
        // brushed steel: light to dark grey
        IReadOnlyList<RgbColor> steel = [RgbColor.Parse("#CDD0D1"), RgbColor.Parse("#9A9EA0")];
        var navy = RgbColor.Parse("#2E3F5C");

        var tinted = Palette.Tint(steel, navy);

        Assert.Equal(2, tinted.Count);
        var (hue, _, lightness) = navy.ToHsl();
        Assert.All(tinted, color => Assert.InRange(color.ToHsl().Hue - hue, -2, 2));
        // the same difference in lightness, around the colour's lightness
        Assert.Equal(steel[0].ToHsl().Lightness - steel[1].ToHsl().Lightness, tinted[0].ToHsl().Lightness - tinted[1].ToHsl().Lightness, 1);
        Assert.Equal(lightness, tinted.Average(color => color.ToHsl().Lightness), 1);
    }

    [Fact]
    public void Tint_KeepsTransparency()
    {
        var tinted = Palette.Tint([new RgbColor(0x80, 0x80, 0x80, 0x40)], RgbColor.Parse("#8E3B2E"));

        Assert.Equal(0x40, tinted[0].A);
    }

    [Fact]
    public void AreaColors_Over_TakesTheOthersWhereItHasNone()
    {
        var dashboard = new AreaColors(Panel: "#263B30");
        var rig = new AreaColors("#D4A838", Panel: "#34302C");

        Assert.Equal(new AreaColors("#D4A838", Panel: "#263B30"), dashboard.Over(rig));
        Assert.Equal("#263B30", dashboard[ColorArea.Panel]);
        Assert.Equal(new AreaColors(Plate: "#B4B8BA", Panel: "#263B30"), dashboard.With(ColorArea.Plate, "#B4B8BA"));
    }

    [Fact]
    public void TintsAndShades_AreDarkToLight_WithTheColourInTheMiddle()
    {
        var color = RgbColor.Parse("#5FB3A1");

        var colors = Palette.TintsAndShades(color, 3);

        Assert.Equal(7, colors.Count);
        Assert.Equal(color, colors[3]);
        var lightness = colors.Select(each => each.ToHsl().Lightness).ToList();
        Assert.Equal(lightness.Order(), lightness);
        Assert.Equal(lightness.Count, lightness.Distinct().Count());
    }
}
