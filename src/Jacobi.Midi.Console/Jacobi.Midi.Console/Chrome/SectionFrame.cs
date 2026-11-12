using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The chrome of a section: the frame around a group of controls, with its title. What it looks like is decided by
/// its control theme, which a look replaces; the theme shows the documentation (<see cref="Doc"/>) in the tip of the
/// title (<see cref="DocTip"/>). A collapsible section collapses to its title (the top of its padding) and
/// expands again with a click on the title; the theme shows it with the <c>:collapsible</c> and <c>:collapsed</c>
/// pseudo-classes (e.g. a chevron, the content hidden).
/// </summary>
public class SectionFrame : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SectionFrame, string?>(nameof(Header));

    /// <summary>The documentation of the schema node the section is about (its <c>midi.doc</c>), shown in the title's tip.</summary>
    public static readonly StyledProperty<string?> DocProperty =
        AvaloniaProperty.Register<SectionFrame, string?>(nameof(Doc));

    public static readonly StyledProperty<bool> IsCollapsibleProperty =
        AvaloniaProperty.Register<SectionFrame, bool>(nameof(IsCollapsible));

    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<SectionFrame, bool>(nameof(IsCollapsed));

    // the height the frame was given while it is collapsed (NaN when it had none)
    private double? _expandedHeight;

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Doc
    {
        get => GetValue(DocProperty);
        set => SetValue(DocProperty, value);
    }

    /// <summary>Whether a click on the title collapses and expands the section.</summary>
    public bool IsCollapsible
    {
        get => GetValue(IsCollapsibleProperty);
        set => SetValue(IsCollapsibleProperty, value);
    }

    /// <summary>Whether only the title is shown: the frame is as high as the top and bottom of its padding.</summary>
    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCollapsibleProperty)
        {
            PseudoClasses.Set(":collapsible", IsCollapsible);
            if (!IsCollapsible)
                IsCollapsed = false;
        }
        else if (change.Property == IsCollapsedProperty)
        {
            PseudoClasses.Set(":collapsed", IsCollapsed);
            if (IsCollapsed)
            {
                _expandedHeight = Height;
                Height = Padding.Top + Padding.Bottom;
            }
            else if (_expandedHeight is { } height)
            {
                _expandedHeight = null;
                Height = height;
            }
        }
    }

    // a press on the title (the top of the padding) collapses or expands the section
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !IsCollapsible || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.GetPosition(this).Y >= Padding.Top)
            return;
        IsCollapsed = !IsCollapsed;
        e.Handled = true;
    }
}
