using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The outer part of a parameter control: the plate or frame around its face (the content), with the label and the
/// value text. What it looks like is decided by its control theme, which a look replaces. The documentation of the
/// control's parameters (<see cref="Doc"/>) is for the label's tip.
/// </summary>
public class ControlChrome : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ControlChrome, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<ControlChrome, string?>(nameof(ValueText));

    /// <summary>The documentation of the control's parameters (their <c>midi.doc</c>); null when there is none.</summary>
    public static readonly StyledProperty<string?> DocProperty =
        AvaloniaProperty.Register<ControlChrome, string?>(nameof(Doc));

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

    public string? Doc
    {
        get => GetValue(DocProperty);
        set => SetValue(DocProperty, value);
    }
}
