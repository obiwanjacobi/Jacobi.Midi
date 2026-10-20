using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// Self-contained control that shows an <see cref="Models.EditDocument"/> as a tree:
/// the document as the root with its members below it.
/// Assign <see cref="EditDocument"/> to show a schema; <see cref="SelectedItem"/> reflects
/// (and can set) the selected item; the selected root is reported as the document itself.
/// An item that is not shown in the tree (a node def) is kept by the host but not selected in the tree.
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
    /// The selected <see cref="EditNode"/>, or the <see cref="Models.EditDocument"/> when the root is selected.
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
            // the root is replaced: show its children
            Dispatcher.UIThread.Post(() => ExpandTo([]), DispatcherPriority.Loaded);
        }
        else if (change.Property == SelectedItemProperty && !_synchronizing
            && !ReferenceEquals(SchemaTreeViewModel.ToItem(_viewModel.SelectedItem), SelectedItem))
        {
            _synchronizing = true;
            try
            {
                // an item that is not in the tree (a node def) is not selected in the tree
                object? treeItem = null;
                if (SelectedItem is { } item && _viewModel.GetPath(item) is { } path)
                {
                    ExpandTo(path);
                    treeItem = _viewModel.ToTreeItem(item);
                }
                _viewModel.SelectedItem = treeItem;
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
                SelectedItem = SchemaTreeViewModel.ToItem(_viewModel.SelectedItem);
            }
            finally
            {
                _synchronizing = false;
            }
        }
    }

    /// <summary>
    /// Expands the root and the tree items along <paramref name="path"/> (from the root down) so the item below it becomes visible.
    /// </summary>
    private void ExpandTo(IReadOnlyList<object>? path)
    {
        if (path is null)
        {
            return;
        }
        // the root item may just have been replaced
        Tree.UpdateLayout();
        ItemsControl level = Tree;
        // the root is always expanded
        if (level.ContainerFromIndex(0) is TreeViewItem rootItem)
        {
            rootItem.IsExpanded = true;
        }
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
