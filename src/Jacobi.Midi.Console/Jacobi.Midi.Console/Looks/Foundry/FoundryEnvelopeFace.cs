using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry envelope: an amber screen with the curve in the accent over a faint fill, and brass beads on the breakpoints.
/// An unknown time or level makes the curve dashed, without fill.
/// </summary>
public sealed class FoundryEnvelopeFace : Face
{
    protected override Size Cells => new(8, 3.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);
        var area = PlotArea(Bounds.Size);
        context.DrawLine(new Pen(TrackBrush, 1), PlotPoint(area, 0, 0), PlotPoint(area, 1, 0));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not EnvelopeFaceState envelope || envelope.Breakpoints.Count == 0)
            return;
        var area = PlotArea(Bounds.Size);
        var points = EnvelopeShape.Points(envelope);
        var isKnown = envelope.Breakpoints.All(breakpoint => breakpoint.Time is not null && breakpoint.Level is not null);
        if (isKnown)
        {
            using (context.PushOpacity(0.2))
                context.DrawGeometry(AccentBrush, null, EnvelopeCurve(area, points, closed: true));
        }
        var pen = isKnown ? new Pen(AccentBrush, 2, lineJoin: PenLineJoin.Round) : new Pen(TrackBrush, 1.5, new DashStyle([3, 2], 0));
        context.DrawGeometry(null, pen, EnvelopeCurve(area, points, closed: false));
        foreach (var point in points.Where(point => point.Segment is not null))
            context.DrawEllipse(SurfaceBrush, new Pen(OutlineBrush, 1), PlotPoint(area, point.X, point.Y), 4.5, 4.5);
    }
}
