namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// The layout of a dashboard in cells. The dashboard's root and every grid and section are grids: each control in them
/// has a position (column, row) and a size in cells. Rows, columns, tabs and selector tabs arrange their children
/// themselves: those only have a size. <see cref="Complete"/> gives the controls of a file that lack them a size and position;
/// <see cref="Place"/> moves or sizes a control and pushes the controls it would overlap down, growing the grids that
/// no longer fit their children.
/// </summary>
public static class GridLayout
{
    /// <summary>The cells around the grid of a section: its frame and title (left, top, right, bottom).</summary>
    public static CellInset SectionInset { get; } = new(1, 2, 1, 1);

    /// <summary>The width of a grid without a size, to place the controls without a position in.</summary>
    private const int DefaultColumns = 48;

    /// <summary>The rows the headers of tabs and selector tabs take above their pages.</summary>
    private const int TabHeaderRows = 2;

    /// <summary>True when the layout control places its children at a position (a grid or section).</summary>
    public static bool IsGrid(LayoutControl layout) => layout.Kind is LayoutControlKind.Grid or LayoutControlKind.Section;

    /// <summary>The cells around the children of a grid: the inset of a section, none for a grid.</summary>
    public static CellInset InsetOf(LayoutControl layout)
        => layout.Kind == LayoutControlKind.Section ? SectionInset : default;

    /// <summary>The size a new control of a kind gets: one in which its face and chrome fit unscaled.</summary>
    public static CellSize DefaultSize(ParameterControlKind kind)
        => kind switch
        {
            ParameterControlKind.Knob => new(5, 7),
            ParameterControlKind.Slider => new(4, 9),
            ParameterControlKind.Switch => new(4, 7),
            ParameterControlKind.Selector => new(6, 4),
            ParameterControlKind.SelectorTabs => DefaultSize(LayoutControlKind.Tabs),
            ParameterControlKind.NumberBox => new(6, 4),
            ParameterControlKind.Text => new(9, 4),
            ParameterControlKind.XyPad => new(7, 7),
            ParameterControlKind.Envelope => new(10, 6),
            _ => new(4, 4),
        };

    /// <summary>The size a new, empty layout control of a kind gets: room for a few controls, or its own content.</summary>
    public static CellSize DefaultSize(LayoutControlKind kind)
        => kind switch
        {
            LayoutControlKind.Grid => new(12, 9),
            LayoutControlKind.Section => new(12, 10),
            LayoutControlKind.Row => new(12, 7),
            LayoutControlKind.Column => new(5, 14),
            LayoutControlKind.Tabs => new(12, 10),
            LayoutControlKind.Separator => new(12, 1),
            LayoutControlKind.Label => new(6, 2),
            _ => new(1, 1),
        };

    /// <summary>The size of a page of tabs or selector tabs of a size: below the headers.</summary>
    public static CellSize PageSize(CellSize size)
        => new(size.Columns, Math.Max(1, size.Rows - TabHeaderRows));

    /// <summary>
    /// True when cells are wider than tall: a control with a direction (a slider, a separator) then runs horizontally,
    /// otherwise vertically.
    /// </summary>
    public static bool IsWide(CellSize size) => size.Columns > size.Rows;

    /// <summary>The control that holds the control; null for the root (or a control not in it).</summary>
    public static DashboardControl? ParentOf(DashboardControl root, DashboardControl control)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var child in root.Children)
        {
            if (child == control)
                return root;
            if (ParentOf(child, control) is { } parent)
                return parent;
        }
        return null;
    }

    /// <summary>
    /// Gives every control without a size its default size (a layout control: the size of its children), and every
    /// child of a grid without a position one below the children that have one. The pages of tabs and selector tabs get
    /// the space below their headers (not less than their controls need; the tabs grow when they need more), as
    /// <see cref="Place"/> keeps them. Returns the number of sizes and positions given.
    /// </summary>
    public static int Complete(DashboardControl root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var given = root.Children.Sum(Complete);
        if (root is LayoutControl layout && IsGrid(layout))
            given += PlaceUnplaced(layout);
        if (root.Size is null)
        {
            root.Size = root switch
            {
                LayoutControl content => ContentSize(content),
                ParameterControl parameter => DefaultSize(parameter.Kind),
            };
            given++;
        }
        if (HasPages(root))
            given += CompletePages(root);
        return given;
    }

    // the tabs fit the largest content of their pages, the pages take the space below the headers
    private static int CompletePages(DashboardControl holder)
    {
        var given = 0;
        var size = holder.Size!.Value;
        var minimum = MinimumSize(holder);
        if (minimum.Columns > size.Columns || minimum.Rows > size.Rows)
        {
            size = new CellSize(Math.Max(size.Columns, minimum.Columns), Math.Max(size.Rows, minimum.Rows));
            holder.Size = size;
            given++;
        }
        var changes = new Dictionary<DashboardControl, Placement>();
        SizePages(holder, size, changes);
        foreach (var (page, placement) in changes)
        {
            page.Size = placement.Size;
            given++;
        }
        return given;
    }

    /// <summary>
    /// Moves and sizes a control in its grid (<paramref name="root"/> is the dashboard's root): the controls it would
    /// overlap are pushed down, and a grid that no longer fits its children grows (pushing in its own grid in turn).
    /// Returns the new position and size of every control that changes; nothing is changed yet.
    /// </summary>
    public static IReadOnlyDictionary<DashboardControl, Placement> Place(LayoutControl root, DashboardControl control, CellPosition at, CellSize size)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(control);
        var changes = new Dictionary<DashboardControl, Placement>();
        Place(root, control, new Placement(new CellPosition(Math.Max(0, at.Column), Math.Max(0, at.Row)), size), changes);
        return changes;
    }

    private static void Place(LayoutControl root, DashboardControl control, Placement placement, Dictionary<DashboardControl, Placement> changes)
    {
        changes[control] = placement;
        if (HasPages(control) && SizePages(control, placement.Size, changes))
            Fit(root, control, changes);
        if (ParentOf(root, control) is not { } parent)
            return;
        if (parent is LayoutControl layout && IsGrid(layout))
            PushAside(layout, control, changes);
        Fit(root, parent, changes);
    }

    /// <summary>
    /// The smallest size a control can be given: what its controls need (a grid, section, row or column), the largest
    /// content of its pages below the headers (tabs and selector tabs), one cell for any other control.
    /// </summary>
    public static CellSize MinimumSize(DashboardControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (HasPages(control))
        {
            var contents = control.Children
                .Select(page => new Placement(default, page is LayoutControl layout ? ContentSize(layout) : page.Size ?? new CellSize(1, 1)))
                .ToList();
            return contents.Count == 0 ? new CellSize(1, TabHeaderRows + 1) : PagesSize(contents);
        }
        return control is LayoutControl other ? ContentSize(other) : new CellSize(1, 1);
    }

    /// <summary>True when the control shows its children as pages, one at a time below headers: tabs and selector tabs.</summary>
    public static bool HasPages(DashboardControl control)
        => control is LayoutControl { Kind: LayoutControlKind.Tabs } or SelectorTabsControl;

    // the pages take the space below the headers, but not less than their controls need; true when a page needs more
    private static bool SizePages(DashboardControl holder, CellSize size, Dictionary<DashboardControl, Placement> changes)
    {
        var space = PageSize(size);
        var needsMore = false;
        foreach (var page in holder.Children)
        {
            var needed = page is LayoutControl layout ? ContentSize(layout, changes) : PlacementOf(page, changes).Size;
            var pageSize = new CellSize(Math.Max(space.Columns, needed.Columns), Math.Max(space.Rows, needed.Rows));
            needsMore |= pageSize != space;
            if (pageSize != PlacementOf(page, changes).Size)
                changes[page] = PlacementOf(page, changes) with { Size = pageSize };
        }
        return needsMore;
    }

    // the siblings the control overlaps move down below it, and so on for the siblings those then overlap
    private static void PushAside(LayoutControl grid, DashboardControl fixedControl, Dictionary<DashboardControl, Placement> changes)
    {
        var pushers = new Queue<DashboardControl>([fixedControl]);
        while (pushers.TryDequeue(out var pusher))
        {
            var area = PlacementOf(pusher, changes);
            var overlapped = grid.Children
                .Where(child => child != pusher && child != fixedControl && Overlaps(area, PlacementOf(child, changes)))
                .OrderBy(child => PlacementOf(child, changes).At.Row)
                .ThenBy(child => PlacementOf(child, changes).At.Column);
            foreach (var child in overlapped)
            {
                var placement = PlacementOf(child, changes);
                changes[child] = placement with { At = placement.At with { Row = area.At.Row + area.Size.Rows } };
                pushers.Enqueue(child);
            }
        }
    }

    // a control grows to the size of its children when they no longer fit
    private static void Fit(LayoutControl root, DashboardControl holder, Dictionary<DashboardControl, Placement> changes)
    {
        var current = PlacementOf(holder, changes);
        var needed = ContentSize(holder, changes);
        if (needed.Columns <= current.Size.Columns && needed.Rows <= current.Size.Rows)
            return;
        var size = new CellSize(Math.Max(needed.Columns, current.Size.Columns), Math.Max(needed.Rows, current.Size.Rows));
        Place(root, holder, current with { Size = size }, changes);
    }

    // the children without a position, side by side below those with one, on lines as wide as the grid
    private static int PlaceUnplaced(LayoutControl grid)
    {
        var unplaced = grid.Children.Where(child => child.At is null).ToList();
        if (unplaced.Count == 0)
            return 0;
        var inset = InsetOf(grid);
        var width = Math.Max(grid.Size is { } size ? size.Columns - inset.Left - inset.Right : DefaultColumns,
            unplaced.Max(child => child.Size!.Value.Columns));
        var top = grid.Children.Where(child => child.At is not null).Select(child => child.At!.Value.Row + child.Size!.Value.Rows).DefaultIfEmpty(0).Max();
        int column = 0, row = top, lineHeight = 0;
        foreach (var child in unplaced)
        {
            var childSize = child.Size!.Value;
            if (column > 0 && column + childSize.Columns > width)
            {
                column = 0;
                row += lineHeight;
                lineHeight = 0;
            }
            child.At = new CellPosition(column, row);
            column += childSize.Columns;
            lineHeight = Math.Max(lineHeight, childSize.Rows);
        }
        return unplaced.Count;
    }

    private static CellSize ContentSize(LayoutControl layout)
        => ContentSize(layout, new Dictionary<DashboardControl, Placement>());

    // the size a control needs for its children (with their placements as changed)
    private static CellSize ContentSize(DashboardControl holder, Dictionary<DashboardControl, Placement> changes)
    {
        var children = holder.Children.Select(child => PlacementOf(child, changes)).ToList();
        if (holder is not LayoutControl layout)
            return children.Count == 0 ? new CellSize(1, 1) : PagesSize(children);
        if (children.Count == 0)
            return new CellSize(Math.Max(1, InsetOf(layout).Left + InsetOf(layout).Right), Math.Max(1, InsetOf(layout).Top + InsetOf(layout).Bottom));

        var gaps = layout.Gap * (children.Count - 1);
        switch (layout.Kind)
        {
            case LayoutControlKind.Row:
                return new CellSize(children.Sum(child => child.Size.Columns) + gaps, children.Max(child => child.Size.Rows));
            case LayoutControlKind.Column:
                return new CellSize(children.Max(child => child.Size.Columns), children.Sum(child => child.Size.Rows) + gaps);
            case LayoutControlKind.Tabs:
                return PagesSize(children);
            default:
                var inset = InsetOf(layout);
                return new CellSize(
                    children.Max(child => child.At.Column + child.Size.Columns) + inset.Left + inset.Right,
                    children.Max(child => child.At.Row + child.Size.Rows) + inset.Top + inset.Bottom);
        }
    }

    // pages shown one at a time below their headers
    private static CellSize PagesSize(List<Placement> pages)
        => new(pages.Max(page => page.Size.Columns), pages.Max(page => page.Size.Rows) + TabHeaderRows);

    private static Placement PlacementOf(DashboardControl control, Dictionary<DashboardControl, Placement> changes)
        => changes.TryGetValue(control, out var placement)
            ? placement
            : new Placement(control.At ?? default, control.Size ?? new CellSize(1, 1));

    private static bool Overlaps(Placement first, Placement second)
        => first.At.Column < second.At.Column + second.Size.Columns && second.At.Column < first.At.Column + first.Size.Columns
            && first.At.Row < second.At.Row + second.Size.Rows && second.At.Row < first.At.Row + first.Size.Rows;
}

/// <summary>Where a control is in its grid and how big it is, in cells.</summary>
public readonly record struct Placement(CellPosition At, CellSize Size);

/// <summary>A number of cells on each side.</summary>
public readonly record struct CellInset(int Left, int Top, int Right, int Bottom);
