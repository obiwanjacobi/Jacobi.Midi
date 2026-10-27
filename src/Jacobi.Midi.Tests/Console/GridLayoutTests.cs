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
}
