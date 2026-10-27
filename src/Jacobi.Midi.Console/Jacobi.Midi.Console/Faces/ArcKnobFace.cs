using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// A flat knob face: an arc from the origin to the value around a solid body with a pointer. An unknown value is
/// drawn hollow: a dotted track, no arc and no pointer.
/// </summary>
public sealed class ArcKnobFace : Face
{
    private const double Diameter = 72;
    private const double StrokeThickness = 3;
    // the track runs clockwise from bottom-left (135°) to bottom-right (405°); angles from +x, y down
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    protected override Size PreferredSize => new(Diameter, Diameter);

    public override void Render(DrawingContext context)
    {
        if (State is not { } state || state is not RangeFaceState range)
            return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - StrokeThickness;

        if (range.Fraction is not { } fraction)
        {
            var dotted = new Pen(TrackBrush, StrokeThickness, new DashStyle([0.3, 2.5], 0), PenLineCap.Round);
            context.DrawGeometry(null, dotted, Arc(center, radius, StartAngle, StartAngle + SweepAngle));
            context.DrawEllipse(null, new Pen(BodyBrush, 2), center, radius * 0.68, radius * 0.68);
            return;
        }

        var track = new Pen(TrackBrush, StrokeThickness, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, track, Arc(center, radius, StartAngle, StartAngle + SweepAngle));

        var originAngle = AngleOf(range.Origin);
        var valueAngle = AngleOf(fraction);
        // no arc when the value is at the origin
        if (Math.Abs(valueAngle - originAngle) >= 0.01)
            context.DrawGeometry(null, new Pen(AccentBrush, StrokeThickness, lineCap: PenLineCap.Round), Arc(center, radius, originAngle, valueAngle));

        context.DrawEllipse(BodyBrush, null, center, radius * 0.7, radius * 0.7);
        var pointer = new Pen(PointerBrush, 2.5, lineCap: PenLineCap.Round);
        context.DrawLine(pointer, PointAt(center, radius * 0.28, valueAngle), PointAt(center, radius * 0.6, valueAngle));
    }

    private static double AngleOf(double fraction) => StartAngle + SweepAngle * fraction;

    private static Point PointAt(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static StreamGeometry Arc(Point center, double radius, double fromAngle, double toAngle)
    {
        var sweep = toAngle - fromAngle;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(center, radius, fromAngle), isFilled: false);
            context.ArcTo(PointAt(center, radius, toAngle), new Size(radius, radius), 0,
                isLargeArc: Math.Abs(sweep) > 180,
                sweep > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            context.EndFigure(isClosed: false);
        }
        return geometry;
    }
}
