using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry knob: an engraved track with the value in the accent, a bakelite body with a brass cap and a pointer.
/// An unknown value has a dotted track, a faded body and no pointer.
/// </summary>
public sealed class FoundryKnobFace : Face
{
    protected override Size Cells => new(3, 3);

    private double Radius => Math.Min(Bounds.Width, Bounds.Height) / 2 - 4;

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState range)
            return;

        var center = Center;
        var outline = new Pen(OutlineBrush, 1);
        if (range.Fraction is null)
        {
            context.DrawGeometry(null, new Pen(TrackBrush, 3, new DashStyle([0.5, 2.5], 0), PenLineCap.Round),
                Arc(center, Radius, StartAngle, StartAngle + SweepAngle));
            using (context.PushOpacity(0.45))
                context.DrawEllipse(BodyBrush, outline, center, Radius * 0.72, Radius * 0.72);
            context.DrawEllipse(null, new Pen(SurfaceBrush, 1.5), center, Radius * 0.28, Radius * 0.28);
            return;
        }

        context.DrawGeometry(null, new Pen(TrackBrush, 5, lineCap: PenLineCap.Round), Arc(center, Radius, StartAngle, StartAngle + SweepAngle));
        context.DrawEllipse(BodyBrush, outline, center, Radius * 0.72, Radius * 0.72);
        context.DrawEllipse(SurfaceBrush, null, center, Radius * 0.28, Radius * 0.28);
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        var center = Center;
        DrawValueArc(context, new Pen(AccentBrush, 5, lineCap: PenLineCap.Round), center, Radius, range.Origin, value);
        var angle = AngleOf(value);
        context.DrawLine(new Pen(PointerBrush, 2.5, lineCap: PenLineCap.Round), PointAt(center, Radius * 0.34, angle), PointAt(center, Radius * 0.64, angle));
    }
}
