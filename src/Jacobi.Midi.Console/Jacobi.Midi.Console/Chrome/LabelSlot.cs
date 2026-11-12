using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The place of the label in the chrome of a control: it takes the width the chrome has, but asks for none of its own.
/// So the label never sets the size of the control (the natural size <c>CellFit</c> scales by) nor of the face: the
/// plate does, and a label longer than the plate is cut off (its text trimmed); the look shows it in full in a tip.
/// </summary>
internal sealed class LabelSlot : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
            return default;
        child.Measure(availableSize);
        return new Size(0, child.DesiredSize.Height);
    }
}
