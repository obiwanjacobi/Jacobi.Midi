using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Document;

/// <summary>
/// The overlay view of the close document dialog; all behavior is in <see cref="CloseDocumentViewModel"/>.
/// </summary>
internal partial class CloseDocumentView : UserControl
{
    public CloseDocumentView()
    {
        InitializeComponent();
    }
}
