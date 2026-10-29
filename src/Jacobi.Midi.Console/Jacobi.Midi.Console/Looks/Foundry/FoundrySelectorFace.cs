using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

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
