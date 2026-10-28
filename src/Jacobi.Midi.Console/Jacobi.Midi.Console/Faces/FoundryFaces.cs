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

    protected override bool IsDirectional => true;

    protected override Size Cells => new(2, 5);

    private double Top => CapHeight / 2;
    private double Bottom => Extent.Height - CapHeight / 2;

    public override double TravelLength => Bottom - Top;
    private Rect Slot => new((Extent.Width - SlotWidth) / 2, Top, SlotWidth, Bottom - Top);

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

        var cap = new Rect((Extent.Width - CapWidth) / 2, to - CapHeight / 2, CapWidth, CapHeight);
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

/// <summary>
/// Foundry selector: a row of bakelite keys with the option names engraved in brass, the selected key lit in the accent;
/// with many options an amber readout with the selected option and ▲/▼ at the right. An unknown value has no key lit, or
/// a dash in the readout.
/// </summary>
public sealed class FoundrySelectorFace : Face
{
    protected override Size Cells
        => State is { } known && known is SelectorFaceState { IsList: false } selector
            ? new Size(Math.Max(1, selector.Options.Count) * 1.75, 1.5)
            : new Size(5, 1.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (state is not SelectorFaceState selector)
            return;
        var area = new Rect(Bounds.Size);
        var outline = new Pen(OutlineBrush, 1);
        if (selector.IsList)
        {
            context.DrawRectangle(DisplayBrush, outline, area, 3, 3);
            using (context.PushOpacity(0.75))
                DrawStepArrows(context, area, ArrowsWidth, DisplayTextBrush, outline);
            return;
        }
        for (var index = 0; index < selector.Options.Count; index++)
            context.DrawRectangle(BodyBrush, outline, Segment(area, selector.Options.Count, index), 3, 3);
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
                DrawFittedText(context, text ?? "–", name, DisplayFont, DisplayTextBrush, 16);
            return;
        }
        for (var index = 0; index < selector.Options.Count; index++)
        {
            var key = Segment(area, selector.Options.Count, index);
            var isSelected = index == selector.Selected;
            if (isSelected)
            {
                if (AccentGlow is { } glow)
                    context.DrawRectangle(glow, null, key.Inflate(2), 4, 4);
                context.DrawRectangle(AccentBrush, new Pen(OutlineBrush, 1), key, 3, 3);
            }
            using (context.PushOpacity(selector.Selected is null ? 0.5 : 1))
                DrawFittedText(context, selector.Options[index], key, DisplayFont, isSelected ? DisplayBrush : SurfaceBrush, 12);
        }
    }
}

/// <summary>
/// Foundry XY pad: an amber screen with an engraved grid, the point in the accent on crosshairs to its edges and a brass
/// bead on it. An unknown value has a dashed ring in the middle.
/// </summary>
public sealed class FoundryXyPadFace : Face
{
    protected override Size Cells => new(4, 4);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);
        var area = PlotArea(Bounds.Size);
        var grid = new Pen(TrackBrush, 1);
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
        var crosshair = new Pen(AccentBrush, 1.5);
        context.DrawLine(crosshair, new Point(point.X, area.Bottom), new Point(point.X, area.Top));
        context.DrawLine(crosshair, new Point(area.Left, point.Y), new Point(area.Right, point.Y));
        if (AccentGlow is { } glow)
            context.DrawEllipse(glow, null, point, 11, 11);
        context.DrawEllipse(SurfaceBrush, new Pen(OutlineBrush, 1), point, 6, 6);
    }
}

/// <summary>
/// Foundry envelope: an amber screen with the curve in the accent over a faint fill, and brass beads on the breakpoints.
/// An unknown time or level makes the curve dashed, without fill.
/// </summary>
public sealed class FoundryEnvelopeFace : Face
{
    protected override Size Cells => new(8, 3.5);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), new Rect(Bounds.Size), 3, 3);
        var area = PlotArea(Bounds.Size);
        context.DrawLine(new Pen(TrackBrush, 1), PlotPoint(area, 0, 0), PlotPoint(area, 1, 0));
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not EnvelopeFaceState envelope || envelope.Breakpoints.Count == 0)
            return;
        var area = PlotArea(Bounds.Size);
        var points = EnvelopeShape.Points(envelope);
        var isKnown = envelope.Breakpoints.All(breakpoint => breakpoint.Time is not null && breakpoint.Level is not null);
        if (isKnown)
        {
            using (context.PushOpacity(0.2))
                context.DrawGeometry(AccentBrush, null, EnvelopeCurve(area, points, closed: true));
        }
        var pen = isKnown ? new Pen(AccentBrush, 2, lineJoin: PenLineJoin.Round) : new Pen(TrackBrush, 1.5, new DashStyle([3, 2], 0));
        context.DrawGeometry(null, pen, EnvelopeCurve(area, points, closed: false));
        foreach (var point in points.Where(point => point.Segment is not null))
            context.DrawEllipse(SurfaceBrush, new Pen(OutlineBrush, 1), PlotPoint(area, point.X, point.Y), 4.5, 4.5);
    }
}
