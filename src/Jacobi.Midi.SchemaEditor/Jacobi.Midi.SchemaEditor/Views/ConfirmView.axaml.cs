using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Views;

/// <summary>
/// The overlay view of a confirmation dialog; all behavior is in <see cref="ViewModels.ConfirmViewModel"/>.
/// </summary>
internal partial class ConfirmView : UserControl
{
    public ConfirmView()
    {
        InitializeComponent();
    }
}
