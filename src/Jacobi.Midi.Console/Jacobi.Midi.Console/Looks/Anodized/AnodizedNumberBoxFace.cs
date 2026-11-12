using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Anodized;

/// <summary>
/// Anodized number box: an OLED display with the number in the accent and ▲/▼ at the right. An unknown value shows a
/// dash. The display takes the plate's width.
/// </summary>
public sealed class AnodizedNumberBoxFace : Face
{
    protected override Size Cells => new(4, 1.5);

    protected override bool FillsWidth => true;

    private Rect Display => new(Bounds.Size);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), Display, 3, 3);
        DrawStepArrows(context, Display, TrackBrush, new Pen(TrackBrush, 1));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not TextFaceState text)
            return;

        var number = StepValueArea(Display);
        using (context.PushOpacity(text.Text is null ? 0.4 : 1))
            DrawCenteredText(context, text.Text ?? "–", number, DisplayFont, AccentDisplayBrush, 18);
    }
}
