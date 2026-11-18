using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// What the chrome of tabs and selector tabs shows besides their headers and pages: their label (the name of the tabs),
/// attached to the tab control by the dashboard, for a look's theme of tabs to show (with the headers at the left: at
/// the top of the header column; LCARS: in its upper elbow).
/// </summary>
internal static class PagesChrome
{
    /// <summary>The label of the tabs; null when they have none.</summary>
    public static readonly AttachedProperty<string?> LabelProperty =
        AvaloniaProperty.RegisterAttached<TabControl, string?>("Label", typeof(PagesChrome));

    public static string? GetLabel(TabControl tabs) => tabs.GetValue(LabelProperty);

    public static void SetLabel(TabControl tabs, string? value) => tabs.SetValue(LabelProperty, value);
}
