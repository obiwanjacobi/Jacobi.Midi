using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry XY pad: an amber screen with an engraved grid, the point in the accent on crosshairs to its edges and a brass
/// bead on it. An unknown value has a dashed ring in the middle.
/// </summary>
public sealed class FoundryXyPadFace : Face
{
    protected override Size Cells => new(4, 4);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);
        var area = PlotArea(Bounds.Size);
        var grid = new Pen(TrackBrush, 1);
        for (var line = 1; line < 4; line++)
        {
            context.DrawLine(grid, PlotPoint(area, line / 4.0, 0), PlotPoint(area, line / 4.0, 1));
            context.DrawLine(grid, PlotPoint(area, 0, line / 4.0), PlotPoint(area, 1, line / 4.0));
        }
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not XyFaceState xy)
            return;
        var area = PlotArea(Bounds.Size);
        if (xy.X is not { } x || xy.Y is not { } y)
        {
            context.DrawEllipse(null, new Pen(TrackBrush, 1.5, new DashStyle([2, 2], 0)), area.Center, 7, 7);
            return;
        }
        var point = PlotPoint(area, x, y);
        var crosshair = new Pen(AccentBrush, 1.5);
        context.DrawLine(crosshair, new Point(point.X, area.Bottom), new Point(point.X, area.Top));
        context.DrawLine(crosshair, new Point(area.Left, point.Y), new Point(area.Right, point.Y));
        if (AccentGlow is { } glow)
            context.DrawEllipse(glow, null, point, 11, 11);
        context.DrawEllipse(SurfaceBrush, new Pen(OutlineBrush, 1), point, 6, 6);
    }
}
