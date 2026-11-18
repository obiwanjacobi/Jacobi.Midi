using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Anodized;

/// <summary>
/// Anodized knob: a ring of LEDs lit in the accent from the origin to the value, around a machined cap with a lit
/// pointer. An unknown value has all LEDs off, a faded cap and no pointer. Of the knob's options
/// (<see cref="FaceOptions.Knob"/>) it takes <c>track</c> (the unlit LEDs; an unknown value always shows them),
/// <c>body</c> (the cap), <c>pointer</c> and <c>value</c> (inside: the value text on the cap); its LEDs have no stroke to set.
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

        if (Options.Switch("track") || range.Fraction is null)
        {
            var off = new Pen(TrackBrush, 1);
            for (var led = 0; led < Leds; led++)
                context.DrawEllipse(DisplayBrush, off, LedAt(led), 2.4, 2.4);
        }

        if (!Options.Switch("body"))
            return;
        using (context.PushOpacity(range.Fraction is null ? 0.45 : 1))
        {
            context.DrawEllipse(BodyBrush, new Pen(OutlineBrush, 1), Center, Cap, Cap);
            context.DrawEllipse(SurfaceBrush, null, Center, Cap * 0.55, Cap * 0.55);
        }
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is RangeFaceState { Text: { } text })
            DrawValueInside(context, text, Center, Cap, DisplayFont, AccentDisplayBrush);
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
        if (!Options.Switch("pointer"))
            return;
        var angle = AngleOf(value);
        context.DrawLine(new Pen(AccentBrush, 3, lineCap: PenLineCap.Round), PointAt(Center, Cap * 0.62, angle), PointAt(Center, Cap * 0.9, angle));
    }
}
