using System.Globalization;
using Avalonia.Data;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Dashboards;
using Binding = Jacobi.Midi.Console.Core.Dashboards.Binding;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Looks;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The properties of the control selected in Edit mode, for the control panel: its kind (and the kinds it can become),
/// label, size, display settings, colours, and its device, base and bindings; for a layout control the variables it
/// declares and, for a row or column, its arrangement. The user changes them, each change an edit of the
/// <see cref="MainViewModel"/>. The position and size are always set: changing them places the control in its grid
/// (see <see cref="MainViewModel.Place"/>). A value that cannot be taken is a validation error of the field.
/// </summary>
internal sealed partial class ControlPropertiesViewModel(MainViewModel owner, DashboardControl control) : ViewModelBase
{
    // the order the slots of a kind are shown in
    private static readonly string[] SlotOrder = [SlotNames.Value, SlotNames.Min, SlotNames.Max, SlotNames.X, SlotNames.Y];

    public DashboardControl Control => control;

    public string Kind => ControlCatalog.KindOf(control);

    /// <summary>
    /// The kinds the control can become, its own included (not to choose); empty when it cannot change. A kind that
    /// cannot show what the control is bound to (a switch for a number) is left out. A kind is chosen by its own
    /// command, not by a selection: a panel that shows another control keeps no choice of this one.
    /// </summary>
    public IReadOnlyList<KindOption> Kinds
    {
        get
        {
            if (owner.IsRoot(control))
                return [];
            var kinds = ControlCatalog.KindsOf(control)
                .Where(kind => kind.ToString() == Kind || kind is not ParameterControlKind other || control is not ParameterControl parameter || owner.Accepts(parameter, other))
                .ToList();
            return kinds.Count < 2 ? [] : [.. kinds.Select(kind => new KindOption(kind, kind.ToString() == Kind, () => owner.ChangeKind(control, kind)))];
        }
    }

    public bool CanChangeKind => Kinds.Count > 0;

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

    public bool IsLayout => control is LayoutControl;

    /// <summary>True for a row or column: it has a gap between its children and may wrap them.</summary>
    public bool IsFlex => control is LayoutControl { Kind: LayoutControlKind.Row or LayoutControlKind.Column };

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

    /// <summary>The device alias of the bindings in and below the control; empty to inherit it.</summary>
    public string? Device
    {
        get => control.Device;
        set
        {
            var alias = NullIfEmpty(value);
            if (alias is not null && !owner.IsDeviceOfRig(alias))
                throw new DataValidationException($"The rig has no device '{alias}'.");
            owner.SetControlDevice(control, alias);
        }
    }

    /// <summary>The device the control inherits, shown when it has none of its own.</summary>
    public string InheritedDevice => owner.ScopeAround(control).Device is { } device ? $"inherited: {device}" : "none";

    /// <summary>The full path the bindings in and below the control are relative to; empty to inherit it.</summary>
    public string? Base
    {
        get => control.Base;
        set
        {
            var path = NullIfEmpty(value);
            if (path is not null && !SchemaPaths.TryParse(path, _ => 0, out _, out var error))
                throw new DataValidationException(error);
            owner.EditControl(control, "base", () => control.Base, basePath => control.Base = basePath, path, path ?? "inherited");
        }
    }

    public string InheritedBase => owner.ScopeAround(control).Base is { } basePath ? $"inherited: {basePath}" : "none: paths from the map root";

    /// <summary>The space between the children of a row or column, in cells.</summary>
    public decimal? Gap
    {
        get => (control as LayoutControl)?.Gap;
        set
        {
            if (control is LayoutControl layout && value is not null)
                owner.EditControl(layout, "gap", () => layout.Gap, gap => layout.Gap = gap, Whole(value, 0));
        }
    }

    public bool Wrap
    {
        get => (control as LayoutControl)?.Wrap ?? false;
        set
        {
            if (control is LayoutControl layout)
                owner.EditControl(layout, "wrap", () => layout.Wrap, wrap => layout.Wrap = wrap, value);
        }
    }

    /// <summary>The slots of a parameter control: those of its kind, the time and level of each segment of an envelope.</summary>
    public IReadOnlyList<SlotViewModel> Slots
        => control switch
        {
            ValueControl value => [.. SlotOrder.Where(SlotNames.Of(value.Kind).Contains).Select(slot => new SlotViewModel(this, slot, value.Bindings.GetValueOrDefault(slot)))],
            EnvelopeControl envelope => [.. Enumerable.Range(0, envelope.Segments.Count).SelectMany(index => new[]
            {
                new SlotViewModel(this, SlotNames.Time(index), envelope.Segments[index].Time),
                new SlotViewModel(this, SlotNames.Level(index), envelope.Segments[index].Level),
            })],
            _ => [],
        };

    public bool IsEnvelope => control is EnvelopeControl;

    /// <summary>The segments of an envelope, to set which one holds or to remove one.</summary>
    public IReadOnlyList<SegmentViewModel> Segments
        => control is EnvelopeControl envelope
            ? [.. envelope.Segments.Select((segment, index) => new SegmentViewModel(this, index, segment.Hold))]
            : [];

    /// <summary>Adds a segment at the end of an envelope, not bound.</summary>
    [RelayCommand]
    private void AddSegment()
    {
        if (control is EnvelopeControl envelope)
            EditSegments(envelope, [.. envelope.Segments, new EnvelopeSegment(null)], $"{envelope.Segments.Count + 1} segments");
    }

    /// <summary>Sets whether a segment holds until note off; one segment holds at most.</summary>
    internal void SetHold(int index, bool hold)
    {
        if (control is EnvelopeControl envelope && envelope.Segments[index].Hold != hold)
        {
            EditSegments(envelope, [.. envelope.Segments.Select((segment, other) => segment with { Hold = hold && other == index })],
                hold ? $"segment {index + 1} holds" : "no segment holds");
        }
    }

    /// <summary>Removes a segment of an envelope with its bindings; the segments after it move up.</summary>
    internal void RemoveSegment(int index)
    {
        if (control is EnvelopeControl envelope)
            EditSegments(envelope, [.. envelope.Segments.Where((_, other) => other != index)], $"segment {index + 1} removed");
    }

    private void EditSegments(EnvelopeControl envelope, IReadOnlyList<EnvelopeSegment> segments, string shown)
        => owner.EditControl(envelope, "segments", () => envelope.Segments, value => envelope.Segments = value, segments, shown);

    /// <summary>The index variables a layout control declares.</summary>
    public IReadOnlyList<VariableViewModel> Variables
        => control is LayoutControl layout ? [.. layout.Variables.Select(variable => new VariableViewModel(this, variable))] : [];

    public string AccentColor => ColorText(ColorArea.Accent);

    public string PlateColor => ColorText(ColorArea.Plate);

    public string PanelColor => ColorText(ColorArea.Panel);

    public IBrush? AccentBrush => BrushOf(ColorArea.Accent);

    public IBrush? PlateBrush => BrushOf(ColorArea.Plate);

    public IBrush? PanelBrush => BrushOf(ColorArea.Panel);

    /// <summary>The control has no colour of its own for the area any more: it inherits it.</summary>
    [RelayCommand]
    private void ClearColor(ColorArea area) => owner.SetControlColor(control, area, null);

    [RelayCommand]
    private void Delete() => owner.DeleteControl(control);

    /// <summary>Declares a new index variable, starting at the first item.</summary>
    [RelayCommand]
    private void AddVariable()
    {
        if (control is not LayoutControl layout)
            return;
        var name = "index";
        for (var number = 2; layout.Variables.Any(variable => variable.Name == name); number++)
            name = $"index{number}";
        EditVariables(layout, [.. layout.Variables, new IndexVariable(name)], $"'{SchemaPaths.VariablePrefix}{name}' added");
    }

    /// <summary>Shows the values as they are now: after an edit or undo.</summary>
    public void Refresh() => OnPropertyChanged(String.Empty);

    /// <summary>Binds a slot to the binding text (<c>[device:]path</c> or <c>$name</c>); empty unbinds it.</summary>
    internal void SetSlot(string slot, string? text)
    {
        if (control is not ParameterControl parameter)
            return;
        Binding? binding = null;
        if (!String.IsNullOrWhiteSpace(text) && !Binding.TryParse(text.Trim(), out binding, out var error))
            throw new DataValidationException(error);
        owner.SetSlot(parameter, slot, binding);
    }

    internal void SetVariable(IndexVariable variable, IndexVariable changed)
    {
        if (control is not LayoutControl layout || variable == changed)
            return;
        if (!SchemaPaths.IsVariableName(changed.Name))
            throw new DataValidationException($"'{changed.Name}' is not a variable name (a letter or '_', then letters, digits or '_').");
        if (changed.Name != variable.Name && layout.Variables.Any(other => other.Name == changed.Name))
            throw new DataValidationException($"'{layout.Label ?? Kind}' declares '{changed.Name}' already.");
        EditVariables(layout, [.. layout.Variables.Select(other => other == variable ? changed : other)], $"'{SchemaPaths.VariablePrefix}{changed.Name}' = {changed.Value}");
    }

    internal void RemoveVariable(IndexVariable variable)
    {
        if (control is LayoutControl layout)
            EditVariables(layout, [.. layout.Variables.Where(other => other != variable)], $"'{SchemaPaths.VariablePrefix}{variable.Name}' removed");
    }

    private void EditVariables(LayoutControl layout, IReadOnlyList<IndexVariable> variables, string shown)
        => owner.EditControl(layout, "variables", () => layout.Variables, value => layout.Variables = value, variables, shown);

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

    private static string? NullIfEmpty(string? text) => String.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// A kind the selected control can become, in the control panel: its name, whether it is the control's own kind, and the
/// command that changes the control to it.
/// </summary>
internal sealed partial class KindOption(Enum kind, bool isCurrent, Action change)
{
    public string Name => kind.ToString();

    public bool IsCurrent => isCurrent;

    public bool CanChange => !isCurrent;

    [RelayCommand]
    private void Change() => change();
}

/// <summary>
/// A segment of the selected envelope in the control panel: its number, whether it holds until note off (to change),
/// and a command to remove it.
/// </summary>
internal sealed partial class SegmentViewModel(ControlPropertiesViewModel owner, int index, bool hold)
{
    public string Name => $"Segment {index + 1}";

    public bool Hold
    {
        get => hold;
        set => owner.SetHold(index, value);
    }

    [RelayCommand]
    private void Remove() => owner.RemoveSegment(index);
}

/// <summary>
/// A slot of the selected parameter control in the control panel: its name and its binding as text, to change.
/// </summary>
internal sealed class SlotViewModel(ControlPropertiesViewModel owner, string name, Binding? binding)
{
    public string Name => name;

    /// <summary>The binding, <c>[device:]path</c> or <c>$name</c>; empty when the slot is not bound.</summary>
    public string Text
    {
        get => binding?.ToString() ?? String.Empty;
        set => owner.SetSlot(name, value);
    }
}

/// <summary>
/// An index variable the selected layout control declares, in the control panel: its name and starting item to change,
/// and a command to remove it.
/// </summary>
internal sealed partial class VariableViewModel(ControlPropertiesViewModel owner, IndexVariable variable)
{
    public string Name
    {
        get => variable.Name;
        set => owner.SetVariable(variable, variable with { Name = value?.Trim() ?? String.Empty });
    }

    /// <summary>The zero based index the dashboard starts with.</summary>
    public decimal? Value
    {
        get => variable.Value;
        set
        {
            if (value is not null)
                owner.SetVariable(variable, variable with { Value = Math.Max(0, (int)Math.Round(value.Value)) });
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemoveVariable(variable);
}
