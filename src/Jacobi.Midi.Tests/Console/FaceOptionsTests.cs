using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Faces;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The options of the controls' chrome and faces: their declarations per control kind, the values in their normal
/// form, and how a control's own values, a look's defaults and the declared defaults make the values a face draws with.
/// </summary>
public class FaceOptionsTests
{
    [Fact]
    public void Of_AKnob_HasTheChromeAndKnobOptions()
    {
        Assert.Equal(["plate", "fasteners", "label", "track", "body", "pointer", "width", "ends", "value"],
            FaceOptions.Of(ParameterControlKind.Knob).Select(option => option.Name));
        Assert.Equal(["plate", "fasteners", "label", "value", "track", "cap", "width", "segments", "ends"], FaceOptions.Of(ParameterControlKind.Slider).Select(option => option.Name));
        Assert.Equal(["headers", "headerWidth"], FaceOptions.Of(ParameterControlKind.SelectorTabs).Select(option => option.Name));
        Assert.Equal(["headers", "headerWidth"], FaceOptions.Of(LayoutControlKind.Tabs).Select(option => option.Name));
        Assert.Empty(FaceOptions.Of(LayoutControlKind.Grid));
    }

    [Theory]
    [InlineData("body", "True", "true")]
    [InlineData("width", "8.0", "8")]
    [InlineData("width", "2.5", "2.5")]
    [InlineData("ends", "Flat", "flat")]
    public void TryNormalize_GivesTheValueInItsNormalForm(string name, string text, string expected)
    {
        var option = FaceOptions.Find(ParameterControlKind.Knob, name)!;

        Assert.True(option.TryNormalize(text, out var value, out _));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Complete_TheControlsValuesWin_OverTheLooks_OverTheDeclaredDefaults()
    {
        var look = OptionValues.Of("plate=false", "body=false", "width=8");
        var control = OptionValues.Of("width=3");

        var options = FaceOptions.Complete(ParameterControlKind.Knob, control.Over(look));

        Assert.Equal(3, options.Number("width"));
        Assert.False(options.Switch("body"));
        Assert.False(options.Switch("plate"));
        Assert.True(options.Switch("pointer"));
        Assert.Equal("round", options.Choice("ends"));
    }

    [Fact]
    public void With_Null_MakesTheOptionInheritedAgain()
    {
        var options = OptionValues.Of("body=false").With("width", "8").With("body", null);

        Assert.Equal(OptionValues.Of("width=8"), options);
        Assert.Null(options["body"]);
    }

    [Fact]
    public void AnOptionThatIsNotDeclared_IsReported()
    {
        var options = FaceOptions.Complete(ParameterControlKind.Slider, OptionValues.Empty);

        Assert.Throws<InvalidOperationException>(() => options.Switch("body"));
    }

    [Theory]
    [InlineData(null, null, 12, 8)]
    [InlineData("left", null, 7, 10)]
    [InlineData("left", "3", 9, 10)]
    [InlineData("top", "3", 12, 8)]
    public void PageSize_IsBesideTheHeaders(string? headers, string? width, int columns, int rows)
    {
        var tabs = new LayoutControl { Kind = LayoutControlKind.Tabs, Options = OptionValues.Empty.With("headers", headers).With("headerWidth", width) };

        Assert.Equal(new CellSize(columns, rows), GridLayout.PageSize(tabs, new CellSize(12, 10)));
    }
}
