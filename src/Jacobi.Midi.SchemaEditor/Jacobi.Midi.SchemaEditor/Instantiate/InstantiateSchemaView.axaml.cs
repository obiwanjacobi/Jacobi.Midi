using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The overlay view of the instantiate dialog; all behavior is in <see cref="InstantiateSchemaViewModel"/>.
/// The view only tells the view model when its window is activated, so it can read the clipboard again.
/// </summary>
internal partial class InstantiateSchemaView : UserControl
{
    private Window? _window;

    public InstantiateSchemaView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // a desktop window: the clipboard may have changed in another application (the browser host has no window)
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is InstantiateSchemaViewModel viewModel)
        {
            await viewModel.RefreshClipboard();
        }
    }
}
