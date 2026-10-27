using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The outer part of a parameter control: the plate or frame around its face (the content), with the label and the
/// value text. What it looks like is decided by its control theme, which a look replaces.
/// </summary>
public class ControlChrome : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ControlChrome, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<ControlChrome, string?>(nameof(ValueText));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }
}
