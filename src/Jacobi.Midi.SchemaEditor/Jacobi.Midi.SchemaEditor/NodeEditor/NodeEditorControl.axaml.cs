using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// Editor panel for the node selected in the schema tree.
/// Binds to the <see cref="ViewModels.MainViewModel"/> and shows a node-type specific
/// editor (field, group, repeat or choice) through data templates.
/// </summary>
internal partial class NodeEditorControl : UserControl
{
    public NodeEditorControl()
    {
        InitializeComponent();
    }
}
