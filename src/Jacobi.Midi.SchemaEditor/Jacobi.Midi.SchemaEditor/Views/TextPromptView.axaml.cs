using Avalonia.Threading;
using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Views;

internal partial class TextPromptView : UserControl
{
    public TextPromptView()
    {
        InitializeComponent();
    }

    // the prompt is shown to type in it: focus the text box once the overlay is visible (after the layout pass)
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Input.Focus();
                Input.SelectAll();
            }, DispatcherPriority.Loaded);
        }
    }
}
