using Jacobi.Midi.Console.Core.Dashboards;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The layout in cells: completing the sizes and positions a file leaves out, and placing a control so that the
/// controls it would overlap are pushed down and the grids around it grow.
/// </summary>
public class GridLayoutTests
{
    private static ValueControl Knob(string label, int column, int row)
        => new(ParameterControlKind.Knob) { Label = label, At = new(column, row), Size = new(2, 2) };

    private static LayoutControl Grid(params DashboardControl[] children)
        => new() { Kind = LayoutControlKind.Grid, Children = children };

    private static LayoutControl Section(string label, int column, int row, int columns, int rows)
        => new() { Kind = LayoutControlKind.Section, Label = label, At = new(column, row), Size = new(columns, rows) };

    [Fact]
    public void ShiftsUp_OfAStack_MovesAllBelowUp_KeepingTheGaps()
    {
        var a = Section("a", 0, 0, 12, 10);
        var b = Section("b", 0, 11, 12, 5);
        var c = Section("c", 0, 17, 12, 5);
        var grid = Grid(a, b, c);

        var shifts = GridLayout.ShiftsUp(grid, new Dictionary<DashboardControl, int> { [a] = 7 });

        Assert.Equal(new Dictionary<DashboardControl, int> { [b] = 7, [c] = 7 }, shifts);
    }

    [Fact]
    public void ShiftsUp_OfASectionInTheMiddleOfAStack_MovesThoseBelowItUp()
    {
        var a = Section("a", 0, 0, 12, 10);
        var b = Section("b", 0, 11, 12, 5);
        var c = Section("c", 0, 17, 12, 5);
        var d = Section("d", 0, 23, 12, 5);
        var grid = Grid(a, b, c, d);

        var shifts = GridLayout.ShiftsUp(grid, new Dictionary<DashboardControl, int> { [b] = 3 });

        Assert.Equal(new Dictionary<DashboardControl, int> { [c] = 3, [d] = 3 }, shifts);
    }

    [Fact]
    public void ShiftsUp_OfTwoSectionsInAStack_AddsUp()
    {
        var a = Section("a", 0, 0, 12, 10);
        var b = Section("b", 0, 11, 12, 5);
        var c = Section("c", 0, 17, 12, 5);
        var grid = Grid(a, b, c);

        var shifts = GridLayout.ShiftsUp(grid, new Dictionary<DashboardControl, int> { [a] = 7, [b] = 3 });

        Assert.Equal(new Dictionary<DashboardControl, int> { [b] = 7, [c] = 10 }, shifts);
    }

    [Fact]
    public void ShiftsUp_BelowTwoSideBySide_MovesTheLeast()
    {
        var left = Section("left", 0, 0, 12, 10);
        var right = Section("right", 12, 0, 12, 10);
        var underLeft = Knob("under left", 0, 10);
        var underBoth = Section("under both", 6, 12, 12, 5);
        var grid = Grid(left, right, underLeft, underBoth);

        var shifts = GridLayout.ShiftsUp(grid, new Dictionary<DashboardControl, int> { [left] = 7 });

        // the control under both stays: the right section did not shrink
        Assert.Equal(new Dictionary<DashboardControl, int> { [underLeft] = 7 }, shifts);
    }

    [Fact]
    public void ShiftsUp_NothingShrunk_MovesNothing()
    {
        var grid = Grid(Section("a", 0, 0, 12, 10), Knob("b", 0, 10));

        Assert.Empty(GridLayout.ShiftsUp(grid, new Dictionary<DashboardControl, int>()));
    }

    [Fact]
    public void Complete_GivesDefaultSizes_AndPlacesSideBySide()
    {
        var knob = new ValueControl(ParameterControlKind.Knob);
        var text = new ValueControl(ParameterControlKind.Text);
        var grid = Grid(knob, text);

        var given = GridLayout.Complete(grid);

        Assert.Equal(GridLayout.DefaultSize(ParameterControlKind.Knob), knob.Size);
        Assert.Equal(new CellPosition(0, 0), knob.At);
        Assert.Equal(new CellPosition(knob.Size!.Value.Columns, 0), text.At);
        // sizes and positions of both, and the grid's size
        Assert.Equal(5, given);
        Assert.Equal(new CellSize(knob.Size.Value.Columns + text.Size!.Value.Columns, Math.Max(knob.Size.Value.Rows, text.Size.Value.Rows)), grid.Size);
    }

    [Fact]
    public void Complete_OfASection_AddsItsInset()
    {
        var section = new LayoutControl { Kind = LayoutControlKind.Section, Children = [Knob("a", 0, 0)] };

        GridLayout.Complete(section);

        var inset = GridLayout.SectionInset;
        Assert.Equal(new CellSize(2 + inset.Left + inset.Right, 2 + inset.Top + inset.Bottom), section.Size);
    }

    [Fact]
    public void Complete_PlacesTheUnplaced_BelowThePlaced()
    {
        var placed = Knob("a", 3, 1);
        var unplaced = new ValueControl(ParameterControlKind.Knob) { Size = new(2, 2) };
        var grid = Grid(placed, unplaced);

        GridLayout.Complete(grid);

        Assert.Equal(new CellPosition(3, 1), placed.At);
        Assert.Equal(new CellPosition(0, 3), unplaced.At);
    }

    [Fact]
    public void Place_PushesOverlappedControlsDown_InCascade()
    {
        var a = Knob("a", 0, 0);
        var b = Knob("b", 4, 0);
        var c = Knob("c", 4, 2);
        var grid = Grid(a, b, c);
        GridLayout.Complete(grid);

        // a moves onto b: b goes below a, c below b
        var changes = GridLayout.Place(grid, a, new CellPosition(4, 1), new CellSize(2, 2));

        Assert.Equal(new CellPosition(4, 1), changes[a].At);
        Assert.Equal(new CellPosition(4, 3), changes[b].At);
        Assert.Equal(new CellPosition(4, 5), changes[c].At);
        // the grid grows to its children
        Assert.Equal(new CellSize(6, 7), changes[grid].Size);
        // nothing is changed yet
        Assert.Equal(new CellPosition(0, 0), a.At);
    }

    [Fact]
    public void Place_WithoutOverlap_OnlyChangesTheControl()
    {
        var a = Knob("a", 0, 0);
        var b = Knob("b", 4, 0);
        var grid = Grid(a, b);
        GridLayout.Complete(grid);

        var changes = GridLayout.Place(grid, a, new CellPosition(1, 0), new CellSize(3, 2));

        Assert.Equal([a], changes.Keys);
    }

    [Fact]
    public void Place_GrowsTheSection_AndPushesInItsGrid()
    {
        var inner = Knob("inner", 0, 0);
        var section = new LayoutControl { Kind = LayoutControlKind.Section, At = new(0, 0), Children = [inner] };
        var below = Knob("below", 0, 6);
        var root = Grid(section, below);
        GridLayout.Complete(root);
        Assert.Equal(new CellSize(4, 5), section.Size);

        // the knob in the section grows: the section grows with it and pushes 'below' down
        var changes = GridLayout.Place(root, inner, new CellPosition(0, 0), new CellSize(2, 4));

        Assert.Equal(new CellSize(4, 7), changes[section].Size);
        Assert.Equal(new CellPosition(0, 7), changes[below].At);
    }

    [Fact]
    public void ParentOf_FindsTheLayoutControlOfAChild()
    {
        var knob = Knob("a", 0, 0);
        var section = new LayoutControl { Kind = LayoutControlKind.Section, Children = [knob] };
        var root = Grid(section);

        Assert.Same(section, GridLayout.ParentOf(root, knob));
        Assert.Same(root, GridLayout.ParentOf(root, section));
        Assert.Null(GridLayout.ParentOf(root, root));
    }

    [Fact]
    public void Place_OnAPageOfSelectorTabs_GrowsThePageAndTheTabs()
    {
        var knob = Knob("a", 0, 0);
        var page = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "Saw", Size = new(4, 4), Children = [knob] };
        var tabs = new SelectorTabsControl { At = new(0, 0), Size = new(4, 6), Children = [page] };
        var root = new LayoutControl { Kind = LayoutControlKind.Grid, Size = new(20, 20), Children = [tabs] };

        var changes = GridLayout.Place(root, knob, new CellPosition(4, 0), new CellSize(2, 2));

        Assert.Equal(new CellSize(6, 4), changes[page].Size);
        // the page below the two rows of the headers
        Assert.Equal(new CellSize(6, 6), changes[tabs].Size);
    }

    [Fact]
    public void Place_ResizedSelectorTabs_SizeTheirPagesToTheSpaceBelowTheHeaders()
    {
        var saw = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "Saw", Size = new(6, 2) };
        var pulse = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "Pulse", Size = new(6, 2) };
        var tabs = new SelectorTabsControl { At = new(0, 0), Size = new(6, 4), Children = [saw, pulse] };
        var root = new LayoutControl { Kind = LayoutControlKind.Grid, Size = new(30, 30), Children = [tabs] };

        var changes = GridLayout.Place(root, tabs, new CellPosition(0, 0), new CellSize(14, 12));

        Assert.Equal(new CellSize(14, 10), changes[saw].Size);
        Assert.Equal(new CellSize(14, 10), changes[pulse].Size);
    }

    [Fact]
    public void Place_ShrunkTabs_KeepTheirPagesAsLargeAsTheirControls_AndGrowAroundThem()
    {
        var page = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "One", Size = new(10, 8), Children = [Knob("a", 6, 4)] };
        var tabs = new LayoutControl { Kind = LayoutControlKind.Tabs, At = new(0, 0), Size = new(10, 10), Children = [page] };
        var root = new LayoutControl { Kind = LayoutControlKind.Grid, Size = new(30, 30), Children = [tabs] };

        var changes = GridLayout.Place(root, tabs, new CellPosition(0, 0), new CellSize(4, 4));

        // the knob at 6,4 of 2,2 needs 8 columns and 6 rows
        Assert.Equal(new CellSize(8, 6), changes[page].Size);
        Assert.Equal(new CellSize(8, 8), changes[tabs].Size);
    }

    [Fact]
    public void Complete_GivesThePagesOfTabsTheSpaceBelowTheHeaders_AndTheTabsRoomForTheirContent()
    {
        var small = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "Saw", Size = new(6, 2) };
        var full = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "Pulse", Size = new(6, 2), Children = [Knob("a", 14, 0)] };
        var tabs = new SelectorTabsControl { At = new(0, 0), Size = new(12, 10), Children = [small, full] };

        var given = GridLayout.Complete(new LayoutControl { Kind = LayoutControlKind.Grid, Size = new(30, 30), Children = [tabs] });

        // the knob at 14,0 of 2,2 needs 16 columns: the tabs grow, the pages fill them below the headers
        Assert.Equal(new CellSize(16, 10), tabs.Size);
        Assert.Equal(new CellSize(16, 8), small.Size);
        Assert.Equal(new CellSize(16, 8), full.Size);
        Assert.Equal(3, given);
    }

    [Fact]
    public void MinimumSize_OfTabs_IsTheLargestPageContent_BelowTheHeaders()
    {
        var small = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "Saw", Size = new(20, 20), Children = [Knob("a", 0, 0)] };
        var large = new LayoutControl { Kind = LayoutControlKind.Grid, Label = "Pulse", Size = new(20, 20), Children = [Knob("b", 6, 1)] };
        var tabs = new SelectorTabsControl { Size = new(20, 22), Children = [small, large] };

        // the knob at 6,1 of 2,2 needs 8 columns and 3 rows; the headers take two rows
        Assert.Equal(new CellSize(8, 5), GridLayout.MinimumSize(tabs));
        Assert.Equal(new CellSize(8, 3), GridLayout.MinimumSize(large));
        Assert.Equal(new CellSize(1, 1), GridLayout.MinimumSize(Knob("c", 0, 0)));
    }

    [Fact]
    public void Children_OfAControlThatHoldsNone_AreEmptyAndCannotBeSet()
    {
        var knob = Knob("a", 0, 0);

        Assert.Empty(knob.Children);
        Assert.Throws<NotSupportedException>(() => knob.Children = [Knob("b", 0, 0)]);
        Assert.Null(GridLayout.ParentOf(knob, knob));
    }
}
