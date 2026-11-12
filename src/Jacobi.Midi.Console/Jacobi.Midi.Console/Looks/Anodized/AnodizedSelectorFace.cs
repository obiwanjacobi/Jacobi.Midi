using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Anodized;

/// <summary>
/// Anodized selector: an OLED display with the selected option and ▲/▼ at the right; or keys with an LED above each
/// option name, the LED and the name of the selected option lit in the accent, in as many rows as the plate's width
/// needs. The display and the keys take the plate's width. An unknown value has a dash in the display, or all LEDs off.
/// </summary>
public sealed class AnodizedSelectorFace : Face
{
    private const double LedHeight = 4;

    protected override bool FillsWidth => true;

    protected override Size Cells
        => State is { } known && known is SelectorFaceState { IsList: false } selector
            ? new Size(Math.Max(1, selector.Options.Count) * KeyCells.Width, KeyCells.Height)
            : new Size(5, 1.5);

    protected override Size SizeIn(Size room)
        => State is { } known && known is SelectorFaceState { IsList: false } selector
            ? KeysSize(selector.Options.Count, room.Width)
            : base.SizeIn(room);

    protected override void RenderStatic(DrawingContext context, FaceState state)
    {
        if (state is not SelectorFaceState selector)
            return;
        if (selector.IsList)
        {
            var area = new Rect(Bounds.Size);
            context.DrawRectangle(DisplayBrush, new Pen(OutlineBrush, 1), area, 3, 3);
            DrawStepArrows(context, area, TrackBrush, new Pen(TrackBrush, 1));
            return;
        }
        var off = new Pen(TrackBrush, 1);
        for (var index = 0; index < selector.Options.Count; index++)
        {
            var key = Key(Bounds.Size, selector.Options.Count, index);
            context.DrawRectangle(BodyBrush, new Pen(OutlineBrush, 1), key, 3, 3);
            context.DrawRectangle(DisplayBrush, off, Led(key), 1, 1);
        }
    }

    protected override void RenderValue(DrawingContext context, FaceState state)
    {
        if (state is not SelectorFaceState selector)
            return;
        if (selector.IsList)
        {
            var name = StepValueArea(new Rect(Bounds.Size));
            var text = selector.Selected is { } selected && selected < selector.Options.Count ? selector.Options[selected] : null;
            using (context.PushOpacity(text is null ? 0.4 : 1))
                DrawTrimmedText(context, text ?? "–", name, DisplayFont, AccentDisplayBrush, 16);
            return;
        }
        for (var index = 0; index < selector.Options.Count; index++)
        {
            var key = Key(Bounds.Size, selector.Options.Count, index);
            var isSelected = index == selector.Selected;
            if (isSelected)
            {
                if (AccentGlow is { } glow)
                    context.DrawRectangle(glow, null, Led(key).Inflate(3), 3, 3);
                context.DrawRectangle(AccentBrush, null, Led(key), 1, 1);
            }
            var label = new Rect(key.X, key.Y + LedHeight + 4, key.Width, key.Height - LedHeight - 4);
            using (context.PushOpacity(selector.Selected is null ? 0.5 : 1))
                DrawTrimmedText(context, selector.Options[index], label, DisplayFont, isSelected ? AccentDisplayBrush : TrackBrush, 11);
        }
    }

    private static Rect Led(Rect key) => new(key.X + key.Width * 0.25, key.Y + 4, key.Width * 0.5, LedHeight);
}
