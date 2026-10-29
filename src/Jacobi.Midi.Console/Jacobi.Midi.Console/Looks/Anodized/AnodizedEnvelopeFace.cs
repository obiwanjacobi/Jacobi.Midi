using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Anodized;

/// <summary>
/// Anodized envelope: an OLED screen with the curve lit in the accent (with the finish's glow) and lit dots on the
/// breakpoints. An unknown time or level makes the curve dashed and unlit.
/// </summary>
public sealed class AnodizedEnvelopeFace : Face
{
    protected override Size Cells => new(8, 3.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);
        var area = PlotArea(Bounds.Size);
        context.DrawLine(new Pen(TrackBrush, 1, new DashStyle([1, 3], 0)), PlotPoint(area, 0, 0), PlotPoint(area, 1, 0));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not EnvelopeFaceState envelope || envelope.Breakpoints.Count == 0)
            return;
        var area = PlotArea(Bounds.Size);
        var points = EnvelopeShape.Points(envelope);
        var curve = EnvelopeCurve(area, points, closed: false);
        if (!envelope.Breakpoints.All(breakpoint => breakpoint.Time is not null && breakpoint.Level is not null))
        {
            context.DrawGeometry(null, new Pen(TrackBrush, 1.5, new DashStyle([3, 2], 0)), curve);
            return;
        }
        if (AccentGlow is { } glow)
            context.DrawGeometry(null, new Pen(glow, 6, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round), curve);
        context.DrawGeometry(null, new Pen(AccentBrush, 2, lineJoin: PenLineJoin.Round), curve);
        foreach (var point in points.Where(point => point.Segment is not null))
            context.DrawEllipse(AccentBrush, null, PlotPoint(area, point.X, point.Y), 3.5, 3.5);
    }
}
