using Avalonia;
using Avalonia.Input;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Controls;

/// <summary>The switch: on or off, toggled by a click, Space or Enter.</summary>
public sealed class SwitchControl : ParameterControlBase
{
    /// <summary>On or off; null when it is not known.</summary>
    public static readonly StyledProperty<bool?> IsOnProperty =
        AvaloniaProperty.Register<SwitchControl, bool?>(nameof(IsOn));

    public SwitchControl()
    {
        UpdateFace();
    }

    public bool? IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    protected override void Refresh() => IsOn = Parameter?.Value as bool?;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsOnProperty)
            return;
        UpdateFace();
        if (IsOn is { } on)
            Write(on);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left || !IsEnabled)
            return;
        Toggle();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Space or Key.Enter) || !IsEnabled)
            return;
        Toggle();
        e.Handled = true;
    }

    private void Toggle() => IsOn = !(IsOn ?? false);

    private void UpdateFace()
    {
        FaceState = new SwitchFaceState(IsOn);
        ValueText = IsOn switch
        {
            true => "On",
            false => "Off",
            null => "–",
        };
    }
}
