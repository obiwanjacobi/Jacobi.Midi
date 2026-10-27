using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Anodized knob: a ring of LEDs lit in the accent from the origin to the value, around a machined cap with a lit
/// pointer. An unknown value has all LEDs off, a faded cap and no pointer.
/// </summary>
public sealed class AnodizedKnobFace : Face
{
    private const int Leds = 11;

    protected override Size Cells => new(3, 3);

    private double Radius => Math.Min(Bounds.Width, Bounds.Height) / 2 - 5;
    private double Cap => Radius * 0.66;

    private Point LedAt(int led) => PointAt(Center, Radius, AngleOf((double)led / (Leds - 1)));

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState range)
            return;

        var off = new Pen(TrackBrush, 1);
        for (var led = 0; led < Leds; led++)
            context.DrawEllipse(DisplayBrush, off, LedAt(led), 2.4, 2.4);

        using (context.PushOpacity(range.Fraction is null ? 0.45 : 1))
        {
            context.DrawEllipse(BodyBrush, new Pen(OutlineBrush, 1), Center, Cap, Cap);
            context.DrawEllipse(SurfaceBrush, null, Center, Cap * 0.55, Cap * 0.55);
        }
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        var glow = AccentGlow;
        for (var led = 0; led < Leds; led++)
        {
            if (!IsLit((double)led / (Leds - 1), range.Origin, value))
                continue;
            if (glow is not null)
                context.DrawEllipse(glow, null, LedAt(led), 4.5, 4.5);
            context.DrawEllipse(AccentBrush, null, LedAt(led), 2.6, 2.6);
        }
        var angle = AngleOf(value);
        context.DrawLine(new Pen(AccentBrush, 3, lineCap: PenLineCap.Round), PointAt(Center, Cap * 0.62, angle), PointAt(Center, Cap * 0.9, angle));
    }
}

/// <summary>
/// Anodized fader: a recessed slot with a brushed cap and, next to it, an LED meter lit from the origin to the
/// value. An unknown value has no cap and all LEDs off.
/// </summary>
public sealed class AnodizedFaderFace : Face
{
    private const int Leds = 10;
    private const double CapHeight = 16;

    protected override Size Cells => new(2.5, 5);

    private double Top => CapHeight / 2;
    private double Bottom => Bounds.Height - CapHeight / 2;
    private double SlotX => Bounds.Width * 0.38;

    private Rect Segment(int led)
    {
        var pitch = (Bottom - Top) / Leds;
        return new Rect(Bounds.Width * 0.82 - 3, Bottom - (led + 1) * pitch + 1.5, 6, pitch - 3);
    }

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(SlotX - 4, Top, 8, Bottom - Top), 4, 4);
        var off = new Pen(TrackBrush, 1);
        for (var led = 0; led < Leds; led++)
            context.DrawRectangle(DisplayBrush, off, Segment(led));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        for (var led = 0; led < Leds; led++)
        {
            if (IsLit((led + 0.5) / Leds, range.Origin, value))
                context.DrawRectangle(AccentBrush, null, Segment(led));
        }

        var y = Bottom - (Bottom - Top) * value;
        var cap = new Rect(SlotX - 16, y - CapHeight / 2, 32, CapHeight);
        context.DrawRectangle(SurfaceBrush, new Pen(OutlineBrush, 1), cap, 3, 3);
        context.DrawLine(new Pen(AccentBrush, 2), new Point(cap.X + 4, y), new Point(cap.Right - 4, y));
    }
}

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

/// <summary>
/// Anodized number box: an OLED display with the number in the accent and ▲/▼ at the right. An unknown value shows a
/// dash.
/// </summary>
public sealed class AnodizedNumberBoxFace : Face
{
    private const double ArrowsWidth = 0.3;

    protected override Size Cells => new(4, 1.5);

    private Rect Display => new(Bounds.Size);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), Display, 3, 3);
        DrawStepArrows(context, Display, ArrowsWidth, TrackBrush, new Pen(TrackBrush, 1));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not TextFaceState text)
            return;

        var number = new Rect(0, 0, Bounds.Width * (1 - ArrowsWidth), Bounds.Height);
        using (context.PushOpacity(text.Text is null ? 0.4 : 1))
            DrawCenteredText(context, text.Text ?? "–", number, DisplayFont, AccentBrush, 18);
    }
}

/// <summary>
/// Anodized text: an OLED display with the text in the accent. An unknown value shows dashes.
/// </summary>
public sealed class AnodizedTextFace : Face
{
    protected override Size Cells => new(7, 1.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
        => context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not TextFaceState text)
            return;

        var display = new Rect(Bounds.Size);
        if (text.Text is null)
        {
            using (context.PushOpacity(0.4))
                DrawCenteredText(context, "– – –", display, DisplayFont, AccentBrush, 18);
            return;
        }
        DrawCenteredText(context, text.Text, display.Deflate(new Thickness(8, 0)), DisplayFont, AccentBrush, 18);
    }
}
