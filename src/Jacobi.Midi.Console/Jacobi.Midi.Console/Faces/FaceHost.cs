using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Places the face for a control kind, taken from the kit of the current look (the <see cref="Kit.ResourceKey"/>
/// resource), and passes the control's face state on to it. Used in the templates of the parameter controls, so a
/// control does not know which face draws it. A missing kit or face is shown as a text, not hidden.
/// <para>
/// The face is shown at the control's face size (<see cref="ScaleProperty"/>, inherited from the control), centred in
/// the room the chrome gives it: only the face is scaled, not the chrome (plate, label, value text). A face larger than
/// its room is cut off at its edges; a face that stretches (a fader) takes the room along its direction.
/// </para>
/// </summary>
public sealed class FaceHost : Decorator
{
    public static readonly StyledProperty<ParameterControlKind> KindProperty =
        AvaloniaProperty.Register<FaceHost, ParameterControlKind>(nameof(Kind));

    public static readonly StyledProperty<FaceState?> StateProperty =
        AvaloniaProperty.Register<FaceHost, FaceState?>(nameof(State));

    /// <summary>The face size: 1 shows the face at its own size. Set on a parameter control, inherited by its face host.</summary>
    public static readonly AttachedProperty<double> ScaleProperty =
        AvaloniaProperty.RegisterAttached<FaceHost, Control, double>("Scale", 1.0, inherits: true);

    // the size of the face, unscaled, as last measured
    private Size _natural;

    static FaceHost()
    {
        AffectsMeasure<FaceHost>(ScaleProperty);
    }

    public FaceHost()
    {
        ClipToBounds = true;
    }

    public static double GetScale(Control control) => control.GetValue(ScaleProperty);

    public static void SetScale(Control control, double value) => control.SetValue(ScaleProperty, value);

    private double Scale => GetScale(this) is > 0 and var scale ? scale : 1;

    public ParameterControlKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public FaceState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        PlaceFace();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty && ((ILogical)this).IsAttachedToLogicalTree)
            PlaceFace();
        else if (change.Property == StateProperty && Child is Face face)
            face.State = State;
    }

    // the face's own size: first its width (a selector's keys take one column then), then its height in the room's
    // width (the rows of keys), not less than it needs
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
            return default;
        var scale = Scale;
        child.Measure(Size.Infinity);
        var width = Double.IsInfinity(availableSize.Width) ? child.DesiredSize.Width : Math.Max(availableSize.Width / scale, child.DesiredSize.Width);
        child.Measure(new Size(width, Double.PositiveInfinity));
        _natural = child.DesiredSize;
        return new Size(_natural.Width * scale, _natural.Height * scale);
    }

    // centred at its size (stretched into the room along a stretching side), scaled from its top left; what does not fit
    // is cut off (ClipToBounds)
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child)
            return finalSize;
        var scale = Scale;
        var size = new Size(
            child.HorizontalAlignment == HorizontalAlignment.Stretch ? Math.Max(finalSize.Width / scale, _natural.Width) : _natural.Width,
            child.VerticalAlignment == VerticalAlignment.Stretch ? Math.Max(finalSize.Height / scale, _natural.Height) : _natural.Height);
        var at = new Point((finalSize.Width - size.Width * scale) / 2, (finalSize.Height - size.Height * scale) / 2);
        child.Arrange(new Rect(at, size));
        child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        child.RenderTransform = new ScaleTransform(scale, scale);
        return finalSize;
    }

    private void PlaceFace()
    {
        if (!this.TryFindResource(Kit.ResourceKey, out var resource) || resource is not Kit kit)
        {
            Child = new TextBlock { Text = $"No kit ('{Kit.ResourceKey}' resource)" };
            return;
        }
        if (kit.Create(Kind) is not { } face)
        {
            Child = new TextBlock { Text = $"No {Kind} face in kit '{kit.Name}'" };
            return;
        }
        face.State = State;
        Child = face;
    }
}
