using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Anodized;

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
