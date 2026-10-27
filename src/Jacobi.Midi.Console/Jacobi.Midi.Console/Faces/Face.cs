using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// The inner part of a parameter control: draws the control's <see cref="FaceState"/> with the palette's brushes.
/// A face has no behaviour and never sees the device state, the schema or MIDI. Its brushes are set by its control
/// theme from the palette resources, so the colour cascade (control, layout control, device, look) is resource lookup.
/// </summary>
public abstract class Face : Control
{
    public static readonly StyledProperty<FaceState?> StateProperty =
        AvaloniaProperty.Register<Face, FaceState?>(nameof(State));

    /// <summary>The value colour: the device's accent, or the control's own colour.</summary>
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(AccentBrush));

    /// <summary>The colour of the range without value.</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(TrackBrush));

    /// <summary>The colour of the moving part (a knob's body, a fader's cap).</summary>
    public static readonly StyledProperty<IBrush?> BodyBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(BodyBrush));

    /// <summary>The colour of the marks on the moving part (a knob's pointer).</summary>
    public static readonly StyledProperty<IBrush?> PointerBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(PointerBrush));

    static Face()
    {
        AffectsRender<Face>(StateProperty, AccentBrushProperty, TrackBrushProperty, BodyBrushProperty, PointerBrushProperty);
    }

    public FaceState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? BodyBrush
    {
        get => GetValue(BodyBrushProperty);
        set => SetValue(BodyBrushProperty, value);
    }

    public IBrush? PointerBrush
    {
        get => GetValue(PointerBrushProperty);
        set => SetValue(PointerBrushProperty, value);
    }

    /// <summary>The size the face needs; the chrome and the layout adapt to it.</summary>
    protected abstract Size PreferredSize { get; }

    protected override Size MeasureOverride(Size availableSize) => PreferredSize;
}
