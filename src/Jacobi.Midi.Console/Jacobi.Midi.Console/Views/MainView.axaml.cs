using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Dashboards;
using Jacobi.Midi.Console.Looks;
using Jacobi.Midi.Console.ViewModels;

namespace Jacobi.Midi.Console.Views;

/// <summary>
/// The main view of the console, shared by the desktop window and the browser host: the toolbar, the open dashboard
/// and the status, and in Edit mode the rig and the palettes to drag from and the properties of the selected device or
/// control. Builds the controls of the selected dashboard when it, the mode, the look or the dashboard itself (an edit)
/// changes.
/// </summary>
internal partial class MainView : UserControl
{
    /// <summary>How far the pointer moves (in pixels) before a press on a drag source starts a drag.</summary>
    private const double DragDistance = 6;

    /// <summary>The narrowest the side panes of Edit mode can be made with their splitters.</summary>
    private const double MinPaneWidth = 180;

    // the widths of the side panes (the rig, the properties), kept while they are closed in Play mode
    private GridLength _rigWidth = new(300);
    private GridLength _propertiesWidth = new(320);

    private MainViewModel? _viewModel;
    private DashboardBuilder? _builder;
    private PendingDrag? _pendingDrag;

    public MainView()
    {
        InitializeComponent();
        // the drag starts when the pointer moved away from a pressed drag source, wherever it is now
        AddHandler(PointerMovedEvent, OnPointerMovedForDrag, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => _pendingDrag = null, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.DashboardChanged -= OnDashboardChanged;
        }
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.DashboardChanged += OnDashboardChanged;
        }
        UpdatePanes();
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        if (_viewModel is not null)
            _viewModel.StorageProvider = topLevel.StorageProvider;
        // on the window: a key reaches it wherever the focus is (also when nothing in the view has it)
        topLevel.AddHandler(KeyDownEvent, OnSelectionKeyDown, RoutingStrategies.Bubble);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, OnSelectionKeyDown);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedLook) && _viewModel is not null && Application.Current is { } application)
        {
            ConsoleLooks.Apply(application, _viewModel.SelectedLook);
            Rebuild();
        }
        else if (e.PropertyName is nameof(MainViewModel.SelectedDashboard) or nameof(MainViewModel.IsEditing))
        {
            UpdatePanes();
            Rebuild();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedControl))
        {
            _builder?.ShowSelection(_viewModel?.SelectedControl);
        }
    }

    private void OnDashboardChanged(object? sender, EventArgs e) => Rebuild();

    // a colour button of the control panel: the colour chooser of that area of the selected control
    private void OnColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ColorArea area } button && _viewModel?.SelectedControl is { } control)
            _builder?.OpenColorChooser(control, area, button);
    }

    // a colour button of the device panel: the colour chooser of that area of the device
    private void OnDeviceColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColorArea area } button || _viewModel?.DeviceProperties?.Device is not { } device)
            return;
        var current = RgbColor.TryParse(device.Colors[area], out var color) ? color.Value : PaletteBrushes.LookColorOf(area);
        ColorChooser.Create(_viewModel, null, device.Alias, area, current, device.Alias).ShowAt(button);
    }

    // a press on an item of the rig tree or a palette: it may become a drag
    private void OnDragSourcePressed(object? sender, PointerPressedEventArgs e)
    {
        _pendingDrag = null;
        if (sender is not Control { DataContext: var data } source || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
            return;
        DragItem? item = data switch
        {
            RigTreeItem { Source: { } dropSource } => new ParameterDragItem(dropSource),
            PaletteItem palette => new ControlDragItem(palette.Kind),
            _ => null,
        };
        if (item is not null)
            _pendingDrag = new PendingDrag(e, item, e.GetPosition(this));
    }

    private async void OnPointerMovedForDrag(object? sender, PointerEventArgs e)
    {
        if (_pendingDrag is not { } pending)
            return;
        var moved = e.GetPosition(this) - pending.Start;
        if (Math.Abs(moved.X) + Math.Abs(moved.Y) < DragDistance)
            return;
        _pendingDrag = null;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DragItem.Format, pending.Item));
        await DragDrop.DoDragDropAsync(pending.Press, transfer, DragDropEffects.Copy);
    }

    // Delete deletes the selected control, unless a field has the focus (there it deletes text)
    // Delete deletes the selected control, Esc selects nothing; not while a field has the focus (there they edit text)
    private void OnSelectionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _viewModel is not { IsEditing: true } viewModel || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;
        switch (e.Key)
        {
            case Key.Delete when viewModel.SelectedControl is not null:
                viewModel.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape when viewModel.SelectedControl is not null || viewModel.SelectedRigItem is not null:
                viewModel.ClearSelectionCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // a press beside the dashboard (the dashboard takes the presses on itself) selects nothing
    private void OnBesideDashboardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is { IsEditing: true } viewModel)
            viewModel.ClearSelectionCommand.Execute(null);
    }

    // the side panes are open in Edit mode, at the width they were given with the splitters, and closed in Play mode
    private void UpdatePanes()
    {
        var (rig, properties) = (Panes.ColumnDefinitions[0], Panes.ColumnDefinitions[4]);
        if (_viewModel?.IsEditing == true)
        {
            (rig.Width, properties.Width) = (_rigWidth, _propertiesWidth);
            rig.MinWidth = properties.MinWidth = MinPaneWidth;
            return;
        }
        if (rig.Width.Value > 0)
            (_rigWidth, _propertiesWidth) = (rig.Width, properties.Width);
        rig.MinWidth = properties.MinWidth = 0;
        rig.Width = properties.Width = new GridLength(0);
    }

    // the faces come from the kit of the look, the colours from the dashboard and the rig, the input from the mode:
    // a change builds the controls again
    private void Rebuild()
    {
        if (_viewModel?.SelectedDashboard is not { } session)
        {
            _builder = null;
            DashboardHost.Content = null;
            return;
        }
        var viewModel = _viewModel;
        _builder = new DashboardBuilder(session, viewModel, viewModel.IsEditing);
        DashboardHost.Content = _builder.Build();
        _builder.ShowSelection(viewModel.IsEditing ? viewModel.SelectedControl : null);
    }

    /// <summary>A press on a drag source that becomes a drag when the pointer moves far enough.</summary>
    private sealed record PendingDrag(PointerPressedEventArgs Press, DragItem Item, Point Start);
}
