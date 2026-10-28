namespace Jacobi.Midi.Console.Core.Faces;

/// <summary>
/// The face state of a control with a number in a range (knob, slider, number box).
/// </summary>
/// <param name="Fraction">The value as a fraction of the range (0-1); null when the value is unknown.</param>
/// <param name="Origin">Where the value is drawn from, as a fraction of the range: 0 for the minimum, 0.5 for the centre of a bipolar range.</param>
/// <param name="IsHorizontal">True when the range runs from left to right (a horizontal slider); otherwise from the bottom up.</param>
public readonly record struct RangeFaceState(double? Fraction, double Origin, bool IsHorizontal = false);

/// <summary>The face state of a switch.</summary>
/// <param name="IsOn">Null when the value is unknown.</param>
public readonly record struct SwitchFaceState(bool? IsOn);

/// <summary>
/// The face state of a selector: a row of segments, one per option, or with more options than
/// <see cref="MaxSegments"/> a display with the selected option and ▲/▼ (<see cref="IsList"/>).
/// </summary>
/// <param name="Selected">The index into <paramref name="Options"/>; null when the value is unknown.</param>
public readonly record struct SelectorFaceState(IReadOnlyList<string> Options, int? Selected)
{
    /// <summary>The most options shown as segments.</summary>
    public const int MaxSegments = 6;

    /// <summary>True when the options are too many for segments: the selected one is shown in a display.</summary>
    public bool IsList => Options.Count > MaxSegments;
}

/// <summary>The face state of a text.</summary>
/// <param name="Text">Null when the value is unknown.</param>
public readonly record struct TextFaceState(string? Text);

/// <summary>The face state of an XY pad: both values as fractions of their range (0-1), null when unknown.</summary>
public readonly record struct XyFaceState(double? X, double? Y);

/// <summary>
/// One breakpoint of an envelope: the time and level of a segment as fractions of their range (0-1), null when unknown;
/// a time or level that is not bound has the fixed value the control gives it.
/// </summary>
public readonly record struct EnvelopeBreakpoint(double? Time, double? Level);

/// <summary>
/// The face state of an envelope: one breakpoint per segment, and the segment that holds until note off (see
/// <see cref="EnvelopeShape"/> for where it is drawn).
/// </summary>
public readonly record struct EnvelopeFaceState(IReadOnlyList<EnvelopeBreakpoint> Breakpoints, int? HoldSegment);

/// <summary>
/// All a face gets from its control: a small, immutable description of what to draw. A face draws it with the
/// palette's brushes and nothing else; it never sees the device state, the schema or MIDI.
/// </summary>
public union FaceState(RangeFaceState, SwitchFaceState, SelectorFaceState, TextFaceState, XyFaceState, EnvelopeFaceState);
