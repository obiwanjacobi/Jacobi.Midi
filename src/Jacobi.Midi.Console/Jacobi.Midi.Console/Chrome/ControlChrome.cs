using Avalonia;
using Avalonia.Controls;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The outer part of a parameter control: the plate or frame around its face (the content), with the label and the
/// value text. What it looks like is decided by its control theme, which a look replaces. The documentation of the
/// control's parameters (<see cref="Doc"/>) is for the label's tip.
/// <para>
/// The switches of the chrome's options (<see cref="FaceOptions.Chrome"/>, inherited from the control:
/// <see cref="FaceHost.OptionsProperty"/>) are pseudo-classes by their name, set when the part is on (<c>:plate</c>,
/// <c>:fasteners</c>), and its choices by name and value (<c>:label-below</c>, <c>:value-below</c>), so a look's
/// chrome theme shows, hides and places its parts with them. A value of a face's own declaration of an option (the
/// knob's <c>value</c> <c>inside</c>) sets none of the chrome's choices.
/// </para>
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

    public ControlChrome()
    {
        UpdateOptions();
    }

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FaceHost.OptionsProperty)
            UpdateOptions();
    }

    private void UpdateOptions()
    {
        var options = FaceOptions.Complete(FaceOptions.Chrome, FaceHost.GetOptions(this));
        foreach (var option in FaceOptions.Chrome)
        {
            if (option.Type == FaceOptionType.Switch)
            {
                PseudoClasses.Set($":{option.Name}", options.Switch(option.Name));
                continue;
            }
            foreach (var choice in option.Choices)
                PseudoClasses.Set($":{option.Name}-{choice}", options[option.Name] == choice);
        }
    }
}
