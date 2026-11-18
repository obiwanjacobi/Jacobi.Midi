using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// A knob drawn as an arc: a track with the value in the accent, a body with a cap and a pointer. Its parts and
/// strokes are options (<see cref="FaceOptions.Knob"/>: <c>track</c>, <c>body</c>, <c>pointer</c>, <c>width</c>,
/// <c>ends</c>), so one face serves looks as different as Foundry (engraved track, bakelite body, brass cap) and LCARS
/// (a thick flat arc only); the materials are the look's brushes. An unknown value has a dotted track (also without
/// <c>track</c>, so the knob does not vanish), a faded body and no pointer. With the value inside (option <c>value</c>:
/// the face state's text) the value text is drawn in the middle, over the body when it has one.
/// </summary>
public sealed class ArcKnobFace : Face
{
    protected override Size Cells => new(3, 3);

    private double StrokeWidth => Options.Number("width");

    private PenLineCap Ends => Options.Choice("ends") == "flat" ? PenLineCap.Flat : PenLineCap.Round;

    // the arc inside the face, whatever its width
    private double Radius => Math.Min(Bounds.Width, Bounds.Height) / 2 - Math.Max(4, StrokeWidth / 2 + 1.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState range)
            return;

        var center = Center;
        var outline = new Pen(OutlineBrush, 1);
        var body = Options.Switch("body");
        if (range.Fraction is null)
        {
            context.DrawGeometry(null, new Pen(TrackBrush, 3, new DashStyle([0.5, 2.5], 0), PenLineCap.Round),
                Arc(center, Radius, StartAngle, StartAngle + SweepAngle));
            if (body)
            {
                using (context.PushOpacity(0.45))
                    context.DrawEllipse(BodyBrush, outline, center, Radius * 0.72, Radius * 0.72);
                context.DrawEllipse(null, new Pen(SurfaceBrush, 1.5), center, Radius * 0.28, Radius * 0.28);
            }
            return;
        }

        if (Options.Switch("track"))
            context.DrawGeometry(null, new Pen(TrackBrush, StrokeWidth, lineCap: Ends), Arc(center, Radius, StartAngle, StartAngle + SweepAngle));
        if (body)
        {
            context.DrawEllipse(BodyBrush, outline, center, Radius * 0.72, Radius * 0.72);
            context.DrawEllipse(SurfaceBrush, null, center, Radius * 0.28, Radius * 0.28);
        }
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is RangeFaceState { Text: { } text })
            DrawValueInside(context, text, Center, Radius, DisplayFont, AccentDisplayBrush);
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        var center = Center;
        DrawValueArc(context, new Pen(AccentBrush, StrokeWidth, lineCap: Ends), center, Radius, range.Origin, value);
        if (Options.Switch("pointer"))
        {
            var angle = AngleOf(value);
            context.DrawLine(new Pen(PointerBrush, 2.5, lineCap: PenLineCap.Round), PointAt(center, Radius * 0.34, angle), PointAt(center, Radius * 0.64, angle));
        }
    }
}
