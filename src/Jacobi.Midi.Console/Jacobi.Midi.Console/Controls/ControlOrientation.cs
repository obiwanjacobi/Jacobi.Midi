using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The direction of a parameter control that has one (a slider): set by the dashboard from the ratio of its cells
/// (<c>GridLayout.IsWide</c>). The control is dragged along it and passes it to its face in the face state, which turns
/// its drawing. Vertical unless set.
/// </summary>
public static class ControlOrientation
{
    public static readonly AttachedProperty<Orientation> OrientationProperty =
        AvaloniaProperty.RegisterAttached<Control, Orientation>("Orientation", typeof(ControlOrientation), Orientation.Vertical);

    public static Orientation GetOrientation(Control control) => control.GetValue(OrientationProperty);

    public static void SetOrientation(Control control, Orientation value) => control.SetValue(OrientationProperty, value);
}
