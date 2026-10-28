using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Jacobi.Midi.Console.Chrome;
using Jacobi.Midi.Console.Controls;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Faces;
using Jacobi.Midi.Console.Looks;

namespace Jacobi.Midi.Console.Dashboards;

/// <summary>
/// Builds the Avalonia controls of an open dashboard: layout controls become panels (row, column, grid, section,
/// tabs, spacer, separator, label), parameter controls the controls of their kind bound to their parameter. Kinds
/// that are not built yet are shown as a placeholder, not left out. Everything is laid out in cells of
/// <see cref="Face.CellPixels"/> (see <see cref="GridLayout"/>): every control has the size of its cells, a parameter
/// control is fitted to them (<see cref="CellFit"/>); in a grid or section the children are at their cell positions, a section's frame
/// takes the cells of its inset. The dashboard as a whole is scaled to its cell size, so controls, fonts, gaps and
/// grids scale together.
/// <para>
/// The colour cascade, per <see cref="ColorArea"/> (accent, plate, panel): a control has (1) its own colour, else (2)
/// the colour of the nearest layout control around it that has one, else (3) the colour of its device (a layout
/// control's device, a parameter control's first slot's device), else (4) the look's. A control whose colour of an
/// area differs from the one around it sets the resources of that area (<see cref="PaletteBrushes"/>) for itself and
/// the controls inside it.
/// </para>
/// <para>
/// In Edit mode (<paramref name="isEditing"/>) the values are locked (<see cref="DashboardEditing"/>) and the dashboard
/// takes the presses before its controls do: a click selects the control under the pointer (shown by
/// <see cref="ShowSelection"/> with the cells of its grid), a right-click also opens the <see cref="ColorChooser"/> for
/// the area that was clicked: the face or an element of the class <c>accentArea</c> is the accent, the rest of a
/// parameter control its plate, the rest of a layout control its panel. Dragging a control in a grid moves it,
/// dragging the handle at its bottom right sizes it, by whole cells; the control follows the pointer and is placed
/// (<see cref="IDashboardEditor.Place"/>) when it is let go. A control let go over another grid, section, row, column
/// or tabs moves into it (<see cref="IDashboardEditor.Move"/>); the cells of the grid it goes into are shown while it is
/// dragged. A <see cref="DragItem"/> dropped on the dashboard goes into the layout control under the pointer, at the cell
/// of the pointer (<see cref="IDashboardEditor.Drop"/>); a field dropped on a parameter control binds it
/// (<see cref="IDashboardEditor.Bind"/>). A tab header shows its page in Edit mode too, and selects it; the page a
/// tabs shows is kept when the dashboard is built again, and selecting a control shows the pages it is on. A label or
/// separator has only an accent: drawn in the look's text or outline colour until it is given one. In Play mode a
/// right-click does nothing.
/// </para>
/// </summary>
internal sealed class DashboardBuilder(DashboardSession session, IDashboardEditor editor, bool isEditing)
{
    private const double Cell = Face.CellPixels;

    /// <summary>The size of the sizing handle at the bottom right of the selection.</summary>
    private const double HandleSize = 10;

    /// <summary>The class of the elements of a look's chrome that are drawn in the accent.</summary>
    private const string AccentAreaClass = "accentArea";

    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF));

    // the built controls with what they show and their colours, both ways
    private readonly Dictionary<Control, (DashboardControl Control, ColorScope Scope)> _built = [];
    private readonly Dictionary<DashboardControl, Control> _controls = [];
    // the cells of the grids and sections: where their children are placed
    private readonly Dictionary<LayoutControl, Canvas> _grids = [];
    // the tab controls of the tabs, and the page each shows (kept while the dashboard is built again)
    private readonly Dictionary<LayoutControl, TabControl> _tabs = [];
    private static readonly ConditionalWeakTable<LayoutControl, StrongBox<int>> ShownPages = [];
    private Control? _root;
    private Control? _selected;
    private Canvas? _shownGrid;
    private Canvas? _targetGrid;
    private Drag? _drag;

    public Control Build()
    {
        var scale = session.Dashboard.CellSize / Face.CellPixels;
        var root = new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(scale, scale),
            Child = Build(session.Dashboard.Layout, ColorScope.Look),
        };
        DashboardEditing.SetIsEditing(root, isEditing);
        if (isEditing)
        {
            // it takes the focus on a press: a text being typed in a panel is taken first, and the keys (Delete) are for it
            root.Focusable = true;
            root.AddHandler(InputElement.PointerPressedEvent, OnEditPressed, RoutingStrategies.Tunnel);
            root.AddHandler(InputElement.PointerMovedEvent, OnEditMoved, RoutingStrategies.Tunnel);
            root.AddHandler(InputElement.PointerReleasedEvent, OnEditReleased, RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(root, true);
            DragDrop.AddDragOverHandler(root, OnDragOver);
            DragDrop.AddDragLeaveHandler(root, (_, _) => ShowTarget(null));
            DragDrop.AddDropHandler(root, OnDrop);
        }
        _root = root;
        return root;
    }

    /// <summary>
    /// Outlines the built control of a dashboard control, with the handle to size it, and shows the cells of the grid
    /// it is in (of the root: its own); null shows nothing.
    /// </summary>
    public void ShowSelection(DashboardControl? control)
    {
        if (_selected is not null)
            AdornerLayer.SetAdorner(_selected, null);
        if (_shownGrid is not null)
            AdornerLayer.SetAdorner(_shownGrid, null);
        _selected = control is not null ? _controls.GetValueOrDefault(control) : null;
        _shownGrid = null;
        if (control is null || _selected is null)
            return;
        ShowPagesOf(control);

        AdornerLayer.SetAdorner(_selected, new Panel
        {
            IsHitTestVisible = false,
            Children =
            {
                new Rectangle { Stroke = SelectionBrush, StrokeThickness = 2, StrokeDashArray = [3, 2] },
                new Rectangle
                {
                    Width = HandleSize, Height = HandleSize, Fill = SelectionBrush,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                },
            },
        });
        var grid = GridLayout.ParentOf(session.Dashboard.Layout, control) ?? control as LayoutControl;
        // a visual has one adorner: the root's own cells are not shown when it is selected itself
        if (grid is not null && _grids.GetValueOrDefault(grid) is { } canvas && canvas != _selected)
        {
            _shownGrid = canvas;
            AdornerLayer.SetAdorner(canvas, new CellGrid { CellSize = Cell, IsHitTestVisible = false });
        }
    }

    // a control on a page of tabs (of tabs on a page, ...): those pages are shown
    private void ShowPagesOf(DashboardControl control)
    {
        var root = session.Dashboard.Layout;
        for (var (child, parent) = (control, GridLayout.ParentOf(root, control)); parent is not null; (child, parent) = (parent, GridLayout.ParentOf(root, parent)))
        {
            if (_tabs.TryGetValue(parent, out var tabs))
                tabs.SelectedIndex = parent.Children.ToList().IndexOf(child);
        }
    }

    /// <summary>Opens the colour chooser of an area of a control at a placement target (e.g. a button in a panel).</summary>
    public void OpenColorChooser(DashboardControl control, ColorArea area, Control placementTarget)
    {
        if (_controls.GetValueOrDefault(control) is not { } built)
            return;
        var scope = _built[built].Scope;
        ColorChooser.Create(editor, control, Describe(control, built), area, CurrentColor(scope, area), scope.Device).ShowAt(placementTarget);
    }

    // Edit mode: the press selects the innermost built control under the pointer; a right press opens its colours, a
    // left press on the handle starts sizing it, elsewhere moving it (in a grid)
    private void OnEditPressed(object? sender, PointerPressedEventArgs e)
    {
        // a tab header shows its page, as in Play mode (the tabs take the press), and selects the page
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<TabItem>().FirstOrDefault() is { Tag: DashboardControl page })
        {
            editor.Select(page);
            return;
        }
        e.Handled = true;
        var built = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(_built.ContainsKey);
        // the dashboard takes the focus, for the keys (Delete); a text being typed in a panel is taken first, for the
        // control it is of (which builds the dashboard again)
        if (_root is { } root && !root.IsFocused)
        {
            root.Focus();
            if (TopLevel.GetTopLevel(root) is null)
            {
                editor.Select(built is null ? null : _built[built].Control);
                return;
            }
        }
        if (built is null)
        {
            editor.Select(null);
            return;
        }
        var (control, scope) = _built[built];
        editor.Select(control);
        var point = e.GetCurrentPoint(built);
        if (point.Properties.IsRightButtonPressed)
        {
            var area = AreaAt(control, built, e.Source);
            ColorChooser.Create(editor, control, Describe(control, built), area, CurrentColor(scope, area), scope.Device).ShowAt(built);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed || built.GetVisualParent() is not Visual reference || control.Size is not { } size)
            return;

        var isSizing = point.Position.X >= built.Bounds.Width - HandleSize * 1.5 && point.Position.Y >= built.Bounds.Height - HandleSize * 1.5;
        var parent = GridLayout.ParentOf(session.Dashboard.Layout, control);
        // the root is not moved; a control outside a grid is only moved into another layout control
        if (!isSizing && parent is null)
            return;
        var placement = new Placement(control.At ?? default, size);
        _drag = new Drag(control, built, reference, e.GetPosition(reference), point.Position, isSizing, parent is not null && GridLayout.IsGrid(parent), placement)
        {
            Current = placement,
        };
        // the layout control under a moved control is found through it
        built.IsHitTestVisible = isSizing;
        e.Pointer.Capture(_root);
    }

    // dragging: the control follows the pointer by whole cells (moved or sized), nothing is changed yet
    private void OnEditMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } drag)
            return;
        var delta = e.GetPosition(drag.Reference) - drag.Start;
        var columns = (int)Math.Round(delta.X / Cell);
        var rows = (int)Math.Round(delta.Y / Cell);
        var original = drag.Original;
        drag.Current = drag.IsSizing
            ? original with { Size = new CellSize(Math.Max(1, original.Size.Columns + columns), Math.Max(1, original.Size.Rows + rows)) }
            : original with { At = new CellPosition(Math.Max(0, original.At.Column + columns), Math.Max(0, original.At.Row + rows)) };
        drag.Built.Width = drag.Current.Size.Columns * Cell;
        drag.Built.Height = drag.Current.Size.Rows * Cell;
        if (!drag.IsSizing)
        {
            if (drag.InGrid)
            {
                Canvas.SetLeft(drag.Built, drag.Current.At.Column * Cell);
                Canvas.SetTop(drag.Built, drag.Current.At.Row * Cell);
            }
            ShowTarget(MoveTargetAt(drag, e) is { } target ? target.Container : null);
        }
        e.Handled = true;
    }

    // let go: the control is placed where it was dragged to (the dashboard is built again)
    private void OnEditReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is not { } drag)
            return;
        _drag = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        drag.Built.IsHitTestVisible = true;
        ShowTarget(null);
        if (!drag.IsSizing && MoveTargetAt(drag, e) is { } target)
        {
            // the control's top left where it was let go, in the cells of the other grid
            var at = _grids.GetValueOrDefault(target.Container) is { } canvas ? CellAt(e.GetPosition(canvas) - drag.Grab) : default;
            editor.Move(drag.Control, target.Container, at);
        }
        else if (drag.Current != drag.Original)
        {
            editor.Place(drag.Control, drag.Current.At, drag.Current.Size);
        }
    }

    // a moved control over another layout control than its own goes into it
    private DropTarget? MoveTargetAt(Drag drag, PointerEventArgs e)
    {
        if (_root?.InputHitTest(e.GetPosition(_root)) is not Visual hit || TargetAt(hit, drag.Control) is not { } target)
            return null;
        return target.Container == GridLayout.ParentOf(session.Dashboard.Layout, drag.Control) ? null : target;
    }

    // a dragged item: the cells of the grid it goes into are shown
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.DataTransfer.TryGetValue(DragItem.Format) is not { } item || TargetAt(e.Source as Visual, null) is not { } target || IsRefused(item, target))
        {
            e.DragEffects = DragDropEffects.None;
            ShowTarget(null);
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        ShowTarget(IsBinding(item, target) ? null : target.Container);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        ShowTarget(null);
        if (e.DataTransfer.TryGetValue(DragItem.Format) is not { } item || TargetAt(e.Source as Visual, null) is not { } target || IsRefused(item, target))
            return;
        if (IsBinding(item, target))
            editor.Bind((ParameterDragItem)item, target.Control!);
        else
            editor.Drop(item, target.Container, _grids.GetValueOrDefault(target.Container) is { } canvas ? CellAt(e.GetPosition(canvas)) : default);
    }

    // a field dropped on a parameter control binds it
    private static bool IsBinding(DragItem item, DropTarget target)
        => item is ParameterDragItem { Source.Node.Kind: ParameterNodeKind.Field } && target.Control is not null;

    // a field the control under the pointer cannot show (a number on a switch) is not dropped on it
    private static bool IsRefused(DragItem item, DropTarget target)
        => IsBinding(item, target) && ((ParameterDragItem)item).Source.Node.Info is { } info && !ControlCatalog.Accepts(target.Control!.Kind, info);

    // the innermost built control at the visual (not in the moved control): a layout control is the container, a
    // parameter control is in the container of its parent
    private DropTarget? TargetAt(Visual? visual, DashboardControl? moved)
    {
        for (; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is not Control built || !_built.TryGetValue(built, out var entry) || (moved is not null && ControlMoves.Contains(moved, entry.Control)))
                continue;
            return entry.Control switch
            {
                LayoutControl layout => new DropTarget(layout, null),
                ParameterControl parameter when GridLayout.ParentOf(session.Dashboard.Layout, parameter) is { } parent => new DropTarget(parent, parameter),
                _ => null,
            };
        }
        return null;
    }

    private static CellPosition CellAt(Point point)
        => new(Math.Max(0, (int)Math.Floor(point.X / Cell)), Math.Max(0, (int)Math.Floor(point.Y / Cell)));

    // the cells of the grid a drop or move goes into; a visual has one adorner: the grid of the selection shows its own
    private void ShowTarget(LayoutControl? container)
    {
        var canvas = container is null ? null : _grids.GetValueOrDefault(container);
        if (canvas == _targetGrid)
            return;
        if (_targetGrid is not null && _targetGrid != _shownGrid)
            AdornerLayer.SetAdorner(_targetGrid, null);
        _targetGrid = canvas;
        if (canvas is not null && canvas != _shownGrid && canvas != _selected)
            AdornerLayer.SetAdorner(canvas, new CellGrid { CellSize = Cell, IsHitTestVisible = false });
    }

    private static RgbColor CurrentColor(ColorScope scope, ColorArea area)
        => scope[area].Color ?? PaletteBrushes.LookColorOf(area);

    private Control Build(DashboardControl control, ColorScope around)
    {
        var scope = ScopeOf(control, around);
        var built = control switch
        {
            LayoutControl layout => BuildLayout(layout, scope),
            ParameterControl parameter => BuildParameter(parameter),
            _ => Placeholder(control, "control", null),
        };
        // a parameter control (chrome and face) is fitted to its cells
        if (control is ParameterControl)
            built = new CellFit { Child = built };

        if (control.Size is { } size)
        {
            built.Width = size.Columns * Cell;
            built.Height = size.Rows * Cell;
        }
        if (control.At is { } at)
        {
            Canvas.SetLeft(built, at.Column * Cell);
            Canvas.SetTop(built, at.Row * Cell);
        }

        foreach (var area in AreaColors.Areas)
        {
            if (scope[area].Color != around[area].Color)
                PaletteBrushes.Apply(built.Resources, area, scope[area].Color);
        }
        // controls on the panel have their labels on it, until their plate has a colour
        if (editor.Look.ControlsOnPanel && scope[ColorArea.Plate].Color is null
            && (scope[ColorArea.Panel].Color != around[ColorArea.Panel].Color || around[ColorArea.Plate].Color is not null))
        {
            PaletteBrushes.ApplyPlateText(built.Resources, scope[ColorArea.Panel].Color);
        }

        _built[built] = (control, scope);
        _controls[control] = built;
        return built;
    }

    // the colours of a control and the device the controls inside it are of: see the cascade above
    private ColorScope ScopeOf(DashboardControl control, ColorScope around)
    {
        var device = control switch
        {
            ParameterControl parameter => session.SlotDeviceOf(parameter) ?? control.Device ?? around.Device,
            _ => control.Device ?? around.Device,
        };
        var ofDevice = device is null ? AreaColors.None : session.ColorsOf(device);
        var sameDevice = String.Equals(device, around.Device, StringComparison.OrdinalIgnoreCase);
        return new ColorScope(device, [.. AreaColors.Areas.Select(area =>
        {
            if (RgbColor.TryParse(control.Colors[area], out var own))
                return new AreaScope(own, IsGiven: true);
            if (around[area].IsGiven || sameDevice)
                return around[area];
            return new AreaScope(RgbColor.TryParse(ofDevice[area], out var color) ? color : null, IsGiven: false);
        })]);
    }

    // the area under the pointer: the face or an element in the accent, else a parameter control's plate or a layout
    // control's panel; a label or separator has only an accent
    private static ColorArea AreaAt(DashboardControl control, Control built, object? source)
    {
        if (control is LayoutControl { Kind: LayoutControlKind.Label or LayoutControlKind.Separator })
            return ColorArea.Accent;
        for (var visual = source as Visual; visual is not null && visual != built; visual = visual.GetVisualParent())
        {
            if (visual is Face || (visual is StyledElement element && element.Classes.Contains(AccentAreaClass)))
                return ColorArea.Accent;
        }
        return control is ParameterControl ? ColorArea.Plate : ColorArea.Panel;
    }

    private static string Describe(DashboardControl control, Control built)
        => control.Label ?? (built as ParameterControlBase)?.Label ?? control switch
        {
            ParameterControl parameter => parameter.Kind.ToString(),
            LayoutControl layout => layout.Kind.ToString(),
            _ => "control",
        };

    private Control BuildLayout(LayoutControl layout, ColorScope scope)
    {
        var gap = layout.Gap * Cell;
        switch (layout.Kind)
        {
            case LayoutControlKind.Row:
            case LayoutControlKind.Column:
                return Arrange(layout, layout.Kind == LayoutControlKind.Row ? Orientation.Horizontal : Orientation.Vertical, gap, scope);

            case LayoutControlKind.Section:
                var inset = GridLayout.InsetOf(layout);
                return new SectionFrame
                {
                    Header = layout.Label,
                    Padding = new Thickness(inset.Left * Cell, inset.Top * Cell, inset.Right * Cell, inset.Bottom * Cell),
                    Content = BuildGrid(layout, scope),
                };

            case LayoutControlKind.Grid:
                return BuildGrid(layout, scope);

            case LayoutControlKind.Tabs:
                // each child is a page; the page shown stays shown when the dashboard is built again
                var pages = layout.Children.Select((child, index) => new TabItem
                {
                    Header = child.Label ?? $"Page {index + 1}",
                    Tag = child,
                    Content = Build(child, scope),
                }).ToList();
                var tabs = new TabControl
                {
                    ItemsSource = pages,
                    SelectedIndex = Math.Min(ShownPages.TryGetValue(layout, out var shown) ? shown.Value : 0, pages.Count - 1),
                    Background = isEditing ? Brushes.Transparent : null,
                };
                tabs.SelectionChanged += (_, e) =>
                {
                    // not the change of tabs on one of its pages
                    if (e.Source == tabs && tabs.SelectedIndex >= 0)
                        ShownPages.AddOrUpdate(layout, new StrongBox<int>(tabs.SelectedIndex));
                };
                _tabs[layout] = tabs;
                return tabs;

            case LayoutControlKind.Spacer:
                return new Border { Width = Cell, Height = Cell };

            case LayoutControlKind.Separator:
                // a line along the long side of its cells, in the accent when it is given one (else the look's outline)
                var horizontal = layout.Size is not { } extent || GridLayout.IsWide(extent);
                var line = new Border
                {
                    Height = horizontal ? 2 : Double.NaN,
                    Width = horizontal ? Double.NaN : 2,
                    HorizontalAlignment = horizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
                    VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Stretch,
                };
                line.Bind(Border.BackgroundProperty, line.GetResourceObservable(scope[ColorArea.Accent].IsGiven ? "ConsoleAccentBrush" : "ConsoleOutlineBrush"));
                return new Border { Background = isEditing ? Brushes.Transparent : null, Child = line };

            case LayoutControlKind.Label:
                // the look's text colour, the accent when it is given one
                var label = new TextBlock { Text = layout.Label, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(scope[ColorArea.Accent].IsGiven ? "ConsoleAccentBrush" : "ConsoleTextBrush"));
                label.Bind(TextBlock.FontFamilyProperty, label.GetResourceObservable("ConsoleLabelFont"));
                return label;

            default:
                return Placeholder(layout, layout.Kind.ToString(), null);
        }
    }

    // a row or column: a stack, a wrap panel, or a grid when children share the remaining space (grow)
    private Panel Arrange(LayoutControl layout, Orientation orientation, double gap, ColorScope scope)
    {
        var children = layout.Children.Select(child => Build(child, scope)).ToList();
        // in Edit mode the space between the children is the row or column, to drop on
        var background = isEditing ? Brushes.Transparent : null;
        if (layout.Wrap)
        {
            var wrap = new WrapPanel { Orientation = orientation, ItemSpacing = gap, LineSpacing = gap, Background = background };
            wrap.Children.AddRange(children);
            return wrap;
        }
        if (layout.Children.All(child => child.Grow is null or 0))
        {
            var stack = new StackPanel { Orientation = orientation, Spacing = gap, Background = background };
            stack.Children.AddRange(children);
            return stack;
        }

        var grid = new Grid { Background = background };
        for (var index = 0; index < children.Count; index++)
        {
            var grow = layout.Children[index].Grow ?? 0;
            var length = grow > 0 ? new GridLength(grow, GridUnitType.Star) : GridLength.Auto;
            if (orientation == Orientation.Horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(length));
                Grid.SetColumn(children[index], index);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(length));
                Grid.SetRow(children[index], index);
            }
        }
        if (orientation == Orientation.Horizontal)
            grid.ColumnSpacing = gap;
        else
            grid.RowSpacing = gap;
        grid.Children.AddRange(children);
        return grid;
    }

    // the cells of a grid or section (inside its inset); each child at its cell position, in its size
    private Canvas BuildGrid(LayoutControl layout, ColorScope scope)
    {
        var inset = GridLayout.InsetOf(layout);
        // at the top left of its frame: sizing the frame (or a frame larger than its grid) does not move the cells
        var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        // in Edit mode the empty cells are the grid, to drop on
        if (isEditing)
            canvas.Background = Brushes.Transparent;
        if (layout.Size is { } size)
        {
            canvas.Width = (size.Columns - inset.Left - inset.Right) * Cell;
            canvas.Height = (size.Rows - inset.Top - inset.Bottom) * Cell;
        }
        canvas.Children.AddRange(layout.Children.Select(child => Build(child, scope)));
        _grids[layout] = canvas;
        return canvas;
    }

    // the parameter controls that have a face; the other kinds are shown as a placeholder
    private static readonly Dictionary<ParameterControlKind, Func<ParameterControl, ParameterControlBase>> Faced = new()
    {
        [ParameterControlKind.Knob] = control => new KnobControl { Unit = control.Display.Unit, Decimals = control.Display.Decimals },
        [ParameterControlKind.Slider] = control => new FaderControl { Unit = control.Display.Unit, Decimals = control.Display.Decimals },
        [ParameterControlKind.NumberBox] = control => new NumberBoxControl { Unit = control.Display.Unit, Decimals = control.Display.Decimals },
        [ParameterControlKind.Switch] = _ => new SwitchControl(),
        [ParameterControlKind.Text] = _ => new TextControl(),
        [ParameterControlKind.Selector] = _ => new SelectorControl(),
        [ParameterControlKind.XyPad] = _ => new XyPadControl(),
        [ParameterControlKind.Envelope] = control => new EnvelopeCurveControl
        {
            SegmentCount = ((EnvelopeControl)control).Segments.Count,
            HoldSegment = ((EnvelopeControl)control).Segments.ToList().FindIndex(segment => segment.Hold) is >= 0 and var hold ? hold : null,
        },
    };

    /// <summary>True when a kind of parameter control has its face; the others are shown as a placeholder.</summary>
    public static bool HasFace(ParameterControlKind kind) => Faced.ContainsKey(kind);

    // the control of its kind with the values of its slots: a control with one slot follows its parameter, one with
    // more slots (an XY pad, an envelope) the value of each
    private Control BuildParameter(ParameterControl control)
    {
        if (!Faced.TryGetValue(control.Kind, out var create))
            return Placeholder(control, control.Kind.ToString(), session.ValueOf(control, SlotNames.Value)?.Info.Name);
        var built = create(control);
        // a slider runs along the long side of its cells
        if (control.Kind == ParameterControlKind.Slider && control.Size is { } size && GridLayout.IsWide(size))
            ControlOrientation.SetOrientation(built, Orientation.Horizontal);

        var parameter = session.ValueOf(control, SlotNames.Value);
        var slots = control.Slots.Keys
            .Select(slot => (Slot: slot, Value: session.ValueOf(control, slot)))
            .Where(slot => slot.Value is not null)
            .ToDictionary(slot => slot.Slot, slot => slot.Value!, StringComparer.Ordinal);
        if (parameter is null)
            built.SlotValues = slots;
        built.Parameter = parameter;
        built.Label = control.Label ?? parameter?.Info.Name ?? control.Kind.ToString();
        // an unbound control (no binding, or a problem with it) shows that it does nothing; in Edit mode it is selected
        built.IsEnabled = slots.Count > 0 || isEditing;
        return built;
    }

    // a kind without its face yet: shown, selectable and bound, not left out
    private static Control Placeholder(DashboardControl control, string kind, string? parameter)
    {
        var text = new TextBlock { Text = $"{control.Label ?? parameter ?? kind}: {kind} is not available yet", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("ConsoleTextBrush"));
        text.Bind(TextBlock.FontFamilyProperty, text.GetResourceObservable("ConsoleLabelFont"));
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Opacity = 0.6,
            Background = Brushes.Transparent,
            Child = text,
        };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("ConsoleTextBrush"));
        return border;
    }

    /// <summary>
    /// A control being dragged: what is dragged, where the pointer started (in the panel the control is in and in the
    /// control itself), whether it is sized or moved, whether it is in a grid, and its placement before and now.
    /// </summary>
    private sealed record Drag(DashboardControl Control, Control Built, Visual Reference, Point Start, Point Grab, bool IsSizing, bool InGrid, Placement Original)
    {
        public Placement Current { get; set; }
    }

    /// <summary>
    /// Where a drop or move goes: the layout control, and the parameter control under the pointer (null over a layout
    /// control).
    /// </summary>
    private sealed record DropTarget(LayoutControl Container, ParameterControl? Control);

    /// <summary>
    /// The colours of a control and the controls inside it: the device they are of and per area its colour.
    /// </summary>
    private sealed record ColorScope(string? Device, IReadOnlyList<AreaScope> Areas)
    {
        /// <summary>No device, the look's colours.</summary>
        public static ColorScope Look { get; } = new(null, [.. AreaColors.Areas.Select(_ => new AreaScope(null, IsGiven: false))]);

        public AreaScope this[ColorArea area] => Areas[(int)area];
    }

    /// <summary>
    /// The colour of an area (null: the look's) and whether a control gave it (then the devices inside it do not
    /// change it).
    /// </summary>
    private readonly record struct AreaScope(RgbColor? Color, bool IsGiven);
}
