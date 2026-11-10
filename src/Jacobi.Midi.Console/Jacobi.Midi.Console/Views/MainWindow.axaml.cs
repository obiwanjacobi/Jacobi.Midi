using Avalonia.Controls;
using Jacobi.Midi.Console.ViewModels;

namespace Jacobi.Midi.Console.Views;

/// <summary>
/// The desktop window of the console; hosts the <see cref="MainView"/>. Closing it asks the user to confirm discarding
/// the unsaved changes of the rig and the open dashboards (<see cref="MainViewModel.ConfirmQuit"/>).
/// </summary>
internal partial class MainWindow : Window
{
    // the user confirmed quitting: the next close goes through
    private bool _quitConfirmed;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // the OS shutting down does not wait for the user
        if (e.Cancel || _quitConfirmed || e.CloseReason == WindowCloseReason.OSShutdown || DataContext is not MainViewModel viewModel)
            return;
        // the confirmation is a dialog in the window: the close waits for it, and is done again when confirmed
        e.Cancel = true;
        _ = Quit(viewModel);
    }

    private async Task Quit(MainViewModel viewModel)
    {
        if (!await viewModel.ConfirmQuit())
            return;
        _quitConfirmed = true;
        Close();
    }
}
