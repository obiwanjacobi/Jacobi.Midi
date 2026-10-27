using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Jacobi.Midi.Console.Dashboards;

/// <summary>
/// Fits a parameter control (chrome and face) into the cells it is given: scaled uniformly by the side that limits it,
/// so the face keeps its shape, and stretched along the other side, so the chrome (the plate) fills the cells exactly
/// and lines up with the grid.
/// </summary>
internal sealed class CellFit : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(Size.Infinity);
        return new Size(
            Double.IsInfinity(availableSize.Width) ? Child?.DesiredSize.Width ?? 0 : availableSize.Width,
            Double.IsInfinity(availableSize.Height) ? Child?.DesiredSize.Height ?? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child || child.DesiredSize.Width <= 0 || child.DesiredSize.Height <= 0)
            return finalSize;
        var natural = child.DesiredSize;
        var scale = Math.Min(finalSize.Width / natural.Width, finalSize.Height / natural.Height);
        // laid out in the cells' size at the scale, then scaled from the top left
        child.Arrange(new Rect(0, 0, finalSize.Width / scale, finalSize.Height / scale));
        child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        child.RenderTransform = new ScaleTransform(scale, scale);
        return finalSize;
    }
}
