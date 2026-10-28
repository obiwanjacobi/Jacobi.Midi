using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The base of the controls of a number in a range (knob, fader): the range and step (from the parameter's data
/// type), the value and the interaction: drag vertically (along a horizontal control, see
/// <see cref="ControlOrientation"/>: to the right), mouse wheel, arrow keys; with Shift in fine steps.
/// </summary>
public abstract class RangeControl : ParameterControlBase
{
    // the drag distance in pixels that covers the whole range
    private const double DragRange = 200;
    private const double FineFactor = 10;
    // the distance in pixels before a press becomes a drag
    private const double DragThreshold = 3;

    public static readonly StyledProperty<decimal> MinimumProperty =
        AvaloniaProperty.Register<RangeControl, decimal>(nameof(Minimum));

    public static readonly StyledProperty<decimal> MaximumProperty =
        AvaloniaProperty.Register<RangeControl, decimal>(nameof(Maximum), 127);

    public static readonly StyledProperty<decimal> StepProperty =
        AvaloniaProperty.Register<RangeControl, decimal>(nameof(Step), 1);

    /// <summary>The logical value; null when it is not known.</summary>
    public static readonly StyledProperty<decimal?> ValueProperty =
        AvaloniaProperty.Register<RangeControl, decimal?>(nameof(Value));

    /// <summary>The text after the value (a display setting of the dashboard).</summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<RangeControl, string?>(nameof(Unit));

    /// <summary>The number of decimals shown; null for as many as the step has.</summary>
    public static readonly StyledProperty<int?> DecimalsProperty =
        AvaloniaProperty.Register<RangeControl, int?>(nameof(Decimals));

    private Point _dragStart;
    private decimal _dragStartValue;
    private bool _dragged;

    protected RangeControl()
    {
        UpdateFace();
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

    public string? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public int? Decimals
    {
        get => GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    private bool IsHorizontal => ControlOrientation.GetOrientation(this) == Orientation.Horizontal;

    // while Minimum and Maximum are set one after the other, the range may be inverted for a moment
    private ValueRange? Range => Maximum < Minimum ? null : new ValueRange(Minimum, Maximum);

    protected override void Refresh()
    {
        if (Parameter is { } parameter)
        {
            if (parameter.Info.Range is { } range)
            {
                Minimum = range.Minimum;
                Maximum = range.Maximum;
            }
            Step = parameter.Info.Step;
        }
        Value = Parameter?.Number;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty
            || change.Property == UnitProperty || change.Property == DecimalsProperty)
        {
            UpdateFace();
        }
        // the face runs and the pointer shows the direction to drag in; vertical is the theme's
        if (change.Property == ControlOrientation.OrientationProperty)
        {
            UpdateFace();
            if (IsHorizontal)
                Cursor = new Cursor(StandardCursorType.SizeWestEast);
            else
                ClearValue(CursorProperty);
        }
        if (change.Property == ValueProperty && Value is { } value)
            Write(value);
    }

    protected override string? EditText => Value?.ToString(CultureInfo.CurrentCulture) ?? String.Empty;

    protected override bool TryApplyEdit(string text)
    {
        if (Range is not { } range
            || !(Decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
                || Decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)))
        {
            return false;
        }
        Value = Snap(range, value);
        return true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || Range is not { } range || !CanChange || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Pointer.Capture(this);
        _dragStart = e.GetPosition(this);
        _dragStartValue = Value ?? range.Clamp(0);
        _dragged = false;
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this || Range is not { } range)
            return;

        // up, or to the right along a horizontal control, is more
        var position = e.GetPosition(this);
        var moved = IsHorizontal ? position.X - _dragStart.X : _dragStart.Y - position.Y;
        if (!_dragged && Math.Abs(moved) < DragThreshold)
            return;
        _dragged = true;
        var distance = moved / DragLength;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            distance /= FineFactor;
        Value = Snap(range, _dragStartValue + (range.Maximum - range.Minimum) * (decimal)distance);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured != this)
            return;
        e.Pointer.Capture(null);
        if (!_dragged)
            OnClick(e.GetPosition(this));
    }

    /// <summary>The drag distance (in pixels of the control) that covers the whole range.</summary>
    protected virtual double DragLength => DragRange;

    /// <summary>The control was clicked without dragging, at a position in the control.</summary>
    protected virtual void OnClick(Point position)
    {
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!CanChange)
            return;
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
        if (direction == 0 || !CanChange)
            return;

        Nudge(direction, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    /// <summary>Changes the value by a step (<paramref name="fine"/>) or about a hundredth of the range (at least a step).</summary>
    protected void Nudge(int direction, bool fine)
    {
        if (direction == 0 || Range is not { } range)
            return;

        var coarse = Math.Max(Step, Snap(range, range.Minimum + (range.Maximum - range.Minimum) / 100) - range.Minimum);
        Value = Snap(range, (Value ?? range.Clamp(0)) + direction * (fine ? Step : coarse));
    }

    private decimal Snap(ValueRange range, decimal value)
        => range.Clamp(Step > 0 ? range.Minimum + Math.Round((value - range.Minimum) / Step) * Step : value);

    /// <summary>The value as shown: with its decimals, a sign for a positive bipolar value and the unit; null when unknown.</summary>
    protected string? FormatValue()
    {
        if (Value is not { } value || Range is not { } range)
            return null;
        var decimals = Decimals ?? DecimalsOf(Step);
        var text = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
        if (value > 0 && range.IsBipolar)
            text = "+" + text;
        return Unit is { Length: > 0 } unit ? $"{text} {unit}" : text;
    }

    /// <summary>Updates the face state and the value text; a knob or fader draws the range, the chrome shows the value.</summary>
    protected virtual void UpdateFace()
    {
        if (Range is not { } range)
            return;
        FaceState = range.ToFaceState(Value) with { IsHorizontal = IsHorizontal };
        // a choice shows the name of its option
        ValueText = Parameter?.Info.OptionAt(Value) ?? FormatValue() ?? "–";
    }

    // the decimals of the step: 1 → 0, 0.5 → 1, 0.25 → 2
    private static int DecimalsOf(decimal step)
    {
        var decimals = 0;
        while (step != Math.Truncate(step) && decimals < 6)
        {
            step *= 10;
            decimals++;
        }
        return decimals;
    }
}

/// <summary>The knob: a number in a range, turned by dragging vertically.</summary>
public sealed class KnobControl : RangeControl
{
}

/// <summary>
/// The fader: a number in a range, as a slider; vertical, or horizontal when its cells are wider than tall. Its slot
/// takes all the length it is given, and dragging along the whole slot covers the whole range: a longer fader is finer.
/// </summary>
public sealed class FaderControl : RangeControl
{
    // the travel of the cap on the face: the pointer and the cap move together; at least a short drag
    protected override double DragLength
        => this.GetVisualDescendants().OfType<Faces.Face>().FirstOrDefault() is { } face
            ? Math.Max(40, face.TravelLength)
            : base.DragLength;
}

/// <summary>
/// The number box: a number shown in a display with ▲/▼, stepped by a click on the upper or lower half of the arrows
/// (the right of the control), or dragged, wheeled and typed like a knob. Bound to an index variable it selects the
/// patch.
/// </summary>
public sealed class NumberBoxControl : RangeControl
{
    protected override void OnClick(Point position)
    {
        if (ArrowAt(position) is { } direction)
            Nudge(direction, fine: true);
    }

    // a quick second click on the arrows is another step, not a request to type the value
    protected override bool IsEditArea(Point position) => ArrowAt(position) is null;

    // the arrows are in the face, which the chrome surrounds; +1 for ▲, -1 for ▼, null outside the arrows
    private int? ArrowAt(Point position)
    {
        if (FacePoint(position) is not (Point point, Size size) || point.X < size.Width * (1 - Faces.FaceDrawing.ArrowsWidth))
            return null;
        return point.Y < size.Height / 2 ? 1 : -1;
    }

    // the face shows the number (of a choice: the name of its option): the chrome has no value text
    protected override void UpdateFace()
    {
        FaceState = new TextFaceState(Parameter?.Info.OptionAt(Value) ?? FormatValue());
        ValueText = null;
    }
}
