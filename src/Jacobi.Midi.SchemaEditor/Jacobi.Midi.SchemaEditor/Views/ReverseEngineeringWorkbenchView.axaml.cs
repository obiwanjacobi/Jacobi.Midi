using Avalonia.Controls;
using Avalonia.Interactivity;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Views;

public partial class ReverseEngineeringWorkbenchView : UserControl
{
    public ReverseEngineeringWorkbenchView()
    {
        InitializeComponent();
    }

    private void OnByteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index } && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectByte(index);
        }
    }
}