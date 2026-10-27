using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// Whether a dashboard is in Edit mode: set on the root of the built dashboard and inherited by all controls in it.
/// In Edit mode the parameter controls do not change their values: a click selects them instead.
/// </summary>
public static class DashboardEditing
{
    public static readonly AttachedProperty<bool> IsEditingProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEditing", typeof(DashboardEditing), inherits: true);

    public static bool GetIsEditing(Control control) => control.GetValue(IsEditingProperty);

    public static void SetIsEditing(Control control, bool value) => control.SetValue(IsEditingProperty, value);
}
