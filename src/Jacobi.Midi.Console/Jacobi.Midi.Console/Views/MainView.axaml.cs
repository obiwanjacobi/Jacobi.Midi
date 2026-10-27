using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Jacobi.Midi.Console.Dashboards;
using Jacobi.Midi.Console.Looks;
using Jacobi.Midi.Console.ViewModels;

namespace Jacobi.Midi.Console.Views;

/// <summary>
/// The main view of the console, shared by the desktop window and the browser host: the toolbar, the open dashboard
/// and the status. Builds the controls of the selected dashboard when it or the look changes.
/// </summary>
internal partial class MainView : UserControl
{
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelChanged;
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
        else if (e.PropertyName == nameof(MainViewModel.SelectedDashboard))
        {
            Rebuild();
        }
    }

    // the faces come from the kit of the look: a new look or session builds the controls again
    private void Rebuild()
        => DashboardHost.Content = _viewModel?.SelectedDashboard is { } session ? new DashboardBuilder(session).Build() : null;
}
