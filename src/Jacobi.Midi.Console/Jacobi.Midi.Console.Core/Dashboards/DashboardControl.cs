using Jacobi.Midi.Console.Core.Palettes;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// An element of a dashboard: a <see cref="ParameterControl"/> bound to device parameters or a
/// <see cref="LayoutControl"/> that arranges other controls. Holds what every control has: its place in the layout,
/// its label and colour, and the device and base path the bindings inside it are relative to.
/// </summary>
public closed class DashboardControl
{
    /// <summary>The name shown; for a parameter control the default is the parameter's field name.</summary>
    public string? Label { get; set; }

    /// <summary>The size in cells; null until it is given (see <see cref="GridLayout.Complete"/>).</summary>
    public CellSize? Size { get; set; }

    /// <summary>
    /// The cell position in a grid or section (of its grid, inside the section's frame); not used inside a row, column
    /// or tabs. Null until it is given (see <see cref="GridLayout.Complete"/>).
    /// </summary>
    public CellPosition? At { get; set; }

    /// <summary>The share of the remaining space; only used inside a row or column.</summary>
    public int? Grow { get; set; }

    /// <summary>
    /// Other colours (accent, plate, panel) for this control and the controls inside it; the user changes them in the
    /// console.
    /// </summary>
    public AreaColors Colors { get; set; } = AreaColors.None;

    /// <summary>The device alias of the bindings in and below this control; inherited by the controls inside it.</summary>
    public string? Device { get; set; }

    /// <summary>
    /// The full schema path (from the map root, with repeat indices) that the binding paths in and below this control
    /// are relative to; inherited by the controls inside it. Bases do not stack: the nearest one is used.
    /// </summary>
    public string? Base { get; set; }
}

/// <summary>
/// A control with named slots, each bound to one device parameter.
/// </summary>
public closed class ParameterControl : DashboardControl
{
    public abstract ParameterControlKind Kind { get; }

    /// <summary>How the values are presented: unit and decimals.</summary>
    public DisplaySettings Display { get; set; } = DisplaySettings.Default;

    /// <summary>
    /// The bound slots by name; an unbound slot is left out. Setting it binds the slots anew (see
    /// <see cref="SlotNames"/> for the names a kind has).
    /// </summary>
    public abstract IReadOnlyDictionary<string, Binding> Slots { get; set; }
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
    public IReadOnlyDictionary<string, Binding> Bindings { get; set; } = new Dictionary<string, Binding>();

    public override IReadOnlyDictionary<string, Binding> Slots
    {
        get => Bindings;
        set => Bindings = value;
    }
}

/// <summary>
/// An envelope: a list of segments, each with an optional time and level parameter. ADSR, AR and multi-stage
/// envelopes are all lists of segments.
/// </summary>
public sealed class EnvelopeControl : ParameterControl
{
    public override ParameterControlKind Kind => ParameterControlKind.Envelope;

    public IReadOnlyList<EnvelopeSegment> Segments { get; set; } = [];

    /// <summary>The time and level slots of the segments; setting them keeps the segments and their hold.</summary>
    /// <exception cref="ArgumentException">A slot name is not the time or level of a segment.</exception>
    public override IReadOnlyDictionary<string, Binding> Slots
    {
        get
        {
            var slots = new Dictionary<string, Binding>(StringComparer.Ordinal);
            for (var index = 0; index < Segments.Count; index++)
            {
                if (Segments[index].Time is { } time)
                    slots[SlotNames.Time(index)] = time;
                if (Segments[index].Level is { } level)
                    slots[SlotNames.Level(index)] = level;
            }
            return slots;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var count = Segments.Count;
            foreach (var slot in value.Keys)
            {
                if (SlotNames.SegmentOf(slot) is not { } segment)
                    throw new ArgumentException($"'{slot}' is not the time or level of a segment.", nameof(value));
                count = Math.Max(count, segment + 1);
            }
            Segments = [.. Enumerable.Range(0, count).Select(index => new EnvelopeSegment(
                value.GetValueOrDefault(SlotNames.Time(index)),
                value.GetValueOrDefault(SlotNames.Level(index)),
                index < Segments.Count && Segments[index].Hold))];
        }
    }
}

/// <summary>
/// One segment of an envelope: the parameter for its time, the parameter for the level it ends at, and whether
/// it holds (sustains) until note off.
/// </summary>
public sealed record EnvelopeSegment(Binding? Time, Binding? Level = null, bool Hold = false);

/// <summary>
/// A control without MIDI function: it arranges, groups and labels the controls inside it, and may declare the index
/// variables the bindings inside it use.
/// </summary>
public sealed class LayoutControl : DashboardControl
{
    public required LayoutControlKind Kind { get; init; }

    public IReadOnlyList<DashboardControl> Children { get; set; } = [];

    /// <summary>
    /// The index variables of the controls inside this one (<c>Programs[$patch]</c>). A variable of a nearer layout
    /// control hides one with the same name further out.
    /// </summary>
    public IReadOnlyList<IndexVariable> Variables { get; set; } = [];

    /// <summary>The space between the children, in cells.</summary>
    public int Gap { get; set; }

    /// <summary>Whether a row or column wraps its children onto more lines.</summary>
    public bool Wrap { get; set; }
}

/// <summary>
/// An index into a repeat of a device's address space that the user changes in the UI, e.g. the patch number.
/// Declared on a layout control and used in the paths of the bindings inside it: <c>Programs[$patch].Program.Level</c>.
/// A parameter control bound to <c>$patch</c> changes it, like any parameter: changing it re-binds the controls that
/// use it.
/// </summary>
/// <param name="Name">The name used in paths after <c>$</c>.</param>
/// <param name="Value">The zero based index the dashboard starts with.</param>
public sealed record IndexVariable(string Name, int Value = 0);

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

/// <summary>A size in cells; written as <c>columns,rows</c> (e.g. <c>2,2</c>).</summary>
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

    /// <summary>The (zero based) segment of an envelope's time or level slot; null for another slot.</summary>
    public static int? SegmentOf(string slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        const string prefix = "segment";
        var dot = slot.IndexOf('.');
        if (!slot.StartsWith(prefix, StringComparison.Ordinal) || dot < 0
            || !Int32.TryParse(slot.AsSpan(prefix.Length, dot - prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            || number < 1)
        {
            return null;
        }
        var segment = number - 1;
        return slot == Time(segment) || slot == Level(segment) ? segment : null;
    }

    /// <summary>
    /// The slots a control shows, in order: <c>value</c> (a slider's <c>min</c> and <c>max</c> are not shown yet),
    /// <c>x</c> and <c>y</c> of an XY pad, the time and level of each segment of an envelope (at least one segment).
    /// </summary>
    public static IReadOnlyList<string> Shown(ParameterControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control switch
        {
            EnvelopeControl envelope => [.. Enumerable.Range(0, Math.Max(1, envelope.Segments.Count)).SelectMany(index => new[] { Time(index), Level(index) })],
            { Kind: ParameterControlKind.XyPad } => [X, Y],
            _ => [Value],
        };
    }

    /// <summary>The slots a control kind has; an envelope's slots follow from its segments (none here).</summary>
    public static IReadOnlySet<string> Of(ParameterControlKind kind)
        => kind switch
        {
            ParameterControlKind.Slider => SliderSlots,
            ParameterControlKind.XyPad => XySlots,
            ParameterControlKind.Envelope => NoSlots,
            _ => ValueSlots,
        };

    private static readonly IReadOnlySet<string> ValueSlots = new HashSet<string> { Value };
    private static readonly IReadOnlySet<string> SliderSlots = new HashSet<string> { Value, Min, Max };
    private static readonly IReadOnlySet<string> XySlots = new HashSet<string> { X, Y };
    private static readonly IReadOnlySet<string> NoSlots = new HashSet<string>();
}
