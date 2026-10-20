using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// Self-contained control that shows an <see cref="Models.EditDocument"/> as a tree.
/// It has two views: the document members, and the document collections (includes, node defs, ...).
/// Assign <see cref="EditDocument"/> to show a schema; <see cref="SelectedItem"/> reflects
/// (and can set) the selected item. Setting it switches to the view that contains the item.
/// </summary>
internal partial class SchemaTreeControl : UserControl
{
    public static readonly StyledProperty<EditDocument?> EditDocumentProperty =
        AvaloniaProperty.Register<SchemaTreeControl, EditDocument?>(nameof(EditDocument));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SchemaTreeControl, object?>(nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    private readonly SchemaTreeViewModel _viewModel = new();
    // set while the selection is synchronized between the control and its view model
    private bool _synchronizing;

    public SchemaTreeControl()
    {
        InitializeComponent();
        // Only the content gets the internal view model; the control itself keeps the inherited
        // DataContext so bindings on it (EditDocument, SelectedItem) resolve against the host.
        Root.DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public EditDocument? EditDocument
    {
        get => GetValue(EditDocumentProperty);
        set => SetValue(EditDocumentProperty, value);
    }

    /// <summary>
    /// The selected <see cref="EditNode"/> or other document item. Category headers are reported as null.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditDocumentProperty)
        {
            _viewModel.Document = EditDocument;
        }
        else if (change.Property == SelectedItemProperty && !_synchronizing
            && !ReferenceEquals(_viewModel.SelectedItem, SelectedItem))
        {
            _synchronizing = true;
            try
            {
                if (SelectedItem is { } item)
                {
                    _viewModel.IsDocumentView = SchemaTreeViewModel.IsDocumentItem(item);
                    ExpandTo(_viewModel.GetPath(item));
                }
                _viewModel.SelectedItem = SelectedItem;
            }
            finally
            {
                _synchronizing = false;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemaTreeViewModel.SelectedItem) && !_synchronizing)
        {
            _synchronizing = true;
            try
            {
                SelectedItem = _viewModel.SelectedItem is SchemaTreeCategory ? null : _viewModel.SelectedItem;
            }
            finally
            {
                _synchronizing = false;
            }
        }
    }

    /// <summary>
    /// Expands the tree items along <paramref name="path"/> (from the root down) so the item below it becomes visible.
    /// </summary>
    private void ExpandTo(IReadOnlyList<object>? path)
    {
        if (path is null)
        {
            return;
        }
        // the root items may just have been replaced by a view switch
        Tree.UpdateLayout();
        ItemsControl level = Tree;
        foreach (var ancestor in path)
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
