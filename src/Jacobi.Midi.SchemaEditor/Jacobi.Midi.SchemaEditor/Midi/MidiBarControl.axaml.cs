using Avalonia;
using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Midi;

/// <summary>
/// Bar that selects a MIDI endpoint and UMP group and starts/stops the MIDI connection.
/// Expects a <see cref="MidiBarViewModel"/> as DataContext; starts endpoint discovery when
/// attached and releases all MIDI resources when detached.
/// </summary>
internal partial class MidiBarControl : UserControl
{
    public MidiBarControl()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is MidiBarViewModel viewModel)
            _ = viewModel.Initialize();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is MidiBarViewModel viewModel)
            _ = viewModel.DisposeAsync().AsTask();
    }
}
