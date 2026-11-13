using Avalonia.Controls;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Views;

/// <summary>
/// The window of one <see cref="SchemaSourceViewModel"/> (Desktop host), opened and closed by the <see cref="MainView"/>.
/// Closing the window (title bar) closes the viewer; <see cref="SchemaSourceViewModel.Activate"/> brings it to the front.
/// </summary>
internal partial class SchemaSourceWindow : Window
{
    private readonly SchemaSourceViewModel? _viewModel;

    // for the XAML previewer
    public SchemaSourceWindow()
    {
        InitializeComponent();
    }

    public SchemaSourceWindow(SchemaSourceViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ActivateRequested += OnActivateRequested;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_viewModel is not null)
        {
            _viewModel.ActivateRequested -= OnActivateRequested;
            // closed from the title bar (or with the owner): the viewer is closed too; a second close is ignored
            _viewModel.CloseCommand.Execute(null);
        }
    }

    private void OnActivateRequested(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }
}
