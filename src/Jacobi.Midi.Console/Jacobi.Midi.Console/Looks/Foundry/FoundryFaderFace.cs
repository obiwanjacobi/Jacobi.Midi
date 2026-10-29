using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry fader: a dark slot filled with the accent from the origin to the value, and a brass cap.
/// An unknown value has an empty slot and no cap.
/// </summary>
public sealed class FoundryFaderFace : Face
{
    private const double SlotWidth = 10;
    private const double CapWidth = 34;
    private const double CapHeight = 14;

    protected override bool IsDirectional => true;

    protected override Size Cells => new(2, 5);

    private double Top => CapHeight / 2;
    private double Bottom => Extent.Height - CapHeight / 2;

    public override double TravelLength => Bottom - Top;
    private Rect Slot => new((Extent.Width - SlotWidth) / 2, Top, SlotWidth, Bottom - Top);

    protected override void RenderStatic(DrawingContext context, FaceState state)
        => context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), Slot, SlotWidth / 2, SlotWidth / 2);

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        double YOf(double fraction) => Bottom - (Bottom - Top) * fraction;
        var from = YOf(range.Origin);
        var to = YOf(value);
        context.DrawRectangle(AccentBrush, null, new Rect(Slot.X + 2, Math.Min(from, to), SlotWidth - 4, Math.Abs(to - from)), 2, 2);

        var cap = new Rect((Extent.Width - CapWidth) / 2, to - CapHeight / 2, CapWidth, CapHeight);
        context.DrawRectangle(SurfaceBrush, new Pen(OutlineBrush, 1), cap, 2, 2);
        context.DrawLine(new Pen(PointerBrush, 2), new Point(cap.X + 4, to), new Point(cap.Right - 4, to));
    }
}
