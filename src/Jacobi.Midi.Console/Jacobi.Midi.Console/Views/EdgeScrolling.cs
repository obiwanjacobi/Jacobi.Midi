using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Jacobi.Midi.Console.Views;

/// <summary>
/// Scrolls a scroll viewer while a drag holds the pointer near (or beyond) one of its edges: the closer to the edge, the
/// faster. The drag reports the pointer with <see cref="Track"/>; after each scroll the drag is told where the pointer
/// is now (in the scroll viewer), so what is dragged follows the content that scrolled under the pointer.
/// </summary>
internal sealed class EdgeScrolling
{
    /// <summary>How far from an edge (in pixels) the scrolling starts.</summary>
    private const double Zone = 40;

    /// <summary>The most it scrolls per tick (in pixels): at the edge and beyond.</summary>
    private const double MaxStep = 20;

    private readonly ScrollViewer _viewer;
    private readonly Action<Point> _scrolled;
    private readonly DispatcherTimer _timer;
    private Point _pointer;

    public EdgeScrolling(ScrollViewer viewer, PointerEventArgs e, Action<Point> scrolled)
    {
        _viewer = viewer;
        _scrolled = scrolled;
        _pointer = e.GetPosition(viewer);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>The pointer moved during the drag.</summary>
    public void Track(PointerEventArgs e) => _pointer = e.GetPosition(_viewer);

    /// <summary>The drag ended.</summary>
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var step = new Vector(StepOf(_pointer.X, _viewer.Bounds.Width), StepOf(_pointer.Y, _viewer.Bounds.Height));
        if (step == default)
            return;
        var before = _viewer.Offset;
        _viewer.Offset = before + step;
        // at the end of the content it does not scroll further
        if (_viewer.Offset == before)
            return;
        _viewer.UpdateLayout();
        _scrolled(_pointer);
    }

    // the step along one axis: toward the edge the pointer is near, faster the closer it is
    private static double StepOf(double at, double length)
    {
        if (at < Zone)
            return -MaxStep * Math.Min(1, (Zone - at) / Zone);
        if (at > length - Zone)
            return MaxStep * Math.Min(1, (at - (length - Zone)) / Zone);
        return 0;
    }
}
