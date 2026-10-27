using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Foundry knob: an engraved track with the value in the accent, a bakelite body with a brass cap and a pointer.
/// An unknown value has a dotted track, a faded body and no pointer.
/// </summary>
public sealed class FoundryKnobFace : Face
{
    protected override Size Cells => new(3, 3);

    private double Radius => Math.Min(Bounds.Width, Bounds.Height) / 2 - 4;

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState range)
            return;

        var center = Center;
        var outline = new Pen(OutlineBrush, 1);
        if (range.Fraction is null)
        {
            context.DrawGeometry(null, new Pen(TrackBrush, 3, new DashStyle([0.5, 2.5], 0), PenLineCap.Round),
                Arc(center, Radius, StartAngle, StartAngle + SweepAngle));
            using (context.PushOpacity(0.45))
                context.DrawEllipse(BodyBrush, outline, center, Radius * 0.72, Radius * 0.72);
            context.DrawEllipse(null, new Pen(SurfaceBrush, 1.5), center, Radius * 0.28, Radius * 0.28);
            return;
        }

        context.DrawGeometry(null, new Pen(TrackBrush, 5, lineCap: PenLineCap.Round), Arc(center, Radius, StartAngle, StartAngle + SweepAngle));
        context.DrawEllipse(BodyBrush, outline, center, Radius * 0.72, Radius * 0.72);
        context.DrawEllipse(SurfaceBrush, null, center, Radius * 0.28, Radius * 0.28);
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        var center = Center;
        DrawValueArc(context, new Pen(AccentBrush, 5, lineCap: PenLineCap.Round), center, Radius, range.Origin, value);
        var angle = AngleOf(value);
        context.DrawLine(new Pen(PointerBrush, 2.5, lineCap: PenLineCap.Round), PointAt(center, Radius * 0.34, angle), PointAt(center, Radius * 0.64, angle));
    }
}

/// <summary>
/// Foundry fader: a dark slot filled with the accent from the origin to the value, and a brass cap.
/// An unknown value has an empty slot and no cap.
/// </summary>
public sealed class FoundryFaderFace : Face
{
    private const double SlotWidth = 10;
    private const double CapWidth = 34;
    private const double CapHeight = 14;

    protected override Size Cells => new(2, 5);

    private double Top => CapHeight / 2;
    private double Bottom => Bounds.Height - CapHeight / 2;
    private Rect Slot => new((Bounds.Width - SlotWidth) / 2, Top, SlotWidth, Bottom - Top);

    protected override void RenderStatic(DrawingContext context, FaceState state)
        => context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), Slot, SlotWidth / 2, SlotWidth / 2);

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        double YOf(double fraction) => Bottom - (Bottom - Top) * fraction;
        var from = YOf(range.Origin);
        var to = YOf(value);
        context.DrawRectangle(AccentBrush, null, new Rect(Slot.X + 2, Math.Min(from, to), SlotWidth - 4, Math.Abs(to - from)), 2, 2);

        var cap = new Rect((Bounds.Width - CapWidth) / 2, to - CapHeight / 2, CapWidth, CapHeight);
        context.DrawRectangle(SurfaceBrush, new Pen(OutlineBrush, 1), cap, 2, 2);
        context.DrawLine(new Pen(PointerBrush, 2), new Point(cap.X + 4, to), new Point(cap.Right - 4, to));
    }
}

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

/// <summary>
/// Foundry number box: an amber readout with the number and ▲/▼ at the right. An unknown value shows a dash.
/// </summary>
public sealed class FoundryNumberBoxFace : Face
{
    private const double ArrowsWidth = 0.3;

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
