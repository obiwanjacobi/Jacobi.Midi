using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// Self-contained control that shows the node tree of an <see cref="Models.EditDocument"/>.
/// Assign <see cref="EditDocument"/> to show a schema; <see cref="SelectedNode"/> reflects
/// (and can set) the selected node.
/// </summary>
internal partial class SchemaTreeControl : UserControl
{
    public static readonly StyledProperty<EditDocument?> EditDocumentProperty =
        AvaloniaProperty.Register<SchemaTreeControl, EditDocument?>(nameof(EditDocument));

    public static readonly StyledProperty<EditNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<SchemaTreeControl, EditNode?>(nameof(SelectedNode),
            defaultBindingMode: BindingMode.TwoWay);

    private readonly SchemaTreeViewModel _viewModel = new();

    public SchemaTreeControl()
    {
        InitializeComponent();
        // Only the tree gets the internal view model; the control itself keeps the inherited
        // DataContext so bindings on it (EditDocument, SelectedNode) resolve against the host.
        Tree.DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public EditDocument? EditDocument
    {
        get => GetValue(EditDocumentProperty);
        set => SetValue(EditDocumentProperty, value);
    }

    public EditNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditDocumentProperty)
        {
            _viewModel.Document = EditDocument;
        }
        else if (change.Property == SelectedNodeProperty && !ReferenceEquals(_viewModel.SelectedNode, SelectedNode))
        {
            ExpandTo(SelectedNode);
            _viewModel.SelectedNode = SelectedNode;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemaTreeViewModel.SelectedNode))
        {
            SelectedNode = _viewModel.SelectedNode;
        }
    }

    /// <summary>
    /// Expands the tree items of all ancestors of <paramref name="node"/> so it becomes visible.
    /// </summary>
    private void ExpandTo(EditNode? node)
    {
        var ancestors = new Stack<EditGroup>();
        for (var parent = node?.Parent; parent is not null and not Models.EditDocument; parent = parent.Parent)
        {
            ancestors.Push(parent);
        }

        ItemsControl level = Tree;
        foreach (var ancestor in ancestors)
        {
            if (level.ContainerFromItem(ancestor) is not TreeViewItem item)
            {
                return;
            }
            item.IsExpanded = true;
            // realize the child containers so the next level can be found
            item.UpdateLayout();
            level = item;
        }
    }
}
