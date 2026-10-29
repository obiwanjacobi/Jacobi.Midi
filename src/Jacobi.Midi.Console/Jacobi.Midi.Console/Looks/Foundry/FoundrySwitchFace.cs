using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry switch: a pilot lamp in the accent (dark when off) above a lever in a slot (up is on).
/// An unknown value has an unlit, outlined lamp and the lever in the middle.
/// </summary>
public sealed class FoundrySwitchFace : Face
{
    protected override Size Cells => new(2, 3.5);

    private Point Lamp => new(Bounds.Width / 2, 12);
    private Rect Slot => new(Bounds.Width / 2 - 8, 30, 16, Bounds.Height - 34);

    protected override void RenderStatic(DrawingContext context, FaceState state)
        => context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), Slot, 8, 8);

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not SwitchFaceState switchState)
            return;

        switch (switchState.IsOn)
        {
            case true:
                if (AccentGlow is { } glow)
                    context.DrawEllipse(glow, null, Lamp, 12, 12);
                context.DrawEllipse(AccentBrush, new Pen(OutlineBrush, 1), Lamp, 8, 8);
                break;
            case false:
                context.DrawEllipse(BodyBrush, new Pen(OutlineBrush, 1), Lamp, 8, 8);
                break;
            default:
                context.DrawEllipse(null, new Pen(TrackBrush, 1.5), Lamp, 8, 8);
                break;
        }

        var lever = new Point(Slot.Center.X, switchState.IsOn switch
        {
            true => Slot.Top + 10,
            false => Slot.Bottom - 10,
            null => Slot.Center.Y,
        });
        if (switchState.IsOn is null)
            context.DrawEllipse(null, new Pen(SurfaceBrush, 1.5), lever, 10, 10);
        else
            context.DrawEllipse(SurfaceBrush, new Pen(OutlineBrush, 1), lever, 11, 11);
    }
}
