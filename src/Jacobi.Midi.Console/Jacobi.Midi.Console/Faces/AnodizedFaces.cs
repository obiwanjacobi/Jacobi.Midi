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

    protected override bool IsDirectional => true;

    protected override Size Cells => new(2.5, 5);

    private double Top => CapHeight / 2;
    private double Bottom => Extent.Height - CapHeight / 2;

    public override double TravelLength => Bottom - Top;
    private double SlotX => Extent.Width * 0.38;

    private Rect Segment(int led)
    {
        var pitch = (Bottom - Top) / Leds;
        return new Rect(Extent.Width * 0.82 - 3, Bottom - (led + 1) * pitch + 1.5, 6, pitch - 3);
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
            DrawCenteredText(context, text.Text ?? "–", number, DisplayFont, AccentDisplayBrush, 18);
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
                DrawCenteredText(context, "– – –", display, DisplayFont, AccentDisplayBrush, 18);
            return;
        }
        DrawCenteredText(context, text.Text, display.Deflate(new Thickness(8, 0)), DisplayFont, AccentDisplayBrush, 18);
    }
}

/// <summary>
/// Anodized selector: a row of keys with an LED above each option name, the LED and the name of the selected option lit in
/// the accent; with many options an OLED display with the selected option and ▲/▼ at the right. An unknown value has all
/// LEDs off, or a dash in the display.
/// </summary>
public sealed class AnodizedSelectorFace : Face
{
    private const double LedHeight = 4;

    protected override Size Cells
        => State is { } known && known is SelectorFaceState { IsList: false } selector
            ? new Size(Math.Max(1, selector.Options.Count) * 1.75, 1.5)
            : new Size(5, 1.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (state is not SelectorFaceState selector)
            return;
        var area = new Rect(Bounds.Size);
        if (selector.IsList)
        {
            context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), area, 3, 3);
            DrawStepArrows(context, area, ArrowsWidth, TrackBrush, new Pen(TrackBrush, 1));
            return;
        }
        var off = new Pen(TrackBrush, 1);
        for (var index = 0; index < selector.Options.Count; index++)
        {
            var key = Segment(area, selector.Options.Count, index);
            context.DrawRectangle(BodyBrush, new Pen(OutlineBrush, 1), key, 3, 3);
            context.DrawRectangle(DisplayBrush, off, Led(key), 1, 1);
        }
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not SelectorFaceState selector)
            return;
        var area = new Rect(Bounds.Size);
        if (selector.IsList)
        {
            var name = new Rect(0, 0, Bounds.Width * (1 - ArrowsWidth), Bounds.Height);
            var text = selector.Selected is { } selected && selected < selector.Options.Count ? selector.Options[selected] : null;
            using (context.PushOpacity(text is null ? 0.4 : 1))
                DrawFittedText(context, text ?? "–", name, DisplayFont, AccentDisplayBrush, 16);
            return;
        }
        for (var index = 0; index < selector.Options.Count; index++)
        {
            var key = Segment(area, selector.Options.Count, index);
            var isSelected = index == selector.Selected;
            if (isSelected)
            {
                if (AccentGlow is { } glow)
                    context.DrawRectangle(glow, null, Led(key).Inflate(3), 3, 3);
                context.DrawRectangle(AccentBrush, null, Led(key), 1, 1);
            }
            var label = new Rect(key.X, key.Y + LedHeight + 4, key.Width, key.Height - LedHeight - 4);
            using (context.PushOpacity(selector.Selected is null ? 0.5 : 1))
                DrawFittedText(context, selector.Options[index], label, DisplayFont, isSelected ? AccentDisplayBrush : TrackBrush, 11);
        }
    }

    private static Rect Led(Rect key) => new(key.X + key.Width * 0.25, key.Y + 4, key.Width * 0.5, LedHeight);
}

/// <summary>
/// Anodized XY pad: an OLED screen with a dotted grid, the point as a lit dot in the accent on crosshairs to its edges.
/// An unknown value has a dashed ring in the middle.
/// </summary>
public sealed class AnodizedXyPadFace : Face
{
    protected override Size Cells => new(4, 4);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);
        var area = PlotArea(Bounds.Size);
        var grid = new Pen(TrackBrush, 1, new DashStyle([1, 3], 0));
        for (var line = 1; line < 4; line++)
        {
            context.DrawLine(grid, PlotPoint(area, line / 4.0, 0), PlotPoint(area, line / 4.0, 1));
            context.DrawLine(grid, PlotPoint(area, 0, line / 4.0), PlotPoint(area, 1, line / 4.0));
        }
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not XyFaceState xy)
            return;
        var area = PlotArea(Bounds.Size);
        if (xy.X is not { } x || xy.Y is not { } y)
        {
            context.DrawEllipse(null, new Pen(TrackBrush, 1.5, new DashStyle([2, 2], 0)), area.Center, 7, 7);
            return;
        }
        var point = PlotPoint(area, x, y);
        using (context.PushOpacity(0.6))
        {
            var crosshair = new Pen(AccentBrush, 1);
            context.DrawLine(crosshair, new Point(point.X, area.Bottom), new Point(point.X, area.Top));
            context.DrawLine(crosshair, new Point(area.Left, point.Y), new Point(area.Right, point.Y));
        }
        if (AccentGlow is { } glow)
            context.DrawEllipse(glow, null, point, 10, 10);
        context.DrawEllipse(AccentBrush, null, point, 5, 5);
    }
}

/// <summary>
/// Anodized envelope: an OLED screen with the curve lit in the accent (with the finish's glow) and lit dots on the
/// breakpoints. An unknown time or level makes the curve dashed and unlit.
/// </summary>
public sealed class AnodizedEnvelopeFace : Face
{
    protected override Size Cells => new(8, 3.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);
        var area = PlotArea(Bounds.Size);
        context.DrawLine(new Pen(TrackBrush, 1, new DashStyle([1, 3], 0)), PlotPoint(area, 0, 0), PlotPoint(area, 1, 0));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not EnvelopeFaceState envelope || envelope.Breakpoints.Count == 0)
            return;
        var area = PlotArea(Bounds.Size);
        var points = EnvelopeShape.Points(envelope);
        var curve = EnvelopeCurve(area, points, closed: false);
        if (!envelope.Breakpoints.All(breakpoint => breakpoint.Time is not null && breakpoint.Level is not null))
        {
            context.DrawGeometry(null, new Pen(TrackBrush, 1.5, new DashStyle([3, 2], 0)), curve);
            return;
        }
        if (AccentGlow is { } glow)
            context.DrawGeometry(null, new Pen(glow, 6, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round), curve);
        context.DrawGeometry(null, new Pen(AccentBrush, 2, lineJoin: PenLineJoin.Round), curve);
        foreach (var point in points.Where(point => point.Segment is not null))
            context.DrawEllipse(AccentBrush, null, PlotPoint(area, point.X, point.Y), 3.5, 3.5);
    }
}
