using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// A switch as a pair of keys, ON and OFF side by side: the key of the value lit in the accent with its name in the
/// display color, the other in the body color with its name in the surface color. Of the switch's options
/// (<see cref="FaceOptions.Switch"/>) it takes <c>ends</c>: round outer ends make the pair a pill. An unknown value
/// lights neither key and fades both names.
/// </summary>
public sealed class PairSwitchFace : Face
{
    private const double Gap = 3;

    protected override Size Cells => new(3.5, 1.5);

    private bool IsRound => Options.Choice("ends") == "round";

    private RoundedRect KeyShape(bool isOn)
    {
        var width = (Bounds.Width - Gap) / 2;
        var key = new Rect(isOn ? 0 : width + Gap, 0, width, Bounds.Height);
        if (!IsRound)
            return new RoundedRect(key, 3);
        var radius = key.Height / 2;
        return isOn
            ? new RoundedRect(key, new CornerRadius(radius, 0, 0, radius))
            : new RoundedRect(key, new CornerRadius(0, radius, radius, 0));
    }

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not SwitchFaceState switchState)
            return;

        foreach (var isOn in new[] { true, false })
        {
            var shape = KeyShape(isOn);
            var isLit = switchState.IsOn == isOn;
            if (isLit && AccentGlow is { } glow)
                context.DrawRectangle(glow, null, shape.Inflate(2, 2));
            context.DrawRectangle(isLit ? AccentBrush : BodyBrush, null, shape);
            using (context.PushOpacity(switchState.IsOn is null ? 0.5 : 1))
                DrawTrimmedText(context, isOn ? "ON" : "OFF", shape.Rect, DisplayFont, isLit ? DisplayBrush : SurfaceBrush, 13);
        }
    }
}
