using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry text: an amber readout window. An unknown value shows dashes.
/// </summary>
public sealed class FoundryTextFace : Face
{
    protected override Size Cells => new(7, 1.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
        => context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not TextFaceState text)
            return;

        var window = new Rect(Bounds.Size);
        if (text.Text is null)
        {
            using (context.PushOpacity(0.4))
                DrawCenteredText(context, "– – –", window, DisplayFont, DisplayTextBrush, 18);
            return;
        }
        DrawCenteredText(context, text.Text, window.Deflate(new Thickness(8, 0)), DisplayFont, DisplayTextBrush, 18);
    }
}
