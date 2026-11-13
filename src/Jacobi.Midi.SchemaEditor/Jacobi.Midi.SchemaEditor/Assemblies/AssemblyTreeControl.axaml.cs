using Avalonia;
using Avalonia.Controls;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.SchemaTree;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Jacobi.Midi.SchemaEditor.Views;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// Tree control that shows the opened assemblies with their embedded schemas and processor factories.
/// Binds to the <see cref="ViewModels.MainViewModel"/>: the items are its <see cref="AssemblyWorkspace"/>
/// and the selection is its <see cref="ViewModels.MainViewModel.SelectedAssemblyItem"/>.
/// A node of an embedded schema that is selected (e.g. by following a reference) is expanded to and scrolled into view.
/// </summary>
internal partial class AssemblyTreeControl : UserControl
{
    public AssemblyTreeControl()
    {
        InitializeComponent();
        Tree.PropertyChanged += OnTreePropertyChanged;
    }

    private void OnTreePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TreeView.SelectedItemProperty && e.NewValue is EditNode node
            && DataContext is MainViewModel { OpenedAssemblies: var assemblies } && assemblies.FindSchema(node) is { } schema)
        {
            TreeViewReveal.Reveal(Tree, GetPath(schema, node), node);
        }
    }

    /// <summary>
    /// The tree items from the assembly down to (excluding) <paramref name="node"/>: the assembly, the embedded schema,
    /// its <see cref="NodeDefsItem"/> for a node def (or a node in one) and the ancestors of the node.
    /// </summary>
    private static List<object> GetPath(LoadedSchema schema, EditNode node)
    {
        var ancestors = new List<object>();
        var top = node;
        for (var parent = node.Parent; parent is not null and not EditDocument; parent = parent.Parent)
        {
            ancestors.Insert(0, parent);
            top = parent;
        }
        var document = schema.Document.Schema;
        return document.NodeDefs.Contains(top)
            ? [schema.Assembly, schema, NodeDefsItem.Get(document), .. ancestors]
            : [schema.Assembly, schema, .. ancestors];
    }
}
