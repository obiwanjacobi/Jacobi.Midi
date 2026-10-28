using Avalonia;
using Avalonia.Input;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The envelope: a list of segments, each with a time and a level slot (<c>segmentN.time</c>, <c>segmentN.level</c>),
/// drawn as a curve (see <see cref="EnvelopeShape"/>). Dragging the breakpoint of a segment changes its time and level
/// together. A time or level that is not bound has a fixed value and is not changed: half the share of the segment, and
/// the maximum level (0 for the last segment, which ends the envelope).
/// </summary>
public sealed class EnvelopeCurveControl : ParameterControlBase
{
    /// <summary>The number of segments.</summary>
    public static readonly StyledProperty<int> SegmentCountProperty =
        AvaloniaProperty.Register<EnvelopeCurveControl, int>(nameof(SegmentCount));

    /// <summary>The segment that holds until note off; null when none does.</summary>
    public static readonly StyledProperty<int?> HoldSegmentProperty =
        AvaloniaProperty.Register<EnvelopeCurveControl, int?>(nameof(HoldSegment));

    // how near (in pixels of the face) a press must be to a breakpoint to drag it
    private const double GrabDistance = 12;

    private int? _dragged;

    public EnvelopeCurveControl()
    {
        UpdateFace();
    }

    public int SegmentCount
    {
        get => GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    public int? HoldSegment
    {
        get => GetValue(HoldSegmentProperty);
        set => SetValue(HoldSegmentProperty, value);
    }

    protected override void Refresh() => UpdateFace();

    protected override void Reset() => ResetSlots();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentCountProperty || change.Property == HoldSegmentProperty)
            UpdateFace();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !CanChange || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || FaceState is not { } faceState || faceState is not EnvelopeFaceState state
            || FacePoint(e.GetPosition(this)) is not (Point point, Size size))
        {
            return;
        }
        var area = FaceDrawing.PlotArea(size);
        var nearest = EnvelopeShape.Points(state)
            .Where(breakpoint => breakpoint.Segment is not null)
            .Select(breakpoint => (breakpoint.Segment, Distance: Point.Distance(point, FaceDrawing.PlotPoint(area, breakpoint.X, breakpoint.Y))))
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (nearest.Segment is null || nearest.Distance > GrabDistance)
            return;
        _dragged = nearest.Segment;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this || _dragged is not { } segment || FaceState is not { } faceState || faceState is not EnvelopeFaceState state
            || FacePoint(e.GetPosition(this)) is not (Point point, Size size))
        {
            return;
        }
        var (x, y) = FaceDrawing.PlotFractions(FaceDrawing.PlotArea(size), point);
        WriteFraction(SlotNames.Time(segment), EnvelopeShape.TimeAt(state, segment, x));
        WriteFraction(SlotNames.Level(segment), y);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragged = null;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    // a breakpoint per segment: the bound values (null while unknown), the fixed ones for the slots not bound
    private void UpdateFace()
    {
        var breakpoints = Enumerable.Range(0, SegmentCount).Select(segment => new EnvelopeBreakpoint(
            SlotValues.ContainsKey(SlotNames.Time(segment)) ? FractionOf(SlotNames.Time(segment)) : EnvelopeShape.UnknownTime,
            SlotValues.ContainsKey(SlotNames.Level(segment)) ? FractionOf(SlotNames.Level(segment)) : segment == SegmentCount - 1 ? 0 : 1)).ToList();
        FaceState = new EnvelopeFaceState(breakpoints, HoldSegment);
        ValueText = null;
    }
}
