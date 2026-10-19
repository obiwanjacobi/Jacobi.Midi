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
            _viewModel.Initialize(EditDocument);
        }
        else if (change.Property == SelectedNodeProperty
            && !ReferenceEquals(_viewModel.SelectedNode?.Node, SelectedNode))
        {
            _viewModel.Select(SelectedNode);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemaTreeViewModel.SelectedNode)
            && !ReferenceEquals(_viewModel.SelectedNode?.Node, SelectedNode))
        {
            SelectedNode = _viewModel.SelectedNode?.Node;
        }
    }
}
