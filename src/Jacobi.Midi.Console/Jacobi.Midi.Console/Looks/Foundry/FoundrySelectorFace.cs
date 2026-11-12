using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;
using static Jacobi.Midi.Console.Faces.FaceDrawing;

namespace Jacobi.Midi.Console.Looks.Foundry;

/// <summary>
/// Foundry selector: an amber readout with the selected option and ▲/▼ at the right; or bakelite keys with the option
/// names engraved in brass, the selected key lit in the accent, in as many rows as the plate's width needs. The readout
/// and the keys take the plate's width. An unknown value has a dash in the readout, or no key lit.
/// </summary>
public sealed class FoundrySelectorFace : Face
{
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
        var outline = new Pen(OutlineBrush, 1);
        if (selector.IsList)
        {
            var area = new Rect(Bounds.Size);
            context.DrawRectangle(DisplayBrush, outline, area, 3, 3);
            using (context.PushOpacity(0.75))
                DrawStepArrows(context, area, DisplayTextBrush, outline);
            return;
        }
        for (var index = 0; index < selector.Options.Count; index++)
            context.DrawRectangle(BodyBrush, outline, Key(Bounds.Size, selector.Options.Count, index), 3, 3);
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
                DrawTrimmedText(context, text ?? "–", name, DisplayFont, DisplayTextBrush, 16);
            return;
        }
        for (var index = 0; index < selector.Options.Count; index++)
        {
            var key = Key(Bounds.Size, selector.Options.Count, index);
            var isSelected = index == selector.Selected;
            if (isSelected)
            {
                if (AccentGlow is { } glow)
                    context.DrawRectangle(glow, null, key.Inflate(2), 4, 4);
                context.DrawRectangle(AccentBrush, new Pen(OutlineBrush, 1), key, 3, 3);
            }
            using (context.PushOpacity(selector.Selected is null ? 0.5 : 1))
                DrawTrimmedText(context, selector.Options[index], key, DisplayFont, isSelected ? DisplayBrush : SurfaceBrush, 12);
        }
    }
}
