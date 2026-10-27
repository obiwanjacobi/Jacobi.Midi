using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Jacobi.Midi.Console.Chrome;
using Jacobi.Midi.Console.Controls;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Dashboards;

/// <summary>
/// Builds the Avalonia controls of an open dashboard: layout controls become panels (row, column, grid, section,
/// tabs, spacer, separator, label), parameter controls the controls of their kind bound to their parameter. The
/// colour cascade is resource lookup: a control with a device or a colour of its own sets the accent resource for
/// itself and the controls inside it. Kinds that are not built yet are shown as a placeholder, not left out.
/// Everything is laid out in cells of <see cref="Face.CellPixels"/>; the dashboard as a whole is scaled to its cell
/// size, so controls, fonts, gaps and grids scale together.
/// </summary>
internal sealed class DashboardBuilder(DashboardSession session)
{
    private const string AccentKey = "ConsoleAccentBrush";

    private const double Cell = Face.CellPixels;

    public Control Build()
    {
        var scale = session.Dashboard.CellSize / Face.CellPixels;
        return new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(scale, scale),
            Child = Build(session.Dashboard.Layout),
        };
    }

    private Control Build(DashboardControl control)
    {
        var built = control switch
        {
            LayoutControl layout => BuildLayout(layout),
            ValueControl value => BuildParameter(value),
            _ => Placeholder(control, "envelope"),
        };

        if (control.Size is { } size)
        {
            built.Width = size.Columns * Cell;
            built.Height = size.Rows * Cell;
        }
        if (control.At is { } at)
        {
            Grid.SetColumn(built, at.Column);
            Grid.SetRow(built, at.Row);
            Grid.SetColumnSpan(built, control.Size?.Columns ?? 1);
            Grid.SetRowSpan(built, control.Size?.Rows ?? 1);
        }

        // the colour of the control, else of its device: for this control and the controls inside it
        var color = control.Color ?? (control.Device is { } device ? session.ColorOf(device) : null);
        if (color is not null && Color.TryParse(color, out var parsed))
            built.Resources[AccentKey] = new SolidColorBrush(parsed);
        return built;
    }

    private Control BuildLayout(LayoutControl layout)
    {
        var gap = layout.Gap * Cell;
        switch (layout.Kind)
        {
            case LayoutControlKind.Row:
            case LayoutControlKind.Column:
                return Arrange(layout, layout.Kind == LayoutControlKind.Row ? Orientation.Horizontal : Orientation.Vertical, gap);

            case LayoutControlKind.Section:
                return new SectionFrame
                {
                    Header = layout.Label,
                    Content = Arrange(layout, layout.Direction == LayoutDirection.Row ? Orientation.Horizontal : Orientation.Vertical, gap),
                };

            case LayoutControlKind.Grid:
                return BuildGrid(layout);

            case LayoutControlKind.Tabs:
                return new TabControl
                {
                    ItemsSource = layout.Children.Select(child => new TabItem
                    {
                        Header = child.Label ?? $"Page {layout.Children.ToList().IndexOf(child) + 1}",
                        Content = Build(child),
                    }).ToList(),
                };

            case LayoutControlKind.Spacer:
                return new Border { Width = Cell, Height = Cell };

            case LayoutControlKind.Separator:
                return new Separator();

            case LayoutControlKind.Label:
                var label = new TextBlock { Text = layout.Label, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("ConsoleTextBrush"));
                label.Bind(TextBlock.FontFamilyProperty, label.GetResourceObservable("ConsoleLabelFont"));
                return label;

            default:
                return Placeholder(layout, layout.Kind.ToString());
        }
    }

    // a row or column: a stack, a wrap panel, or a grid when children share the remaining space (grow)
    private Panel Arrange(LayoutControl layout, Orientation orientation, double gap)
    {
        var children = layout.Children.Select(Build).ToList();
        if (layout.Wrap)
        {
            var wrap = new WrapPanel { Orientation = orientation, ItemSpacing = gap, LineSpacing = gap };
            wrap.Children.AddRange(children);
            return wrap;
        }
        if (layout.Children.All(child => child.Grow is null or 0))
        {
            var stack = new StackPanel { Orientation = orientation, Spacing = gap };
            stack.Children.AddRange(children);
            return stack;
        }

        var grid = new Grid();
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

    // a grid of cells; each child at its cell position, spanning its size
    private Grid BuildGrid(LayoutControl layout)
    {
        var columns = layout.Children.Select(child => (child.At?.Column ?? 0) + (child.Size?.Columns ?? 1)).DefaultIfEmpty(1).Max();
        var rows = layout.Children.Select(child => (child.At?.Row ?? 0) + (child.Size?.Rows ?? 1)).DefaultIfEmpty(1).Max();
        var grid = new Grid();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(Cell, GridUnitType.Pixel));
        for (var row = 0; row < rows; row++)
            grid.RowDefinitions.Add(new RowDefinition(Cell, GridUnitType.Pixel));
        grid.Children.AddRange(layout.Children.Select(Build));
        return grid;
    }

    private Control BuildParameter(ValueControl control)
    {
        ParameterControlBase? built = control.Kind switch
        {
            ParameterControlKind.Knob => new KnobControl { Unit = control.Display.Unit, Decimals = control.Display.Decimals },
            ParameterControlKind.Slider => new FaderControl { Unit = control.Display.Unit, Decimals = control.Display.Decimals },
            ParameterControlKind.NumberBox => new NumberBoxControl { Unit = control.Display.Unit, Decimals = control.Display.Decimals },
            ParameterControlKind.Switch => new SwitchControl(),
            ParameterControlKind.Text => new TextControl(),
            _ => null,
        };
        if (built is null)
            return Placeholder(control, control.Kind.ToString());

        var parameter = session.ValueOf(control, SlotNames.Value);
        built.Parameter = parameter;
        built.Label = control.Label ?? parameter?.Info.Name ?? control.Kind.ToString();
        // an unbound control (no binding, or a problem with it) shows that it does nothing
        built.IsEnabled = parameter is not null;
        return built;
    }

    private static Control Placeholder(DashboardControl control, string kind)
    {
        var text = new TextBlock { Text = $"{control.Label ?? kind}: {kind} is not available yet", FontSize = 11 };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("ConsoleTextBrush"));
        text.Bind(TextBlock.FontFamilyProperty, text.GetResourceObservable("ConsoleLabelFont"));
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Opacity = 0.6,
            Child = text,
        };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("ConsoleTextBrush"));
        return border;
    }
}
