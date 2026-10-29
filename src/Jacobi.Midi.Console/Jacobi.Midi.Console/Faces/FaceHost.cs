using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Places the face for a control kind, taken from the kit of the current look (the <see cref="Kit.ResourceKey"/>
/// resource), and passes the control's face state on to it. Used in the templates of the parameter controls, so a
/// control does not know which face draws it. A missing kit or face is shown as a text, not hidden.
/// </summary>
public sealed class FaceHost : Decorator
{
    public static readonly StyledProperty<ParameterControlKind> KindProperty =
        AvaloniaProperty.Register<FaceHost, ParameterControlKind>(nameof(Kind));

    public static readonly StyledProperty<FaceState?> StateProperty =
        AvaloniaProperty.Register<FaceHost, FaceState?>(nameof(State));

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
