using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Anodized;

/// <summary>
/// Anodized fader: a recessed slot with a brushed cap and, next to it, an LED meter lit from the origin to the
/// value. An unknown value has no cap and all LEDs off. Of the slider's options (<see cref="FaceOptions.Slider"/>) it
/// takes <c>track</c> (the slot and the unlit LEDs), <c>cap</c> and <c>segments</c> (the number of LEDs; 0: one
/// continuous bar); its slot and LEDs have a fixed width and shape.
/// </summary>
public sealed class AnodizedFaderFace : Face
{
    private const double CapHeight = 16;

    protected override bool IsDirectional => true;

    protected override Size Cells => new(2.5, 5);

    private double Top => CapHeight / 2;
    private double Bottom => Extent.Height - CapHeight / 2;
    private int Leds => (int)Options.Number("segments");

    public override double TravelLength => Bottom - Top;
    private double SlotX => Extent.Width * 0.38;
    private double MeterX => Extent.Width * 0.82 - 3;

    private Rect Segment(int led)
    {
        var pitch = (Bottom - Top) / Leds;
        return new Rect(MeterX, Bottom - (led + 1) * pitch + 1.5, 6, pitch - 3);
    }

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (!Options.Switch("track"))
            return;
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(SlotX - 4, Top, 8, Bottom - Top), 4, 4);
        var off = new Pen(TrackBrush, 1);
        if (Leds == 0)
        {
            context.DrawRectangle(DisplayBrush, off, new Rect(MeterX, Top, 6, Bottom - Top));
            return;
        }
        for (var led = 0; led < Leds; led++)
            context.DrawRectangle(DisplayBrush, off, Segment(led));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not RangeFaceState { Fraction: { } value } range)
            return;

        double YOf(double fraction) => Bottom - (Bottom - Top) * fraction;
        var y = YOf(value);
        if (Leds == 0)
        {
            var from = YOf(range.Origin);
            context.DrawRectangle(AccentBrush, null, new Rect(MeterX, Math.Min(from, y), 6, Math.Abs(y - from)));
        }
        for (var led = 0; led < Leds; led++)
        {
            if (IsLit((led + 0.5) / Leds, range.Origin, value))
                context.DrawRectangle(AccentBrush, null, Segment(led));
        }

        if (!Options.Switch("cap"))
            return;
        var cap = new Rect(SlotX - 16, y - CapHeight / 2, 32, CapHeight);
        context.DrawRectangle(SurfaceBrush, new Pen(OutlineBrush, 1), cap, 3, 3);
        context.DrawLine(new Pen(AccentBrush, 2), new Point(cap.X + 4, y), new Point(cap.Right - 4, y));
    }
}
