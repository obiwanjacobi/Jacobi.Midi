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
    /// True when a control of the kind can show the parameter: a switch only an on/off (bool) parameter, a selector and
    /// selector tabs only a choice, a text anything, a number box a number (with or without range); the knob, slider, XY pad and envelope a
    /// value in a range (a number, an on/off or a choice, see <see cref="ParameterInfo.Range"/>).
    /// </summary>
    public static bool Accepts(ParameterControlKind kind, ParameterInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return kind switch
        {
            ParameterControlKind.Switch => info.Kind == ParameterKind.Switch,
            ParameterControlKind.Selector or ParameterControlKind.SelectorTabs => info.Kind == ParameterKind.Choice,
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
            ParameterControlKind.Selector or ParameterControlKind.SelectorTabs => "a parameter with options",
            ParameterControlKind.NumberBox => "a number",
            _ => "a value in a range (a number, an on/off or options)",
        };
        return $"a {kind} shows only {shows}; '{info.Name}' is a {info.Kind.ToString().ToLowerInvariant()}.";
    }

    /// <summary>
    /// A new, unbound parameter control of a kind in its default size; an envelope is an ADSR (see <see cref="Adsr"/>),
    /// selector tabs have no pages yet (they follow the options of the parameter it is bound to).
    /// </summary>
    public static ParameterControl Create(ParameterControlKind kind)
        => kind switch
        {
            ParameterControlKind.Envelope => new EnvelopeControl { Size = GridLayout.DefaultSize(kind), Segments = Adsr },
            ParameterControlKind.SelectorTabs => new SelectorTabsControl { Size = GridLayout.DefaultSize(kind) },
            _ => new ValueControl(kind) { Size = GridLayout.DefaultSize(kind) },
        };

    /// <summary>The segments of an ADSR envelope, not bound: attack, decay (to the sustain level, held) and release.</summary>
    public static IReadOnlyList<EnvelopeSegment> Adsr { get; } = [new(null), new(null, null, Hold: true), new(null)];

    /// <summary>A new, empty layout control of a kind in its default size.</summary>
    public static LayoutControl Create(LayoutControlKind kind)
        => new() { Kind = kind, Size = GridLayout.DefaultSize(kind) };

    /// <summary>
    /// The kinds a control can be changed to, its own included: a single-value control (selector tabs included) to
    /// another (an envelope keeps its kind), a grid to a section and back, a row to a column and back; empty for the
    /// other layout controls. A page of selector tabs (its <paramref name="parent"/>) keeps its kind: a page is a grid.
    /// </summary>
    public static IReadOnlyList<Enum> KindsOf(DashboardControl control, DashboardControl? parent = null)
        => parent is SelectorTabsControl ? [] : control switch
        {
            ValueControl or SelectorTabsControl => [.. ParameterKinds.Where(kind => kind != ParameterControlKind.Envelope).Cast<Enum>()],
            LayoutControl { Kind: LayoutControlKind.Grid or LayoutControlKind.Section } => [LayoutControlKind.Grid, LayoutControlKind.Section],
            LayoutControl { Kind: LayoutControlKind.Row or LayoutControlKind.Column } => [LayoutControlKind.Row, LayoutControlKind.Column],
            _ => [],
        };

    /// <summary>
    /// A control of another kind (one of <see cref="KindsOf"/>) with everything the control has: its place, label,
    /// colors, device and base; a parameter control its display settings and the bindings of the slots the kind has
    /// (the others are dropped), a layout control its children and variables. Selector tabs get a page per option of
    /// <paramref name="options"/> (those of the parameter it is bound to; none without), as when they are bound
    /// (<see cref="SelectorPages.For"/>); selector tabs that become another kind lose their pages.
    /// </summary>
    /// <exception cref="ArgumentException">The control (in its <paramref name="parent"/>) cannot be changed to the kind.</exception>
    public static DashboardControl WithKind(DashboardControl control, Enum kind, IReadOnlyList<string>? options = null, DashboardControl? parent = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(kind);
        if (!KindsOf(control, parent).Contains(kind))
            throw new ArgumentException($"A {KindOf(control)} cannot become a {kind}.", nameof(kind));

        DashboardControl changed = control switch
        {
            ParameterControl parameter => ParameterOfKind(parameter, (ParameterControlKind)kind, options),
            LayoutControl layout => new LayoutControl
            {
                Kind = (LayoutControlKind)kind,
                Children = layout.Children,
                Variables = layout.Variables,
                Gap = layout.Gap,
                Wrap = layout.Wrap,
                Collapsible = layout.Collapsible,
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

    // a single-value control of another kind with the display and the bindings of the slots the kind has
    private static ParameterControl ParameterOfKind(ParameterControl control, ParameterControlKind kind, IReadOnlyList<string>? options)
    {
        var bindings = control.Slots.Where(slot => SlotNames.Of(kind).Contains(slot.Key))
            .ToDictionary(slot => slot.Key, slot => slot.Value, StringComparer.Ordinal);
        if (kind != ParameterControlKind.SelectorTabs)
            return new ValueControl(kind) { Display = control.Display, Bindings = bindings };
        var tabs = new SelectorTabsControl { Display = control.Display, Bindings = bindings, Size = control.Size };
        tabs.Children = SelectorPages.For(tabs, options ?? []);
        return tabs;
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
