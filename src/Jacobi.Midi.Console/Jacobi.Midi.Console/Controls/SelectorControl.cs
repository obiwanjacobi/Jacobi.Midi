using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Jacobi.Midi.Console.Core.Faces;
using Jacobi.Midi.Console.Faces;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The selector: one of the options of a choice. A display with the selected option (<see cref="IsList"/>), where ▲/▼
/// step and a click on the option opens the list; or a key per option, where a click selects one. The mouse wheel and
/// the arrow keys step too.
/// </summary>
public sealed class SelectorControl : ParameterControlBase
{
    /// <summary>The number of the selected option; null when it is not known.</summary>
    public static readonly StyledProperty<int?> SelectedProperty =
        AvaloniaProperty.Register<SelectorControl, int?>(nameof(Selected));

    /// <summary>True to show the selected option in a display with a list (the default), false for a key per option.</summary>
    public static readonly StyledProperty<bool> IsListProperty =
        AvaloniaProperty.Register<SelectorControl, bool>(nameof(IsList), defaultValue: true);

    public SelectorControl()
    {
        UpdateFace();
    }

    public int? Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public bool IsList
    {
        get => GetValue(IsListProperty);
        set => SetValue(IsListProperty, value);
    }

    private IReadOnlyList<string> Options => Parameter?.Info.Options ?? [];

    protected override void Refresh()
    {
        Selected = Parameter?.Number is { } number ? (int)number : null;
        UpdateFace();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsListProperty)
            UpdateFace();
        if (change.Property != SelectedProperty)
            return;
        UpdateFace();
        if (Selected is { } selected)
            Write((decimal)selected);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left || !CanChange || e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || Options.Count == 0 || FacePoint(e.GetPosition(this)) is not (Point point, Size size))
        {
            return;
        }
        if (!IsList)
        {
            // between the keys nothing is selected
            if (FaceDrawing.KeyAt(size, Options.Count, point) is { } key)
                Selected = key;
        }
        else if (FaceDrawing.IsOnStepArrows(size, point))
            Step(point.Y < size.Height / 2 ? -1 : 1);
        else
            OpenList();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!CanChange)
            return;
        Step(-Math.Sign(e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var direction = e.Key switch
        {
            Key.Right or Key.Down => 1,
            Key.Left or Key.Up => -1,
            _ => 0,
        };
        if (direction == 0 || !CanChange)
            return;
        Step(direction);
        e.Handled = true;
    }

    // the next (+1) or previous (-1) option, within the options
    private void Step(int direction)
    {
        if (direction != 0 && Options.Count > 0)
            Selected = Math.Clamp((Selected ?? 0) + direction, 0, Options.Count - 1);
    }

    // the options in a list below the control; the selected one checked
    private void OpenList()
    {
        var flyout = new MenuFlyout();
        for (var index = 0; index < Options.Count; index++)
        {
            var option = index;
            var item = new MenuItem { Header = Options[index], ToggleType = MenuItemToggleType.Radio, IsChecked = index == Selected };
            item.Click += (_, _) => Selected = option;
            flyout.Items.Add(item);
        }
        flyout.ShowAt(this);
    }

    // keys show the option names and the chrome the selected one; a list shows it in its display
    private void UpdateFace()
    {
        FaceState = new SelectorFaceState(Options, Selected, IsList);
        ValueText = IsList ? null : Selected is { } selected && selected < Options.Count ? Options[selected] : "–";
    }
}
