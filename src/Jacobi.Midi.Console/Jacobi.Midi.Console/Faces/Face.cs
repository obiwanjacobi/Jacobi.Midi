using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// The inner part of a parameter control: draws the control's <see cref="FaceState"/> with the palette's brushes,
/// the typography's display font and the finish's glow, all set by its control theme from resources (the same keys
/// in every look). A face has no behaviour and never sees the device state, the schema or MIDI.
/// <para>
/// A face draws on two layers: the static parts (<see cref="RenderStatic"/>: a track, a body, a slot) are only drawn
/// again when the brushes change or the value becomes known or unknown; the value (<see cref="RenderValue"/>: an arc, a
/// pointer, lit LEDs) is drawn again on every change of the value. Its size is given in cells (<see cref="Cells"/>);
/// the dashboard scales as a whole with its cell size.
/// </para>
/// <para>
/// A face with a direction (<see cref="IsDirectional"/>, a fader) is drawn upright, in its <see cref="Extent"/>; when its
/// state runs horizontally (<see cref="RangeFaceState.IsHorizontal"/>) the face turns its drawing a quarter clockwise
/// (the bottom of the upright drawing on the left) and takes its cells the other way round. A face takes its own size,
/// centred in the room its chrome gives it; a directional face stretches along its direction into all of it (a long
/// fader has a long slot).
/// </para>
/// </summary>
public abstract class Face : Control
{
    /// <summary>The size of a cell in pixels when a dashboard is not scaled: the unit of the faces' sizes.</summary>
    public const double CellPixels = 24;

    public static readonly StyledProperty<FaceState?> StateProperty =
        AvaloniaProperty.Register<Face, FaceState?>(nameof(State));

    /// <summary>The value colour: the device's accent, or the control's own colour.</summary>
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(AccentBrush));

    /// <summary>The accent for text and figures in displays: lighter than the accent where that is needed to read it.</summary>
    public static readonly StyledProperty<IBrush?> AccentDisplayBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(AccentDisplayBrush));

    /// <summary>The colour of the range without value.</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(TrackBrush));

    /// <summary>The material of the moving part (a knob's body, a switch's button).</summary>
    public static readonly StyledProperty<IBrush?> BodyBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(BodyBrush));

    /// <summary>The material of the parts on or next to the body (a knob's cap, a fader's cap, a lever).</summary>
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(SurfaceBrush));

    /// <summary>The colour of the marks on the moving part (a knob's pointer).</summary>
    public static readonly StyledProperty<IBrush?> PointerBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(PointerBrush));

    /// <summary>The background of displays and slots.</summary>
    public static readonly StyledProperty<IBrush?> DisplayBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(DisplayBrush));

    /// <summary>The text of displays.</summary>
    public static readonly StyledProperty<IBrush?> DisplayTextBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(DisplayTextBrush));

    /// <summary>The edges of bodies, slots and displays.</summary>
    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<Face, IBrush?>(nameof(OutlineBrush));

    /// <summary>Typography: the font of displays.</summary>
    public static readonly StyledProperty<FontFamily> DisplayFontProperty =
        AvaloniaProperty.Register<Face, FontFamily>(nameof(DisplayFont), FontFamily.Default);

    /// <summary>Finish: the opacity of the glow around lit parts (0-1); 0 draws no glow.</summary>
    public static readonly StyledProperty<double> GlowOpacityProperty =
        AvaloniaProperty.Register<Face, double>(nameof(GlowOpacity));

    private readonly FaceLayer _static;
    private readonly FaceLayer _value;

    protected Face()
    {
        _static = new FaceLayer(this, isStatic: true);
        _value = new FaceLayer(this, isStatic: false);
        VisualChildren.Add(_static);
        VisualChildren.Add(_value);
        UpdateAlignment();
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

    public IBrush? AccentDisplayBrush
    {
        get => GetValue(AccentDisplayBrushProperty);
        set => SetValue(AccentDisplayBrushProperty, value);
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

    public IBrush? SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public IBrush? PointerBrush
    {
        get => GetValue(PointerBrushProperty);
        set => SetValue(PointerBrushProperty, value);
    }

    public IBrush? DisplayBrush
    {
        get => GetValue(DisplayBrushProperty);
        set => SetValue(DisplayBrushProperty, value);
    }

    public IBrush? DisplayTextBrush
    {
        get => GetValue(DisplayTextBrushProperty);
        set => SetValue(DisplayTextBrushProperty, value);
    }

    public IBrush? OutlineBrush
    {
        get => GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    public FontFamily DisplayFont
    {
        get => GetValue(DisplayFontProperty);
        set => SetValue(DisplayFontProperty, value);
    }

    public double GlowOpacity
    {
        get => GetValue(GlowOpacityProperty);
        set => SetValue(GlowOpacityProperty, value);
    }

    /// <summary>The size the face needs, in cells, upright (it may follow the state); the chrome and the layout adapt to it.</summary>
    protected abstract Size Cells { get; }

    // the cells the face was last measured in: other cells (another state) measure it again and draw it all again
    private Size _measuredCells;

    // the size the face was last arranged in
    private Size _arrangedSize;

    private Size CellsAsDrawn => IsTurned ? new Size(Cells.Height, Cells.Width) : Cells;

    /// <summary>True for a face that runs in a direction: it is turned when its control is horizontal.</summary>
    protected virtual bool IsDirectional => false;

    /// <summary>True when the face is drawn turned: a directional face whose state runs horizontally.</summary>
    private bool IsTurned => IsDirectional && IsHorizontal(State);

    /// <summary>The size the face draws in, upright: its bounds, the other way round when it is turned.</summary>
    protected Size Extent => IsTurned ? new Size(Bounds.Height, Bounds.Width) : Bounds.Size;

    /// <summary>The centre of the face.</summary>
    protected Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    /// <summary>Draws what does not change with the value; <paramref name="state"/> tells whether the value is known.</summary>
    protected abstract void RenderStatic(DrawingContext context, FaceState state);

    /// <summary>Draws the value, on top of the static parts.</summary>
    protected abstract void RenderValue(DrawingContext context, FaceState state);

    /// <summary>The glow brush of the finish around a lit part in the accent; null without glow.</summary>
    protected IBrush? AccentGlow => GlowOpacity > 0 ? FaceDrawing.Glow(AccentBrush, GlowOpacity) : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        _measuredCells = CellsAsDrawn;
        var size = new Size(_measuredCells.Width * CellPixels, _measuredCells.Height * CellPixels);
        _static.Measure(size);
        _value.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(finalSize);
        _static.Arrange(bounds);
        _value.Arrange(bounds);
        // another size (a fader stretched into longer cells): both layers are drawn again for it
        if (finalSize != _arrangedSize)
        {
            _arrangedSize = finalSize;
            _static.InvalidateVisual();
            _value.InvalidateVisual();
        }
        return finalSize;
    }

    /// <summary>
    /// The distance the value travels over from its minimum to its maximum, in pixels of the face along its (upright)
    /// direction: by default its whole length; a fader without the ends its cap overhangs.
    /// </summary>
    public virtual double TravelLength => Extent.Height;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            _value.InvalidateVisual();
            var (old, now) = (change.GetOldValue<FaceState?>(), change.GetNewValue<FaceState?>());
            if (CellsAsDrawn != _measuredCells)
            {
                // turned the other way, or a state with another size (e.g. more options): measured and drawn again
                UpdateAlignment();
                InvalidateMeasure();
                _static.InvalidateVisual();
            }
            else if (IsKnown(old) != IsKnown(now))
            {
                _static.InvalidateVisual();
            }
        }
        else if (change.Property.OwnerType == typeof(Face))
        {
            // a brush, the font or the glow: everything is drawn with them
            _static.InvalidateVisual();
            _value.InvalidateVisual();
        }
    }

    // centred at its own size; a directional face stretched along its direction
    private void UpdateAlignment()
    {
        HorizontalAlignment = IsDirectional && IsTurned ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Center;
        VerticalAlignment = IsDirectional && !IsTurned ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Center;
    }

    private static bool IsHorizontal(FaceState? state) => state is { } known && known is RangeFaceState { IsHorizontal: true };

    private static bool IsKnown(FaceState? state)
        => state switch
        {
            null => false,
            { } known => known switch
            {
                RangeFaceState range => range.Fraction is not null,
                SwitchFaceState toggle => toggle.IsOn is not null,
                SelectorFaceState selector => selector.Selected is not null,
                TextFaceState text => text.Text is not null,
                XyFaceState xy => xy.X is not null && xy.Y is not null,
                EnvelopeFaceState envelope => envelope.Breakpoints.Any(breakpoint => breakpoint.Time is not null || breakpoint.Level is not null),
            },
        };

    // one layer of a face: draws the static parts or the value of its face
    private sealed class FaceLayer(Face face, bool isStatic) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (face.State is not { } state)
                return;
            // turned a quarter clockwise: the upright drawing's bottom on the left, its top on the right
            using var turned = face.IsTurned
                ? (DrawingContext.PushedState?)context.PushTransform(Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateTranslation(Bounds.Width, 0))
                : null;
            if (isStatic)
                face.RenderStatic(context, state);
            else
                face.RenderValue(context, state);
        }
    }
}
