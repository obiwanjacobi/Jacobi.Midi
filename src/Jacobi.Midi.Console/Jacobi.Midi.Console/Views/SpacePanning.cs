using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Jacobi.Midi.Console.Views;

/// <summary>
/// Pans a scroll viewer by dragging with the left button while Space is held (the convention of design tools).
/// Space counts only while the pointer is over the scroll viewer and no text box has the focus, so elsewhere it keeps
/// its usual meaning (typing, pressing a focused button). While panning, the press does not reach the content.
/// </summary>
internal sealed class SpacePanning
{
    private readonly ScrollViewer _viewer;
    private TopLevel? _topLevel;
    private bool _spaceHeld;
    private Cursor? _cursorBefore;

    // the pointer position and scroll offset when the pan started; null when not panning
    private (Point Start, Vector Offset)? _pan;

    public SpacePanning(ScrollViewer viewer)
    {
        _viewer = viewer;
        viewer.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => _pan = null);
    }

    /// <summary>
    /// Listens to the keys of the window (they reach it wherever the focus is); call when the view is attached.
    /// </summary>
    public void Attach(TopLevel topLevel)
    {
        _topLevel = topLevel;
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        if (topLevel is WindowBase window)
            window.Deactivated += OnDeactivated;
    }

    /// <summary>Stops listening to the keys of the window; call when the view is detached.</summary>
    public void Detach()
    {
        if (_topLevel is null)
            return;
        _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _topLevel.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        if (_topLevel is WindowBase window)
            window.Deactivated -= OnDeactivated;
        _topLevel = null;
        Release();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || (!_spaceHeld && !CanHold()))
            return;
        // handled: a focused button or check box is not pressed by the Space meant for panning (also its repeats)
        e.Handled = true;
        if (_spaceHeld)
            return;
        _spaceHeld = true;
        _cursorBefore = _viewer.Cursor;
        _viewer.Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_spaceHeld)
            return;
        e.Handled = true;
        Release();
    }

    // the window lost the focus: the key up of Space may never come
    private void OnDeactivated(object? sender, EventArgs e) => Release();

    private bool CanHold() =>
        _viewer.IsPointerOver && _topLevel?.FocusManager?.GetFocusedElement() is not TextBox;

    private void Release()
    {
        _spaceHeld = false;
        _pan = null;
        _viewer.Cursor = _cursorBefore;
        _cursorBefore = null;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_spaceHeld || !e.GetCurrentPoint(_viewer).Properties.IsLeftButtonPressed)
            return;
        _pan = (e.GetPosition(_viewer), _viewer.Offset);
        e.Pointer.Capture(_viewer);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pan is not { } pan)
            return;
        var moved = e.GetPosition(_viewer) - pan.Start;
        _viewer.Offset = new Vector(pan.Offset.X - moved.X, pan.Offset.Y - moved.Y);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pan is null)
            return;
        _pan = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
