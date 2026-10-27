using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The knob parameter control: the value and the interaction (drag vertically, mouse wheel, arrow keys; with Shift
/// in fine steps). It computes the face state and the value text; its template places the chrome and the face.
/// For now the knob holds its value itself; with the device state it will read and write the parameter there.
/// </summary>
public class KnobControl : TemplatedControl
{
    // the drag distance in pixels that covers the whole range
    private const double DragRange = 200;
    private const double FineFactor = 10;

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<KnobControl, string?>(nameof(Label));

    public static readonly StyledProperty<decimal> MinimumProperty =
        AvaloniaProperty.Register<KnobControl, decimal>(nameof(Minimum));

    public static readonly StyledProperty<decimal> MaximumProperty =
        AvaloniaProperty.Register<KnobControl, decimal>(nameof(Maximum), 100);

    public static readonly StyledProperty<decimal> StepProperty =
        AvaloniaProperty.Register<KnobControl, decimal>(nameof(Step), 1);

    /// <summary>The logical value; null when it is not known.</summary>
    public static readonly StyledProperty<decimal?> ValueProperty =
        AvaloniaProperty.Register<KnobControl, decimal?>(nameof(Value));

    public static readonly DirectProperty<KnobControl, FaceState?> FaceStateProperty =
        AvaloniaProperty.RegisterDirect<KnobControl, FaceState?>(nameof(FaceState), knob => knob.FaceState);

    public static readonly DirectProperty<KnobControl, string> ValueTextProperty =
        AvaloniaProperty.RegisterDirect<KnobControl, string>(nameof(ValueText), knob => knob.ValueText);

    private FaceState? _faceState;
    private string _valueText = String.Empty;
    private double _dragStartY;
    private decimal _dragStartValue;

    public KnobControl()
    {
        Focusable = true;
        UpdateFace();
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public decimal Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public decimal Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public decimal Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public decimal? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>What the face draws.</summary>
    public FaceState? FaceState
    {
        get => _faceState;
        private set => SetAndRaise(FaceStateProperty, ref _faceState, value);
    }

    /// <summary>The value as shown: a sign for positive values of a bipolar range, a dash when unknown.</summary>
    public string ValueText
    {
        get => _valueText;
        private set => SetAndRaise(ValueTextProperty, ref _valueText, value);
    }

    // while Minimum and Maximum are set one after the other, the range may be inverted for a moment
    private ValueRange? Range => Maximum < Minimum ? null : new ValueRange(Minimum, Maximum);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty)
            UpdateFace();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Range is not { } range || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Pointer.Capture(this);
        _dragStartY = e.GetPosition(this).Y;
        _dragStartValue = Value ?? range.Clamp(0);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this || Range is not { } range)
            return;

        var distance = (_dragStartY - e.GetPosition(this).Y) / DragRange;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            distance /= FineFactor;
        Value = Snap(range, _dragStartValue + (range.Maximum - range.Minimum) * (decimal)distance);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Nudge(Math.Sign(e.Delta.Y), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var direction = e.Key switch
        {
            Key.Up or Key.Right => 1,
            Key.Down or Key.Left => -1,
            _ => 0,
        };
        if (direction == 0)
            return;

        Nudge(direction, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    // a step, or a tenth of the range in steps without Shift when the range is large
    private void Nudge(int direction, bool fine)
    {
        if (direction == 0 || Range is not { } range)
            return;

        var coarse = Math.Max(Step, Snap(range, (range.Maximum - range.Minimum) / 100) - range.Minimum);
        var amount = fine ? Step : coarse;
        Value = Snap(range, (Value ?? range.Clamp(0)) + direction * amount);
    }

    private decimal Snap(ValueRange range, decimal value)
        => range.Clamp(Step > 0 ? range.Minimum + Math.Round((value - range.Minimum) / Step) * Step : value);

    private void UpdateFace()
    {
        if (Range is not { } range)
            return;

        FaceState = range.ToFaceState(Value);
        ValueText = Value switch
        {
            null => "–",
            > 0 when range.IsBipolar => "+" + Value.Value.ToString("0.###", CultureInfo.CurrentCulture),
            { } value => value.ToString("0.###", CultureInfo.CurrentCulture),
        };
    }
}
