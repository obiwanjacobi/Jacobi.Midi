using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The place of the label in the chrome of a control: it takes the width the chrome has, but asks for none of its own.
/// So the label never sets the size of the control nor of the face: the plate does, and a label longer than the plate
/// is cut off (its text trimmed); the full label is shown in a tip (<see cref="Tip"/>).
/// </summary>
internal sealed class LabelSlot : Decorator
{
    /// <summary>The full label, shown in a tip.</summary>
    public static readonly StyledProperty<string?> TipProperty =
        AvaloniaProperty.Register<LabelSlot, string?>(nameof(Tip));

    public string? Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TipProperty)
            return;
        // on one line: a plain text tip wraps, and with display scaling (e.g. 250%) its width can be rounded a fraction
        // below the text's width, which wraps the last letter onto a line the tip has no room for
        ToolTip.SetTip(this, String.IsNullOrEmpty(Tip) ? null : new TextBlock { Text = Tip, TextWrapping = TextWrapping.NoWrap });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
            return default;
        child.Measure(availableSize);
        return new Size(0, child.DesiredSize.Height);
    }
}
