using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Jacobi.Midi.Console.Dashboards;

/// <summary>
/// Fits a parameter control (chrome and face) into the cells it is given: scaled uniformly by the side that limits it,
/// so the face keeps its shape, and stretched along the other side, so the chrome (the plate) fills the cells exactly
/// and lines up with the grid. The scale follows from the control's natural size when it is first laid out: a value
/// text that grows later ("9" to "10") does not scale the control again, which would move it under the pointer while it
/// is dragged.
/// </summary>
internal sealed class CellFit : Decorator
{
    // the natural size of the child, taken once
    private Size? _natural;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty)
            _natural = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_natural is null)
        {
            Child?.Measure(Size.Infinity);
            if (Child is { DesiredSize: { Width: > 0, Height: > 0 } desired })
                _natural = desired;
        }
        var natural = _natural ?? Child?.DesiredSize ?? default;
        var size = new Size(
            Double.IsInfinity(availableSize.Width) ? natural.Width : availableSize.Width,
            Double.IsInfinity(availableSize.Height) ? natural.Height : availableSize.Height);
        // measured again in the size it is shown at: a text wider than that (a long value) does not widen the chrome,
        // which would move the label and the face aside
        if (Child is { } child && natural.Width > 0 && natural.Height > 0)
        {
            var scale = Math.Min(size.Width / natural.Width, size.Height / natural.Height);
            child.Measure(new Size(size.Width / scale, size.Height / scale));
        }
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child || _natural is not { } natural)
            return finalSize;
        var scale = Math.Min(finalSize.Width / natural.Width, finalSize.Height / natural.Height);
        // laid out in the cells' size at the scale, then scaled from the top left
        child.Arrange(new Rect(0, 0, finalSize.Width / scale, finalSize.Height / scale));
        child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        child.RenderTransform = new ScaleTransform(scale, scale);
        return finalSize;
    }
}
