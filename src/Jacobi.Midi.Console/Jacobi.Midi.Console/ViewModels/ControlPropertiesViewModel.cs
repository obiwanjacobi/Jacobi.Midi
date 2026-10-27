using System.Globalization;
using Avalonia.Data;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Looks;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The properties of the control selected in Edit mode, for the property panel: what it is, its label, size, display
/// settings and colours, which the user changes (each change an edit of the <see cref="MainViewModel"/>), and its
/// device, base, variables and bindings, shown as they are. The position and size are always set: changing them places
/// the control in its grid (see <see cref="MainViewModel.Place"/>). A value that cannot be taken is a validation error
/// of the field.
/// </summary>
internal sealed partial class ControlPropertiesViewModel(MainViewModel owner, DashboardControl control) : ViewModelBase
{
    public DashboardControl Control => control;

    public string Kind => KindOf(control);

    public string? Label
    {
        get => control.Label;
        set => owner.EditControl(control, "label", () => control.Label, label => control.Label = label, NullIfEmpty(value));
    }

    /// <summary>True when the control is in a grid or section: it has a position (<see cref="Column"/>, <see cref="Row"/>).</summary>
    public bool IsInGrid => owner.IsInGrid(control);

    /// <summary>The column of the control in its grid (zero based).</summary>
    public decimal? Column
    {
        get => control.At?.Column;
        set => PlaceAt(value, current => current.At with { Column = Whole(value, 0) });
    }

    /// <summary>The row of the control in its grid (zero based).</summary>
    public decimal? Row
    {
        get => control.At?.Row;
        set => PlaceAt(value, current => current.At with { Row = Whole(value, 0) });
    }

    /// <summary>The width in cells.</summary>
    public decimal? Columns
    {
        get => control.Size?.Columns;
        set => PlaceSize(value, current => current.Size with { Columns = Whole(value, 1) });
    }

    /// <summary>The height in cells.</summary>
    public decimal? Rows
    {
        get => control.Size?.Rows;
        set => PlaceSize(value, current => current.Size with { Rows = Whole(value, 1) });
    }

    public bool IsParameter => control is ParameterControl;

    public string? Unit
    {
        get => (control as ParameterControl)?.Display.Unit;
        set
        {
            if (control is ParameterControl parameter)
                EditDisplay(parameter, "unit", parameter.Display with { Unit = NullIfEmpty(value) }, NullIfEmpty(value) ?? "none");
        }
    }

    /// <summary>The number of decimals; empty for as many as the value has.</summary>
    public string Decimals
    {
        get => (control as ParameterControl)?.Display.Decimals?.ToString(CultureInfo.InvariantCulture) ?? String.Empty;
        set
        {
            if (control is not ParameterControl parameter)
                return;
            int? decimals = null;
            if (!String.IsNullOrWhiteSpace(value))
            {
                if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                    throw new DataValidationException($"'{value}' is not a number of decimals (0 or more).");
                decimals = parsed;
            }
            EditDisplay(parameter, "decimals", parameter.Display with { Decimals = decimals }, decimals?.ToString(CultureInfo.InvariantCulture) ?? "as many as the value has");
        }
    }

    public string AccentColor => ColorText(ColorArea.Accent);

    public string PlateColor => ColorText(ColorArea.Plate);

    public string PanelColor => ColorText(ColorArea.Panel);

    public IBrush? AccentBrush => BrushOf(ColorArea.Accent);

    public IBrush? PlateBrush => BrushOf(ColorArea.Plate);

    public IBrush? PanelBrush => BrushOf(ColorArea.Panel);

    /// <summary>What the bindings in and below the control are relative to, and what they are bound to.</summary>
    public IReadOnlyList<string> Bindings
    {
        get
        {
            var lines = new List<string>();
            if (control.Device is { } device)
                lines.Add($"device: {device}");
            if (control.Base is { } basePath)
                lines.Add($"base: {basePath}");
            switch (control)
            {
                case LayoutControl layout:
                    lines.AddRange(layout.Variables.Select(variable => $"${variable.Name} = {variable.Value}"));
                    break;
                case ValueControl value:
                    lines.AddRange(value.Bindings.Select(binding => $"{binding.Key}: {binding.Value}"));
                    break;
                case EnvelopeControl envelope:
                    lines.Add($"{envelope.Segments.Count} segment(s)");
                    break;
            }
            return lines;
        }
    }

    /// <summary>The control has no colour of its own for the area any more: it inherits it.</summary>
    [RelayCommand]
    private void ClearColor(ColorArea area) => owner.SetControlColor(control, area, null);

    /// <summary>Shows the values as they are now: after an edit or undo.</summary>
    public void Refresh() => OnPropertyChanged(String.Empty);

    /// <summary>The name of a control for the user: its label, else its kind.</summary>
    public static string NameOf(DashboardControl control) => control.Label ?? KindOf(control);

    private static string KindOf(DashboardControl control)
        => control switch
        {
            ParameterControl parameter => parameter.Kind.ToString(),
            LayoutControl layout => layout.Kind.ToString(),
            _ => control.GetType().Name,
        };

    // a position or size is always set: an empty field changes nothing
    private void PlaceAt(decimal? value, Func<Placement, CellPosition> at)
    {
        if (value is not null && Current() is { } current)
            owner.Place(control, at(current), current.Size);
    }

    private void PlaceSize(decimal? value, Func<Placement, CellSize> size)
    {
        if (value is not null && Current() is { } current)
            owner.Place(control, current.At, size(current));
    }

    private Placement? Current()
        => control.Size is { } size ? new Placement(control.At ?? default, size) : null;

    private static int Whole(decimal? value, int minimum) => Math.Max(minimum, (int)Math.Round(value ?? minimum));

    private void EditDisplay(ParameterControl parameter, string what, DisplaySettings display, string shown)
        => owner.EditControl(parameter, what, () => parameter.Display, value => parameter.Display = value, display, shown);

    private string ColorText(ColorArea area) => control.Colors[area] ?? "inherited";

    private IBrush? BrushOf(ColorArea area)
        => RgbColor.TryParse(control.Colors[area], out var color) ? new SolidColorBrush(PaletteBrushes.ToColor(color.Value)) : null;

    private static string? NullIfEmpty(string? text) => String.IsNullOrWhiteSpace(text) ? null : text;
}
