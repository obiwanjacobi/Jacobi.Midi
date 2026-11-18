using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry fader: a dark slot filled with the accent from the origin to the value, and a brass cap. Of the slider's
/// options (<see cref="FaceOptions.Slider"/>) it takes all: <c>track</c> (the slot, or the unlit segments),
/// <c>cap</c>, <c>width</c> (of the slot), <c>segments</c> (a meter of lit segments instead of one fill) and
/// <c>ends</c>. An unknown value has an empty slot and no cap.
/// </summary>
public sealed class FoundryFaderFace : Face
{
    private const double CapHeight = 14;
    private const double SegmentGap = 3;

    protected override bool IsDirectional => true;

    protected override Size Cells => new(Math.Max(2, (Math.Max(SlotWidth, HasCap ? CapWidth : 0) + 8) / CellPixels), 5);

    private double SlotWidth => Options.Number("width");
    private bool HasCap => Options.Switch("cap");
    private double CapWidth => Math.Max(34, SlotWidth + 16);
    private int Segments => (int)Options.Number("segments");
    private bool IsRound => Options.Choice("ends") == "round";

    private double Top => CapHeight / 2;
    private double Bottom => Extent.Height - CapHeight / 2;

    public override double TravelLength => Bottom - Top;
    private Rect Slot => new((Extent.Width - SlotWidth) / 2, Top, SlotWidth, Bottom - Top);

    private Rect Segment(int segment)
    {
        var pitch = (Bottom - Top + SegmentGap) / Segments;
        return new Rect(Slot.X, Bottom - (segment + 1) * pitch + SegmentGap, SlotWidth, pitch - SegmentGap);
    }

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (!Options.Switch("track"))
            return;
        if (Segments == 0)
        {
            var radius = IsRound ? SlotWidth / 2 : 0;
            context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), Slot, radius, radius);
            return;
        }
        for (var segment = 0; segment < Segments; segment++)
            context.DrawRectangle(TrackBrush, null, Segment(segment), IsRound ? 2 : 0, IsRound ? 2 : 0);
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        double YOf(double fraction) => Bottom - (Bottom - Top) * fraction;
        var to = YOf(value);
        if (Segments == 0)
        {
            var from = YOf(range.Origin);
            var inset = Math.Min(2, SlotWidth / 4);
            context.DrawRectangle(AccentBrush, null, new Rect(Slot.X + inset, Math.Min(from, to), SlotWidth - inset * 2, Math.Abs(to - from)), IsRound ? 2 : 0, IsRound ? 2 : 0);
        }
        else
        {
            for (var segment = 0; segment < Segments; segment++)
            {
                if (IsLit((segment + 0.5) / Segments, range.Origin, value))
                    context.DrawRectangle(AccentBrush, null, Segment(segment), IsRound ? 2 : 0, IsRound ? 2 : 0);
            }
        }

        if (!HasCap)
            return;
        var cap = new Rect((Extent.Width - CapWidth) / 2, to - CapHeight / 2, CapWidth, CapHeight);
        context.DrawRectangle(SurfaceBrush, new Pen(OutlineBrush, 1), cap, 2, 2);
        context.DrawLine(new Pen(PointerBrush, 2), new Point(cap.X + 4, to), new Point(cap.Right - 4, to));
    }
}
