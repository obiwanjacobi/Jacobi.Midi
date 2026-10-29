using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry number box: an amber readout with the number and ▲/▼ at the right. An unknown value shows a dash.
/// </summary>
public sealed class FoundryNumberBoxFace : Face
{
    protected override Size Cells => new(4, 1.5);

    private Rect Window => new(Bounds.Size);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), Window, 3, 3);
        using (context.PushOpacity(0.75))
            DrawStepArrows(context, Window, ArrowsWidth, DisplayTextBrush, new Pen(OutlineBrush, 1));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not TextFaceState text)
            return;

        var number = new Rect(0, 0, Bounds.Width * (1 - ArrowsWidth), Bounds.Height);
        using (context.PushOpacity(text.Text is null ? 0.4 : 1))
            DrawCenteredText(context, text.Text ?? "–", number, DisplayFont, DisplayTextBrush, 18);
    }
}
