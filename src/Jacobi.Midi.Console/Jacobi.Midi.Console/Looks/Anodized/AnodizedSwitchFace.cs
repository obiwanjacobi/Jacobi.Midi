using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Anodized;

/// <summary>
/// Anodized switch: an LED bar in the accent (off: dark) above a machined push button.
/// An unknown value has an outlined LED bar.
/// </summary>
public sealed class AnodizedSwitchFace : Face
{
    protected override Size Cells => new(2.5, 3);

    private Rect Led => new(Bounds.Width / 2 - 12, 4, 24, 5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        var button = new Rect(Bounds.Width / 2 - 22, 20, 44, 44);
        context.DrawRectangle(BodyBrush, new Pen(OutlineBrush, 1), button, 6, 6);
        context.DrawRectangle(SurfaceBrush, null, button.Deflate(10), 4, 4);
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not SwitchFaceState switchState)
            return;

        switch (switchState.IsOn)
        {
            case true:
                if (AccentGlow is { } glow)
                    context.DrawRectangle(glow, null, Led.Inflate(3), 4, 4);
                context.DrawRectangle(AccentBrush, null, Led, 2, 2);
                break;
            case false:
                context.DrawRectangle(DisplayBrush, new Pen(TrackBrush, 1), Led, 2, 2);
                break;
            default:
                context.DrawRectangle(null, new Pen(TrackBrush, 1, new DashStyle([2, 2], 0)), Led, 2, 2);
                break;
        }
    }
}
