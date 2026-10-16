
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            viewModel.SetStorageProvider(topLevel.StorageProvider, topLevel.Clipboard);
        }
    }

    private void OnByteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index } && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectByte(index);
        }
    }
}