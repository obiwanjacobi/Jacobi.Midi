using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Parameters;

namespace Jacobi.Midi.Console.Core.Editing;

/// <summary>
/// The controls the console offers in Edit mode: the control suggested for a parameter that is dropped on a dashboard,
/// new controls of a kind (in their default size, see <see cref="GridLayout.DefaultSize(ParameterControlKind)"/>), and
/// the kinds a control can be changed to, keeping what it has.
/// </summary>
public static class ControlCatalog
{
    public static IReadOnlyList<ParameterControlKind> ParameterKinds { get; } = Enum.GetValues<ParameterControlKind>();

    public static IReadOnlyList<LayoutControlKind> LayoutKinds { get; } = Enum.GetValues<LayoutControlKind>();

    /// <summary>
    /// The control for a parameter: a selector for options, a switch for a bool, a text for a string, a knob for a
    /// number with a range and a number box for any other number.
    /// </summary>
    public static ParameterControlKind Suggest(ParameterInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.Kind switch
        {
            ParameterKind.Choice => ParameterControlKind.Selector,
            ParameterKind.Switch => ParameterControlKind.Switch,
            ParameterKind.Text => ParameterControlKind.Text,
            _ => info.Range is null ? ParameterControlKind.NumberBox : ParameterControlKind.Knob,
        };
    }

    /// <summary>
    /// True when a control of the kind can show the parameter: a switch only an on/off (bool) parameter, a selector only
    /// a choice, a text anything, a number box a number (with or without range); the knob, slider, XY pad and envelope a
    /// value in a range (a number, an on/off or a choice, see <see cref="ParameterInfo.Range"/>).
    /// </summary>
    public static bool Accepts(ParameterControlKind kind, ParameterInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return kind switch
        {
            ParameterControlKind.Switch => info.Kind == ParameterKind.Switch,
            ParameterControlKind.Selector => info.Kind == ParameterKind.Choice,
            ParameterControlKind.Text => true,
            ParameterControlKind.NumberBox => info.Range is not null || info.Kind == ParameterKind.Number,
            _ => info.Range is not null,
        };
    }

    /// <summary>Why a control of the kind cannot show the parameter (see <see cref="Accepts"/>); null when it can.</summary>
    public static string? RefusalOf(ParameterControlKind kind, ParameterInfo info)
    {
        if (Accepts(kind, info))
            return null;
        var shows = kind switch
        {
            ParameterControlKind.Switch => "an on/off parameter (a bool)",
            ParameterControlKind.Selector => "a parameter with options",
            ParameterControlKind.NumberBox => "a number",
            _ => "a value in a range (a number, an on/off or options)",
        };
        return $"a {kind} shows only {shows}; '{info.Name}' is a {info.Kind.ToString().ToLowerInvariant()}.";
    }

    /// <summary>A new, unbound parameter control of a kind in its default size; an envelope is an ADSR (see <see cref="Adsr"/>).</summary>
    public static ParameterControl Create(ParameterControlKind kind)
        => kind == ParameterControlKind.Envelope
            ? new EnvelopeControl { Size = GridLayout.DefaultSize(kind), Segments = Adsr }
            : new ValueControl(kind) { Size = GridLayout.DefaultSize(kind) };

    /// <summary>The segments of an ADSR envelope, not bound: attack, decay (to the sustain level, held) and release.</summary>
    public static IReadOnlyList<EnvelopeSegment> Adsr { get; } = [new(null), new(null, null, Hold: true), new(null)];

    /// <summary>A new, empty layout control of a kind in its default size.</summary>
    public static LayoutControl Create(LayoutControlKind kind)
        => new() { Kind = kind, Size = GridLayout.DefaultSize(kind) };

    /// <summary>
    /// The kinds a control can be changed to, its own included: a single-value control to another (an envelope keeps
    /// its kind), a grid to a section and back, a row to a column and back; empty for the other layout controls.
    /// </summary>
    public static IReadOnlyList<Enum> KindsOf(DashboardControl control)
        => control switch
        {
            ValueControl => [.. ParameterKinds.Where(kind => kind != ParameterControlKind.Envelope).Cast<Enum>()],
            LayoutControl { Kind: LayoutControlKind.Grid or LayoutControlKind.Section } => [LayoutControlKind.Grid, LayoutControlKind.Section],
            LayoutControl { Kind: LayoutControlKind.Row or LayoutControlKind.Column } => [LayoutControlKind.Row, LayoutControlKind.Column],
            _ => [],
        };

    /// <summary>
    /// A control of another kind (one of <see cref="KindsOf"/>) with everything the control has: its place, label,
    /// colours, device and base; a parameter control its display settings and the bindings of the slots the kind has
    /// (the others are dropped), a layout control its children and variables.
    /// </summary>
    /// <exception cref="ArgumentException">The control cannot be changed to the kind.</exception>
    public static DashboardControl WithKind(DashboardControl control, Enum kind)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(kind);
        if (!KindsOf(control).Contains(kind))
            throw new ArgumentException($"A {KindOf(control)} cannot become a {kind}.", nameof(kind));

        DashboardControl changed = control switch
        {
            ValueControl value => new ValueControl((ParameterControlKind)kind)
            {
                Display = value.Display,
                Bindings = value.Bindings.Where(slot => SlotNames.Of((ParameterControlKind)kind).Contains(slot.Key))
                    .ToDictionary(slot => slot.Key, slot => slot.Value, StringComparer.Ordinal),
            },
            LayoutControl layout => new LayoutControl
            {
                Kind = (LayoutControlKind)kind,
                Children = layout.Children,
                Variables = layout.Variables,
                Gap = layout.Gap,
                Wrap = layout.Wrap,
            },
            _ => throw new ArgumentException($"A {KindOf(control)} cannot change its kind.", nameof(control)),
        };
        changed.Label = control.Label;
        changed.Size = control.Size;
        changed.At = control.At;
        changed.Grow = control.Grow;
        changed.Colors = control.Colors;
        changed.Device = control.Device;
        changed.Base = control.Base;
        return changed;
    }

    /// <summary>
    /// What goes into tabs for a control: each child of tabs is a page, so a grid or section is a page itself and any
    /// other control is put on a new grid page (labelled after the number of the page) in its size: the control
    /// belongs at the top left of the page (<see cref="CellPosition"/> 0,0), which the caller gives it.
    /// </summary>
    public static LayoutControl PageFor(LayoutControl tabs, DashboardControl control)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(control);
        if (control is LayoutControl layout && GridLayout.IsGrid(layout))
            return layout;
        return new LayoutControl
        {
            Kind = LayoutControlKind.Grid,
            Label = $"Page {tabs.Children.Count + 1}",
            Size = control.Size ?? new CellSize(1, 1),
            Children = [control],
        };
    }

    /// <summary>The kind of a control, for the user.</summary>
    public static string KindOf(DashboardControl control)
        => control switch
        {
            ParameterControl parameter => parameter.Kind.ToString(),
            LayoutControl layout => layout.Kind.ToString(),
            _ => control.GetType().Name,
        };

    /// <summary>The name of a control for the user: its label, else its kind.</summary>
    public static string NameOf(DashboardControl control) => control.Label ?? KindOf(control);
}
