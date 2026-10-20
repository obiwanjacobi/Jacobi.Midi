using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Document;

/// <summary>
/// The overlay view of the rename document dialog; all behavior is in <see cref="DocumentViewModel"/>.
/// </summary>
internal partial class DocumentView : UserControl
{
    public DocumentView()
    {
        InitializeComponent();
    }
}
