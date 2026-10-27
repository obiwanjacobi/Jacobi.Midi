namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// An element of a dashboard: a <see cref="ParameterControl"/> bound to device parameters or a
/// <see cref="LayoutControl"/> that arranges other controls. Holds what every control has: its place in the layout,
/// its label and colour, and the device and base path the bindings inside it are relative to.
/// </summary>
public closed class DashboardControl
{
    /// <summary>The name shown; for a parameter control the default is the parameter's field name.</summary>
    public string? Label { get; init; }

    /// <summary>The size in cells; null for the default size of the control type.</summary>
    public CellSize? Size { get; init; }

    /// <summary>The cell position; only used inside a grid.</summary>
    public CellPosition? At { get; init; }

    /// <summary>The share of the remaining space; only used inside a row, column or section.</summary>
    public int? Grow { get; init; }

    /// <summary>Another colour for this control and the controls inside it (<c>#RRGGBB</c>).</summary>
    public string? Color { get; init; }

    /// <summary>The device alias of the bindings in and below this control; inherited by the controls inside it.</summary>
    public string? Device { get; init; }

    /// <summary>
    /// The full schema path (from the map root) that the binding paths in and below this control are relative to;
    /// inherited by the controls inside it. Bases do not stack: the nearest one is used.
    /// </summary>
    public string? Base { get; init; }
}

/// <summary>
/// A control with named slots, each bound to one device parameter.
/// </summary>
public closed class ParameterControl : DashboardControl
{
    public abstract ParameterControlKind Kind { get; }

    /// <summary>How the values are presented: unit and decimals.</summary>
    public DisplaySettings Display { get; init; } = DisplaySettings.Default;

    /// <summary>The bound slots by name; an unbound slot is left out.</summary>
    public abstract IEnumerable<KeyValuePair<string, Binding>> Slots { get; }
}

/// <summary>
/// A parameter control with a fixed set of slots: a knob, slider, switch, selector, number box, text or XY pad.
/// </summary>
public sealed class ValueControl : ParameterControl
{
    public ValueControl(ParameterControlKind kind)
    {
        if (kind == ParameterControlKind.Envelope)
            throw new ArgumentException($"An envelope is an {nameof(EnvelopeControl)}.", nameof(kind));
        Kind = kind;
    }

    public override ParameterControlKind Kind { get; }

    /// <summary>The bindings by slot name (see <see cref="SlotNames"/>).</summary>
    public IReadOnlyDictionary<string, Binding> Bindings { get; init; } = new Dictionary<string, Binding>();

    public override IEnumerable<KeyValuePair<string, Binding>> Slots => Bindings;
}

/// <summary>
/// An envelope: a list of segments, each with an optional time and level parameter. ADSR, AR and multi-stage
/// envelopes are all lists of segments.
/// </summary>
public sealed class EnvelopeControl : ParameterControl
{
    public override ParameterControlKind Kind => ParameterControlKind.Envelope;

    public IReadOnlyList<EnvelopeSegment> Segments { get; init; } = [];

    public override IEnumerable<KeyValuePair<string, Binding>> Slots
    {
        get
        {
            for (var index = 0; index < Segments.Count; index++)
            {
                if (Segments[index].Time is { } time)
                    yield return new(SlotNames.Time(index), time);
                if (Segments[index].Level is { } level)
                    yield return new(SlotNames.Level(index), level);
            }
        }
    }
}

/// <summary>
/// One segment of an envelope: the parameter for its time, the parameter for the level it ends at, and whether
/// it holds (sustains) until note off.
/// </summary>
public sealed record EnvelopeSegment(Binding? Time, Binding? Level = null, bool Hold = false);

/// <summary>
/// A control without MIDI function: it arranges, groups and labels the controls inside it.
/// </summary>
public sealed class LayoutControl : DashboardControl
{
    public required LayoutControlKind Kind { get; init; }

    public IReadOnlyList<DashboardControl> Children { get; init; } = [];

    /// <summary>The direction a section arranges its children in.</summary>
    public LayoutDirection Direction { get; init; } = LayoutDirection.Row;

    /// <summary>The space between the children, in cells.</summary>
    public int Gap { get; init; }

    /// <summary>Whether a row or column wraps its children onto more lines.</summary>
    public bool Wrap { get; init; }
}

/// <summary>The kinds of parameter controls.</summary>
public enum ParameterControlKind
{
    Knob,
    Slider,
    Switch,
    Selector,
    NumberBox,
    Text,
    XyPad,
    Envelope,
}

/// <summary>The kinds of layout controls.</summary>
public enum LayoutControlKind
{
    Row,
    Column,
    Grid,
    Section,
    Tabs,
    Spacer,
    Separator,
    Label,
}

/// <summary>The direction in which a layout control arranges its children.</summary>
public enum LayoutDirection
{
    Row,
    Column,
}

/// <summary>A size in cells.</summary>
public readonly record struct CellSize(int Columns, int Rows);

/// <summary>A cell position inside a grid, zero based.</summary>
public readonly record struct CellPosition(int Column, int Row);

/// <summary>
/// How a parameter control presents its values. A display concern of the dashboard: the schema says what a value
/// is, the dashboard how it is shown.
/// </summary>
public sealed record DisplaySettings
{
    public static DisplaySettings Default { get; } = new();

    /// <summary>The text after the value (<c>ms</c>, <c>Hz</c>, ...).</summary>
    public string? Unit { get; init; }

    /// <summary>The number of decimals shown; null for as many as the value has.</summary>
    public int? Decimals { get; init; }
}

/// <summary>
/// The names of the slots of the parameter controls.
/// </summary>
public static class SlotNames
{
    /// <summary>The slot of a single-value control (knob, slider, switch, selector, number box, text).</summary>
    public const string Value = "value";
    /// <summary>The lower value of a slider with two values.</summary>
    public const string Min = "min";
    /// <summary>The upper value of a slider with two values.</summary>
    public const string Max = "max";
    /// <summary>The horizontal value of an XY pad.</summary>
    public const string X = "x";
    /// <summary>The vertical value of an XY pad.</summary>
    public const string Y = "y";

    /// <summary>The time slot of an envelope segment (zero based index).</summary>
    public static string Time(int segment) => $"segment{segment + 1}.time";

    /// <summary>The level slot of an envelope segment (zero based index).</summary>
    public static string Level(int segment) => $"segment{segment + 1}.level";
}
