using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// Tree control that shows the opened assemblies with their embedded schemas and processor factories.
/// Binds to the <see cref="ViewModels.MainViewModel"/>: the items are its <see cref="AssemblyWorkspace"/>
/// and the selection is its <see cref="ViewModels.MainViewModel.SelectedAssemblyItem"/>.
/// </summary>
internal partial class AssemblyTreeControl : UserControl
{
    public AssemblyTreeControl()
    {
        InitializeComponent();
    }
}
