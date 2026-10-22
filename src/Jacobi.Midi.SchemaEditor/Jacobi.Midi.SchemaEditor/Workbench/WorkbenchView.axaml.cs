using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Workbench;

internal partial class WorkbenchView : UserControl
{
    public WorkbenchView()
    {
        InitializeComponent();
    }

    // the byte view asks the view model for the tooltip of a byte
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Bytes.DescribeByte = DataContext is WorkbenchViewModel viewModel ? viewModel.DescribeByte : null;
    }
}
