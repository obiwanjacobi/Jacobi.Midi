using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Dashboards;
using Jacobi.Midi.Console.Looks;
using Jacobi.Midi.Console.ViewModels;

namespace Jacobi.Midi.Console.Views;

/// <summary>
/// The main view of the console, shared by the desktop window and the browser host: the toolbar, the open dashboard
/// and the status, and in Edit mode the properties of the selected control. Builds the controls of the selected
/// dashboard when it, the mode, the look or the dashboard itself (an edit) changes.
/// </summary>
internal partial class MainView : UserControl
{
    private MainViewModel? _viewModel;
    private DashboardBuilder? _builder;

    public MainView()
    {
        InitializeComponent();
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
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_viewModel is not null && TopLevel.GetTopLevel(this) is { } topLevel)
            _viewModel.StorageProvider = topLevel.StorageProvider;
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
            Rebuild();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedControl))
        {
            _builder?.ShowSelection(_viewModel?.SelectedControl);
        }
    }

    private void OnDashboardChanged(object? sender, EventArgs e) => Rebuild();

    // a colour button of the property panel: the colour chooser of that area of the selected control
    private void OnColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ColorArea area } button && _viewModel?.SelectedControl is { } control)
            _builder?.OpenColorChooser(control, area, button);
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
}
