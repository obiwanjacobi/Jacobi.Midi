using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The XY pad: two numbers in a range (the <c>x</c> and <c>y</c> slots) as a point on a pad, set together by pressing
/// and dragging on it; the arrow keys move the point (with Shift in finer steps). A slot that is not bound is not
/// changed.
/// </summary>
public sealed class XyPadControl : ParameterControlBase
{
    // a move by an arrow key, as a fraction of the range
    private const double KeyStep = 0.01;
    private const double FineFactor = 10;

    public XyPadControl()
    {
        UpdateFace();
    }

    protected override void Refresh() => UpdateFace();

    protected override void Reset() => ResetSlots();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !CanChange || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Pointer.Capture(this);
        Focus();
        MoveTo(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this)
            return;
        MoveTo(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var (slot, direction) = e.Key switch
        {
            Key.Right => (SlotNames.X, 1),
            Key.Left => (SlotNames.X, -1),
            Key.Up => (SlotNames.Y, 1),
            Key.Down => (SlotNames.Y, -1),
            _ => (null, 0),
        };
        if (slot is null || !CanChange || FractionOf(slot) is not { } fraction)
            return;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? KeyStep / FineFactor : KeyStep;
        WriteFraction(slot, fraction + direction * step);
        e.Handled = true;
    }

    // the point under the pointer, in the plot of the face
    private void MoveTo(Point position)
    {
        if (FacePoint(position) is not (Point point, Size size))
            return;
        var (x, y) = FaceDrawing.PlotFractions(FaceDrawing.PlotArea(size), point);
        WriteFraction(SlotNames.X, x);
        WriteFraction(SlotNames.Y, y);
    }

    // the face shows the point; the chrome both values
    private void UpdateFace()
    {
        FaceState = new XyFaceState(FractionOf(SlotNames.X), FractionOf(SlotNames.Y));
        ValueText = $"{Format(SlotNames.X)} · {Format(SlotNames.Y)}";
    }

    private string Format(string slot)
        => SlotValues.GetValueOrDefault(slot) is { Number: { } number } value
            ? value.Info.OptionAt(number) ?? number.ToString(CultureInfo.CurrentCulture)
            : "–";
}
