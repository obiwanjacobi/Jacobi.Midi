using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Looks;

namespace Jacobi.Midi.Console.Dashboards;

/// <summary>
/// Changes the colours of an open dashboard and its rig, per <see cref="ColorArea"/>: a control's own colour, or a
/// device's colour (in the rig). Null takes the colour away, so the control or device inherits again. Implemented by
/// the main view model, which marks the file as changed and builds the dashboard again.
/// </summary>
internal interface IColorEditor
{
    /// <summary>The look: its accent and the swatches that suit it.</summary>
    Look Look { get; }

    void SetControlColor(DashboardControl control, ColorArea area, RgbColor? color);

    void SetDeviceColor(string alias, ColorArea area, RgbColor? color);
}

/// <summary>
/// What the dashboard changes in Edit mode: besides the colours, the selection, the place of a control in its grid, the
/// controls dropped on it and moved into another layout control. Implemented by the main view model, every change an
/// edit that can be undone.
/// </summary>
internal interface IDashboardEditor : IColorEditor
{
    /// <summary>Selects a control (null: none).</summary>
    void Select(DashboardControl? control);

    /// <summary>Moves and sizes a control in its grid; the controls it would overlap are pushed aside.</summary>
    void Place(DashboardControl control, CellPosition at, CellSize size);

    /// <summary>Adds what is dropped to a layout control: at a cell of a grid or section, at the end of the others.</summary>
    void Drop(DragItem item, LayoutControl container, CellPosition at);

    /// <summary>Binds the slot of a control to the parameter dropped on it.</summary>
    void Bind(ParameterDragItem item, ParameterControl control);

    /// <summary>Moves a control into another layout control (at a cell of a grid or section), with its bindings.</summary>
    void Move(DashboardControl control, LayoutControl container, CellPosition at);
}

/// <summary>
/// What a control or section shows when the user right-clicks it: picks the colour of one area (the one clicked on)
/// for the control (and the controls inside it) or for its device. Offers the look's swatches of the area, tints and
/// shades of the current colour and any colour; a pick applies at once. "Inherited" takes the colour away. Any colour
/// is a flyout of its own (a popup keeps the size it opened with, so it cannot grow to show the colour view). Without a
/// control (the device panel) the pick is the device's colour.
/// </summary>
internal static class ColorChooser
{
    private const double SwatchSize = 22;

    /// <param name="current">The colour the area has now (own, inherited or the look's).</param>
    /// <param name="control">The control; null to pick the colour of the device.</param>
    /// <param name="device">The device of the control; null when it has none.</param>
    public static Flyout Create(IColorEditor editor, DashboardControl? control, string name, ColorArea area, RgbColor current, string? device)
    {
        var title = $"{AreaColors.NameOf(area)} of '{name}'";
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var forDevice = new CheckBox
        {
            Content = $"For all controls of device '{device}'",
            IsVisible = device is not null && control is not null,
        };

        void Apply(RgbColor? color)
        {
            flyout.Hide();
            if ((forDevice.IsChecked == true || control is null) && device is not null)
                editor.SetDeviceColor(device, area, color);
            else if (control is not null)
                editor.SetControlColor(control, area, color);
        }

        var anyColor = new ColorView
        {
            Color = PaletteBrushes.ToColor(current),
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsColorPaletteVisible = false,
        };
        var back = new Button { Content = "Back" };
        var applyAny = new Button { Content = "Apply" };
        var anyFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                    anyColor,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { back, applyAny } },
                },
            },
        };
        applyAny.Click += (_, _) =>
        {
            anyFlyout.Hide();
            Apply(PaletteBrushes.FromColor(anyColor.Color) with { A = 255 });
        };

        // one flyout at a time, on the same control
        void Switch(PopupFlyoutBase from, PopupFlyoutBase to)
        {
            if (from.Target is not { } target)
                return;
            from.Hide();
            Dispatcher.UIThread.Post(() => to.ShowAt(target));
        }
        back.Click += (_, _) => Switch(anyFlyout, flyout);
        var any = new Button { Content = "Any colour…" };
        any.Click += (_, _) => Switch(flyout, anyFlyout);

        var inherited = new Button { Content = "Inherited" };
        ToolTip.SetTip(inherited, "Take the colour away: the colour of the section, the device or the look");
        inherited.Click += (_, _) => Apply(null);

        flyout.Content = new StackPanel
        {
            Spacing = 8,
            Width = 300,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                forDevice,
                Caption($"{editor.Look.Name} colours"),
                Swatches(editor.Look.Swatches[area], Apply),
                Caption("Tints and shades"),
                Swatches(Palette.TintsAndShades(current, 3), Apply),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { any, inherited } },
            },
        };
        return flyout;
    }

    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 12, Opacity = 0.75 };

    private static WrapPanel Swatches(IEnumerable<RgbColor> colors, Action<RgbColor?> apply)
    {
        var panel = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        foreach (var color in colors)
        {
            // the colour is the content: the button's own background changes when pointed at
            var swatch = new Button
            {
                Width = SwatchSize,
                Height = SwatchSize,
                Padding = new Thickness(2),
                MinWidth = 0,
                MinHeight = 0,
                CornerRadius = new CornerRadius(3),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(PaletteBrushes.ToColor(color)) },
            };
            ToolTip.SetTip(swatch, color.ToString());
            swatch.Click += (_, _) => apply(color);
            panel.Children.Add(swatch);
        }
        return panel;
    }
}
