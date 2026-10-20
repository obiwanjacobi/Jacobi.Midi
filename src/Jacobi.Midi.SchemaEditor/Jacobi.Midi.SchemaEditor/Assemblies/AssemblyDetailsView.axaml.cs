using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// Read-only details of an opened assembly: what it provides and what could not be loaded.
/// </summary>
internal partial class AssemblyDetailsView : UserControl
{
    public AssemblyDetailsView()
    {
        InitializeComponent();
    }
}
