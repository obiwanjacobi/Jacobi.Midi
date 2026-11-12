using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The place of the label in the chrome of a control: it takes the width the chrome has, but asks for none of its own.
/// So the label never sets the size of the control nor of the face: the plate does, and a label longer than the plate
/// is cut off (its text trimmed); the full label is shown in a tip (<see cref="Tip"/>), with the documentation of the
/// control's parameters below it (<see cref="Doc"/>, see <see cref="DocTip"/>).
/// </summary>
internal sealed class LabelSlot : Decorator
{
    /// <summary>The full label, shown in a tip.</summary>
    public static readonly StyledProperty<string?> TipProperty =
        AvaloniaProperty.Register<LabelSlot, string?>(nameof(Tip));

    /// <summary>The documentation of the control's parameters (their <c>midi.doc</c>), shown in the tip below the label.</summary>
    public static readonly StyledProperty<string?> DocProperty =
        AvaloniaProperty.Register<LabelSlot, string?>(nameof(Doc));

    public string? Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    public string? Doc
    {
        get => GetValue(DocProperty);
        set => SetValue(DocProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TipProperty || change.Property == DocProperty)
            ToolTip.SetTip(this, DocTip.Create(Tip, Doc));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
            return default;
        child.Measure(availableSize);
        return new Size(0, child.DesiredSize.Height);
    }
}
