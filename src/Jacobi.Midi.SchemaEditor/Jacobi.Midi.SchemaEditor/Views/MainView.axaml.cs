
using Avalonia;
using Avalonia.Controls;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Views;

internal partial class MainView : UserControl
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
}
