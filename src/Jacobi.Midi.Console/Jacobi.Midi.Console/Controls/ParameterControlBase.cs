using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Core.Parameters;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The base of the parameter controls: follows an <see cref="IControlValue"/> (a device parameter or an index
/// variable) while the control is on screen, and provides the label, the value text and the face state its template
/// shows. A control without parameter shows its value as unknown. What every control does: Ctrl+click resets the
/// value to its default, a double-click opens an editor to type the value (for controls that can be typed into:
/// <see cref="EditText"/>). The derived control decides the rest of the interaction and the face state. In Edit mode
/// (<see cref="IsEditing"/>) none of it: the value is locked (<see cref="CanChange"/>), the cursor is the arrow.
/// A control with more than one slot (an XY pad, an envelope) follows the value of each (<see cref="SlotValues"/>).
/// </summary>
public abstract class ParameterControlBase : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ParameterControlBase, string?>(nameof(Label));

    /// <summary>The parameter the control shows and changes; null for an unbound control.</summary>
    public static readonly StyledProperty<IControlValue?> ParameterProperty =
        AvaloniaProperty.Register<ParameterControlBase, IControlValue?>(nameof(Parameter));

    /// <summary>The values of the bound slots of a control with more than one slot, by slot name; empty otherwise.</summary>
    public static readonly StyledProperty<IReadOnlyDictionary<string, IControlValue>> SlotValuesProperty =
        AvaloniaProperty.Register<ParameterControlBase, IReadOnlyDictionary<string, IControlValue>>(nameof(SlotValues),
            new Dictionary<string, IControlValue>());

    /// <summary>Edit mode: inherited from the dashboard (see <see cref="DashboardEditing"/>).</summary>
    public static readonly StyledProperty<bool> IsEditingProperty =
        DashboardEditing.IsEditingProperty.AddOwner<ParameterControlBase>();

    public static readonly DirectProperty<ParameterControlBase, FaceState?> FaceStateProperty =
        AvaloniaProperty.RegisterDirect<ParameterControlBase, FaceState?>(nameof(FaceState), control => control.FaceState);

    public static readonly DirectProperty<ParameterControlBase, string?> ValueTextProperty =
        AvaloniaProperty.RegisterDirect<ParameterControlBase, string?>(nameof(ValueText), control => control.ValueText);

    private static readonly Avalonia.Media.IBrush InvalidBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE0, 0x5A, 0x4F));

    private FaceState? _faceState;
    private string? _valueText;
    private IReadOnlyList<IControlValue> _subscribed = [];
    private bool _isOnScreen;

    protected ParameterControlBase()
    {
        Focusable = true;
        AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if (IsEditArea(e.GetPosition(this)) && OpenEditor())
                e.Handled = true;
        });
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IControlValue? Parameter
    {
        get => GetValue(ParameterProperty);
        set => SetValue(ParameterProperty, value);
    }

    public IReadOnlyDictionary<string, IControlValue> SlotValues
    {
        get => GetValue(SlotValuesProperty);
        set => SetValue(SlotValuesProperty, value);
    }

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    /// <summary>True when the user can change the value: the control is enabled and not in Edit mode.</summary>
    protected bool CanChange => IsEnabled && !IsEditing;

    /// <summary>What the face draws.</summary>
    public FaceState? FaceState
    {
        get => _faceState;
        protected set => SetAndRaise(FaceStateProperty, ref _faceState, value);
    }

    /// <summary>The value as text for the chrome; null when the face shows the value itself.</summary>
    public string? ValueText
    {
        get => _valueText;
        protected set => SetAndRaise(ValueTextProperty, ref _valueText, value);
    }

    /// <summary>True while the control takes over the parameter's value: the change is not written back.</summary>
    protected bool IsRefreshing { get; private set; }

    /// <summary>Takes over the value of <see cref="Parameter"/> (or unknown without one) and updates the face.</summary>
    protected abstract void Refresh();

    /// <summary>Writes a value the user set to the parameter.</summary>
    protected void Write(object value)
    {
        if (!IsRefreshing)
            Parameter?.Set(value);
    }

    /// <summary>The value of a slot as a fraction of its range (0-1); null when it is not bound, not known or has no range.</summary>
    protected double? FractionOf(string slot)
        => SlotValues.GetValueOrDefault(slot) is { Info.Range: { } range, Number: { } number } ? range.ToFraction(number) : null;

    /// <summary>Writes the value at a fraction of its range (0-1) to a bound slot, on a whole number of its steps.</summary>
    protected void WriteFraction(string slot, double fraction)
    {
        if (!IsRefreshing && SlotValues.GetValueOrDefault(slot) is { Info.Range: { } range } value)
            value.Set(range.FromFraction(fraction, value.Info.Step));
    }

    /// <summary>Sets the bound slots to their defaults (a reset of a control with more than one slot).</summary>
    protected void ResetSlots()
    {
        foreach (var value in SlotValues.Values)
        {
            if (value.Info.Default is { } known)
                value.Set(known);
        }
    }

    /// <summary>
    /// A position in the control in the coordinates of its face (which the chrome surrounds), with the size of the face;
    /// null when the control has no face (yet).
    /// </summary>
    protected (Point Position, Size Size)? FacePoint(Point position)
        => this.GetVisualDescendants().OfType<Faces.Face>().FirstOrDefault() is { } face && this.TranslatePoint(position, face) is { } point
            ? (point, face.Bounds.Size)
            : null;

    /// <summary>The value as the user types it; null when the control cannot be typed into.</summary>
    protected virtual string? EditText => null;

    /// <summary>Takes over a typed value; false when it is not valid (the editor stays open).</summary>
    protected virtual bool TryApplyEdit(string text) => false;

    /// <summary>True when a double-click at the position (in the control) opens the editor; e.g. not on step buttons.</summary>
    protected virtual bool IsEditArea(Point position) => true;

    /// <summary>Sets the parameter to its default (Ctrl+click); nothing when it has none.</summary>
    protected virtual void Reset()
    {
        if (Parameter?.Info.Default is { } value)
            Parameter.Set(value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (CanChange && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Reset();
            e.Handled = true;
        }
    }

    // a text box below the control: Enter applies the typed value, Escape (or clicking elsewhere) cancels
    private bool OpenEditor()
    {
        if (!CanChange || Parameter is null || EditText is not { } text)
            return false;

        var box = new TextBox { Text = text, MinWidth = 96 };
        var flyout = new Flyout { Content = box, Placement = PlacementMode.Bottom };
        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (TryApplyEdit(box.Text ?? String.Empty))
                        flyout.Hide();
                    else
                        box.BorderBrush = InvalidBrush;   // the value is not valid: the editor stays open
                    e.Handled = true;
                    break;
                case Key.Escape:
                    flyout.Hide();
                    e.Handled = true;
                    break;
                default:
                    box.ClearValue(BorderBrushProperty);
                    break;
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        flyout.ShowAt(this);
        return true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ParameterProperty || change.Property == SlotValuesProperty)
        {
            Subscribe(_isOnScreen);
            RefreshFromParameter();
        }
        else if (change.Property == IsEditingProperty)
        {
            // the theme's cursor (up-down, hand) says how to change the value: not in Edit mode
            if (IsEditing)
                SetValue(CursorProperty, new Cursor(StandardCursorType.Arrow));
            else
                ClearValue(CursorProperty);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isOnScreen = true;
        Subscribe(true);
        RefreshFromParameter();
    }

    // a control that is no longer shown (e.g. after a rebuild) does not follow its parameter
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isOnScreen = false;
        Subscribe(false);
    }

    // follows the parameter and the values of the slots while the control is on screen
    private void Subscribe(bool onScreen)
    {
        foreach (var value in _subscribed)
            value.Changed -= OnParameterChanged;
        _subscribed = onScreen ? [.. SlotValues.Values.Prepend(Parameter).OfType<IControlValue>().Distinct()] : [];
        foreach (var value in _subscribed)
            value.Changed += OnParameterChanged;
    }

    // the device state may change on another thread (the exchange)
    private void OnParameterChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshFromParameter();
        else
            Dispatcher.UIThread.Post(RefreshFromParameter);
    }

    private void RefreshFromParameter()
    {
        IsRefreshing = true;
        try
        {
            Refresh();
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
