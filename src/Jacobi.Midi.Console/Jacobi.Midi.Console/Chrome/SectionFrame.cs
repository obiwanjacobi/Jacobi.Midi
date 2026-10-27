using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The chrome of a section: the frame around a group of controls, with its title. What it looks like is decided by
/// its control theme, which a look replaces.
/// </summary>
public class SectionFrame : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SectionFrame, string?>(nameof(Header));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
