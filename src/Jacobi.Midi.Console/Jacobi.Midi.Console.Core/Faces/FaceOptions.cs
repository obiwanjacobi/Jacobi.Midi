using System.Globalization;
using Jacobi.Midi.Console.Core.Dashboards;

namespace Jacobi.Midi.Console.Core.Faces;

/// <summary>The kind of value of a <see cref="FaceOption"/>.</summary>
public enum FaceOptionType
{
    /// <summary>On or off: <c>true</c> or <c>false</c>.</summary>
    Switch,

    /// <summary>A number within a range.</summary>
    Number,

    /// <summary>One of a few names.</summary>
    Choice,
}

/// <summary>
/// The declaration of an option of a parameter control's appearance: a part of its chrome or face that can be switched
/// (a plate, a knob's pointer) or a setting of how it is drawn (a stroke width). The declaration gives its name, its
/// kind of value and its default; a look gives its own defaults and a control its own value (see
/// <see cref="OptionValues"/>). Values are kept as text, in the form of the files (<c>true</c>, <c>8</c>, <c>flat</c>).
/// </summary>
public sealed record FaceOption(string Name, FaceOptionType Type, string Default, string Description)
{
    /// <summary>The smallest value of a number.</summary>
    public decimal Minimum { get; init; }

    /// <summary>The largest value of a number.</summary>
    public decimal Maximum { get; init; }

    /// <summary>
    /// True for an option that changes the cells of the control (the place of the headers of tabs): only the control
    /// sets it, a look gives no default, so a dashboard has the same layout in every look.
    /// </summary>
    public bool IsLayout { get; init; }

    /// <summary>The names of a choice.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>A part that is on or off.</summary>
    public static FaceOption Switch(string name, bool @default, string description)
        => new(name, FaceOptionType.Switch, Text(@default), description);

    /// <summary>A number from <paramref name="minimum"/> to <paramref name="maximum"/>.</summary>
    public static FaceOption Number(string name, decimal @default, decimal minimum, decimal maximum, string description)
        => new(name, FaceOptionType.Number, Text(@default), description) { Minimum = minimum, Maximum = maximum };

    /// <summary>One of <paramref name="choices"/>.</summary>
    public static FaceOption Choice(string name, string @default, IReadOnlyList<string> choices, string description)
        => new(name, FaceOptionType.Choice, @default, description) { Choices = choices };

    /// <summary>
    /// The value in its normal form (<c>true</c>, <c>8</c>, <c>flat</c>); false with the reason when it is not a value of
    /// the option.
    /// </summary>
    public bool TryNormalize(string text, out string value, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);
        value = text;
        error = String.Empty;
        switch (Type)
        {
            case FaceOptionType.Switch when Boolean.TryParse(text, out var on):
                value = Text(on);
                return true;
            case FaceOptionType.Switch:
                error = $"'{text}' is not true or false.";
                return false;
            case FaceOptionType.Number when Decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number):
                if (number < Minimum || number > Maximum)
                {
                    error = $"{text} is not from {Text(Minimum)} to {Text(Maximum)}.";
                    return false;
                }
                value = Text(number);
                return true;
            case FaceOptionType.Number:
                error = $"'{text}' is not a number.";
                return false;
            default:
                if (Choices.FirstOrDefault(choice => String.Equals(choice, text, StringComparison.OrdinalIgnoreCase)) is { } name)
                {
                    value = name;
                    return true;
                }
                error = $"'{text}' is not one of {String.Join(", ", Choices)}.";
                return false;
        }
    }

    /// <summary>A switch's value as text.</summary>
    public static string Text(bool value) => value ? "true" : "false";

    /// <summary>A number's value as text (invariant, without trailing zeros).</summary>
    public static string Text(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}

/// <summary>
/// The options of the parameter controls, declared once per control kind: the options of the chrome (every control
/// with a face) and those of each kind's face. The dashboard file, the control panel and the looks only use these
/// declarations; a face reads the values of its options by name. An option means the same in every kind that has it
/// (<c>width</c>, <c>ends</c>), so a look can give it once.
/// </summary>
public static class FaceOptions
{
    /// <summary>The ends of a stroke.</summary>
    public static IReadOnlyList<string> LineEnds { get; } = ["round", "flat"];

    /// <summary>Where the label is, in the chrome.</summary>
    public static IReadOnlyList<string> LabelPlaces { get; } = ["above", "below", "none"];

    /// <summary>Where the headers of tabs are.</summary>
    public static IReadOnlyList<string> HeaderPlaces { get; } = ["top", "left"];

    /// <summary>Where a knob's value is: below the face (in the chrome's readout) or inside its arc (drawn by the face).</summary>
    public static IReadOnlyList<string> KnobValuePlaces { get; } = ["below", "inside", "none"];

    /// <summary>Where the value is, in the chrome: below the face (its readout) or none.</summary>
    public static IReadOnlyList<string> ValuePlaces { get; } = ["below", "none"];

    /// <summary>The options of the chrome around every face.</summary>
    public static IReadOnlyList<FaceOption> Chrome { get; } =
    [
        FaceOption.Switch("plate", true, "The plate the control is mounted on; off: the control is on the panel (a plate color then has no effect)."),
        FaceOption.Switch("fasteners", true, "The screws or rivets that hold the plate, in a look that has them."),
        FaceOption.Choice("label", "above", LabelPlaces, "Where the label is: above or below the face, or none."),
        FaceOption.Choice("value", "below", ValuePlaces, "Where the value is: below the face (the readout), or none. A face that shows the value itself (a number box, a text) is not changed."),
    ];

    /// <summary>The options of a knob's face.</summary>
    public static IReadOnlyList<FaceOption> Knob { get; } =
    [
        FaceOption.Switch("track", true, "The range without value. An unknown value always shows a dotted track."),
        FaceOption.Switch("body", true, "The body and cap that turn."),
        FaceOption.Switch("pointer", true, "The mark on the body that points at the value."),
        FaceOption.Number("width", 5, 1, 12, "The width of the value arc and the track, in pixels (at a cell of 24)."),
        FaceOption.Choice("ends", "round", LineEnds, "The ends of the value arc and the track."),
        FaceOption.Choice("value", "below", KnobValuePlaces, "Where the value is: below the face (the readout of the chrome), inside the arc, or none."),
    ];

    // the dark display or screen behind a face's text, number or plot
    private static FaceOption Display { get; } =
        FaceOption.Switch("display", true, "The dark display or screen behind the text, number or plot; off: it is on the plate or panel.");

    /// <summary>The options of a slider's face.</summary>
    public static IReadOnlyList<FaceOption> Slider { get; } =
    [
        FaceOption.Switch("track", true, "The slot, or the unlit segments, of the range without value."),
        FaceOption.Switch("cap", true, "The cap that slides."),
        FaceOption.Number("width", 10, 2, 40, "The width of the slot and its fill, in pixels (at a cell of 24)."),
        FaceOption.Number("segments", 0, 0, 32, "The number of segments of the fill (a meter); 0: one continuous fill."),
        FaceOption.Choice("ends", "round", LineEnds, "The ends of the slot and its fill."),
    ];

    /// <summary>The options of a switch's face.</summary>
    public static IReadOnlyList<FaceOption> Switch { get; } =
    [
        FaceOption.Switch("lamp", true, "The lamp or LED that is lit when the switch is on, in a face that has one."),
        FaceOption.Choice("ends", "round", LineEnds, "The outer ends of the keys, in a face of keys (on and off)."),
    ];

    /// <summary>The options of a selector's face.</summary>
    public static IReadOnlyList<FaceOption> Selector { get; } =
    [
        Display,
        FaceOption.Switch("lamp", true, "The LED above each option name, in a face that has one."),
        FaceOption.Choice("ends", "flat", LineEnds, "The outer ends of each row of keys: round (a pill) or flat."),
    ];

    /// <summary>The options of a number box's face.</summary>
    public static IReadOnlyList<FaceOption> NumberBox { get; } = [Display];

    /// <summary>The options of a text's face.</summary>
    public static IReadOnlyList<FaceOption> Text { get; } = [Display];

    /// <summary>The options of an XY pad's face.</summary>
    public static IReadOnlyList<FaceOption> XyPad { get; } =
    [
        Display,
        FaceOption.Switch("grid", true, "The grid of the screen."),
        FaceOption.Switch("crosshair", true, "The lines from the point to the edges."),
    ];

    /// <summary>The options of an envelope's face.</summary>
    public static IReadOnlyList<FaceOption> Envelope { get; } =
    [
        Display,
        FaceOption.Switch("fill", true, "The fill under the curve, in a face that has one."),
        FaceOption.Switch("beads", true, "The beads or dots on the breakpoints."),
    ];

    /// <summary>The options of the headers of tabs and selector tabs.</summary>
    public static IReadOnlyList<FaceOption> Pages { get; } =
    [
        FaceOption.Choice("headers", "top", HeaderPlaces, "Where the headers are: above the pages (in two rows) or at their left (in columns).") with { IsLayout = true },
        FaceOption.Number("headerWidth", 0, 0, 24, "The width of a header in cells; 0: on top as wide as its name, at the left 5.") with { IsLayout = true },
    ];

    /// <summary>The options of a control: by its kind.</summary>
    public static IReadOnlyList<FaceOption> Of(DashboardControl control)
        => control switch
        {
            ParameterControl parameter => Of(parameter.Kind),
            LayoutControl layout => Of(layout.Kind),
            _ => [],
        };

    /// <summary>The options of a layout control kind: those of the headers of tabs; none for the others.</summary>
    public static IReadOnlyList<FaceOption> Of(LayoutControlKind kind)
        => kind == LayoutControlKind.Tabs ? Pages : [];

    /// <summary>
    /// The options of a parameter control kind: those of the chrome and of its face (the headers of selector tabs). A face
    /// may declare an option of the chrome again with more choices (the knob's <c>value</c> also <c>inside</c>): its own
    /// declaration is the kind's.
    /// </summary>
    public static IReadOnlyList<FaceOption> Of(ParameterControlKind kind)
        => kind switch
        {
            ParameterControlKind.SelectorTabs => Pages,
            ParameterControlKind.Knob => WithChrome(Knob),
            ParameterControlKind.Slider => WithChrome(Slider),
            ParameterControlKind.Switch => WithChrome(Switch),
            ParameterControlKind.Selector => WithChrome(Selector),
            ParameterControlKind.NumberBox => WithChrome(NumberBox),
            ParameterControlKind.Text => WithChrome(Text),
            ParameterControlKind.XyPad => WithChrome(XyPad),
            ParameterControlKind.Envelope => WithChrome(Envelope),
            _ => Chrome,
        };

    // the options of the chrome that the face does not declare itself, then those of the face
    private static IReadOnlyList<FaceOption> WithChrome(IReadOnlyList<FaceOption> face)
        => [.. Chrome.Where(option => face.All(own => own.Name != option.Name)), .. face];

    /// <summary>The values of all options of a control: its own, the declared defaults for the others (no look).</summary>
    public static OptionValues Complete(DashboardControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return Complete(Of(control), control.Options);
    }

    /// <summary>The option of a control kind by its name; null when the kind has none.</summary>
    public static FaceOption? Find(ParameterControlKind kind, string name)
        => Of(kind).FirstOrDefault(option => option.Name == name);

    /// <summary>The values of all options of a kind: those given, the declared defaults for the others.</summary>
    public static OptionValues Complete(ParameterControlKind kind, OptionValues given)
        => Complete(Of(kind), given);

    /// <summary>The values of all <paramref name="options"/>: those given, the declared defaults for the others.</summary>
    public static OptionValues Complete(IReadOnlyList<FaceOption> options, OptionValues given)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(given);
        return given.Over(new OptionValues(options.Select(option => KeyValuePair.Create(option.Name, option.Default))));
    }
}

/// <summary>
/// Values of options by name, in their normal form (see <see cref="FaceOption"/>): the options a control sets, the
/// defaults of a look, or all options of a control as it is drawn (its own values over the look's over the declared
/// defaults, see <see cref="Over"/>). An option that is not in a control's values is inherited. Immutable; equal when
/// the same options have the same values.
/// </summary>
public sealed class OptionValues : IEquatable<OptionValues>
{
    private readonly Dictionary<string, string> _values;

    public static OptionValues Empty { get; } = new([]);

    public OptionValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    /// <summary>Values written as <c>name=value</c> pairs, for the looks: e.g. <c>("body=false", "width=8")</c>.</summary>
    public static OptionValues Of(params string[] pairs)
        => new(pairs.Select(pair => pair.Split('=', 2) is [var name, var value]
            ? KeyValuePair.Create(name.Trim(), value.Trim())
            : throw new ArgumentException($"'{pair}' is not name=value.", nameof(pairs))));

    public int Count => _values.Count;

    /// <summary>The options that have a value, in no particular order.</summary>
    public IEnumerable<string> Names => _values.Keys;

    /// <summary>The value of an option; null when it has none here.</summary>
    public string? this[string name] => _values.GetValueOrDefault(name);

    /// <summary>These values with an option set to <paramref name="value"/>, or taken away (null: inherited).</summary>
    public OptionValues With(string name, string? value)
    {
        var values = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        if (value is null)
            values.Remove(name);
        else
            values[name] = value;
        return new OptionValues(values);
    }

    /// <summary>These values, and those of <paramref name="defaults"/> for the options that have none here.</summary>
    public OptionValues Over(OptionValues defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (defaults.Count == 0)
            return this;
        var values = new Dictionary<string, string>(defaults._values, StringComparer.Ordinal);
        foreach (var (name, value) in _values)
            values[name] = value;
        return new OptionValues(values);
    }

    /// <summary>The value of a switch.</summary>
    public bool Switch(string name) => Required(name) == FaceOption.Text(true);

    /// <summary>The value of a number.</summary>
    public double Number(string name) => Double.Parse(Required(name), CultureInfo.InvariantCulture);

    /// <summary>The value of a choice.</summary>
    public string Choice(string name) => Required(name);

    // the values a face reads are complete (FaceOptions.Complete): a missing one is an option that is not declared
    private string Required(string name)
        => _values.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"The option '{name}' has no value: it is not declared for this control.");

    public bool Equals(OptionValues? other)
        => other is not null && other._values.Count == _values.Count
            && _values.All(pair => other._values.TryGetValue(pair.Key, out var value) && value == pair.Value);

    public override bool Equals(object? obj) => Equals(obj as OptionValues);

    public override int GetHashCode()
        => _values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value));

    public override string ToString() => String.Join(", ", _values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
}
