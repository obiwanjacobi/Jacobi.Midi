using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Parameters;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The pages of selector tabs: matched to their option by name without case, a page per option when bound to another
/// field, and the pages and options that do not fit reported.
/// </summary>
public class SelectorPagesTests
{
    private static readonly IReadOnlyList<string> Waves = ["Saw", "Pulse", "Noise"];

    private static LayoutControl Page(string label) => new() { Kind = LayoutControlKind.Grid, Label = label };

    private static BoundField Field(string path, IReadOnlyList<string>? options = null)
        => new("synth", path, new ParameterInfo(path, typeof(string), null, 1, options ?? Waves));

    [Fact]
    public void PageOf_MatchesTheOptionWithoutCase()
    {
        var tabs = new SelectorTabsControl { Children = [Page("saw"), Page("PULSE")] };

        Assert.Equal(0, SelectorPages.PageOf(tabs, "Saw"));
        Assert.Equal(1, SelectorPages.PageOf(tabs, "Pulse"));
        Assert.Null(SelectorPages.PageOf(tabs, "Noise"));
        Assert.Null(SelectorPages.PageOf(tabs, null));
    }

    [Fact]
    public void For_GivesAGridPerOption_InTheSizeBelowTheHeaders()
    {
        var tabs = new SelectorTabsControl { Size = new(12, 10) };

        var pages = SelectorPages.For(tabs, Waves);

        Assert.Equal(Waves, pages.Select(page => page.Label));
        Assert.All(pages, page => Assert.True(SelectorTabsControl.IsPage(page)));
        Assert.All(pages, page => Assert.Equal(new CellSize(12, 8), page.Size));
    }

    [Fact]
    public void Rebound_ToAnotherField_GivesThePagesOfItsOptions()
    {
        var tabs = new SelectorTabsControl { Size = new(12, 10), Children = [Page("Old")] };

        var pages = SelectorPages.Rebound(tabs, Field("AddressMap.Osc.Mode", ["Old"]), Field("AddressMap.Osc.Wave"));

        Assert.Equal(Waves, pages!.Select(page => page.Label));
    }

    [Fact]
    public void Rebound_TheFirstTime_GivesThePagesOfTheOptions()
    {
        var tabs = new SelectorTabsControl { Size = new(12, 10) };

        Assert.Equal(Waves, SelectorPages.Rebound(tabs, null, Field("AddressMap.Osc.Wave"))!.Select(page => page.Label));
    }

    [Fact]
    public void Rebound_ToTheSameField_OrUnbound_KeepsThePages()
    {
        var tabs = new SelectorTabsControl { Children = [Page("Saw")] };

        Assert.Null(SelectorPages.Rebound(tabs, Field("AddressMap.Osc.Wave"), Field("AddressMap.Osc.Wave") with { Device = "SYNTH" }));
        Assert.Null(SelectorPages.Rebound(tabs, Field("AddressMap.Osc.Wave"), null));
    }

    [Fact]
    public void Problems_AreAPageOfNoOption_AndAnOptionWithMorePages()
    {
        var tabs = new SelectorTabsControl { Children = [Page("Saw"), Page("Square"), Page("saw")] };

        var problems = SelectorPages.Problems(tabs, Waves);

        Assert.Equal(2, problems.Count);
        Assert.Contains("the page 'Square' is of no option", problems[0]);
        Assert.Contains("the option 'Saw' has more than one page", problems[1]);
    }

    [Fact]
    public void Choices_AreTheOptionsInOrder_ThenThePagesOfNoOption()
    {
        var tabs = new SelectorTabsControl { Children = [Page("Old"), Page("noise")] };

        Assert.Equal(
            [new PageChoice("Saw", false, false), new PageChoice("Pulse", false, false), new PageChoice("Noise", true, false), new PageChoice("Old", true, true)],
            SelectorPages.Choices(tabs, Waves));
    }

    [Fact]
    public void WithPage_InsertsThePageInTheOrderOfTheOptions()
    {
        var tabs = new SelectorTabsControl { Size = new(12, 10), Children = [Page("Old"), Page("Saw"), Page("Noise")] };

        var pages = SelectorPages.WithPage(tabs, Waves, "pulse");

        Assert.Equal(["Old", "Saw", "Pulse", "Noise"], pages!.Select(page => page.Label));
        Assert.Equal(new CellSize(12, 8), pages[2].Size);
        Assert.Null(SelectorPages.WithPage(tabs, Waves, "SAW"));
        Assert.Null(SelectorPages.WithPage(tabs, Waves, "Square"));
    }

    [Fact]
    public void WithoutPage_TakesThePagesOfTheName()
    {
        var tabs = new SelectorTabsControl { Children = [Page("Old"), Page("Saw")] };

        Assert.Equal(["Saw"], SelectorPages.WithoutPage(tabs, "old")!.Select(page => page.Label));
        Assert.Null(SelectorPages.WithoutPage(tabs, "Pulse"));
    }

    [Fact]
    public void TheOnlyPage_CannotBeRemoved()
    {
        var saw = Page("Saw");
        var tabs = new SelectorTabsControl { Children = [saw] };

        Assert.False(SelectorPages.CanRemove(tabs, saw));
        Assert.Null(SelectorPages.WithoutPage(tabs, "Saw"));
        Assert.False(SelectorPages.Choices(tabs, Waves).Single(choice => choice.HasPage).CanChange);

        var pulse = Page("Pulse");
        tabs.Children = [saw, pulse];
        Assert.True(SelectorPages.CanRemove(tabs, saw));
        Assert.All(SelectorPages.Choices(tabs, Waves), choice => Assert.True(choice.CanChange));
    }

    [Fact]
    public void Problems_OfPagesForSomeOptions_AreNone()
    {
        var tabs = new SelectorTabsControl { Children = [Page("Noise"), Page("saw")] };

        Assert.Empty(SelectorPages.Problems(tabs, Waves));
    }
}
