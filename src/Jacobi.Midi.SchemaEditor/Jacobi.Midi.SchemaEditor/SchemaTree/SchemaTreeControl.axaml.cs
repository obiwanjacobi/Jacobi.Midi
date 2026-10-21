using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// Self-contained control that shows <see cref="Models.EditDocument"/>s as a tree:
/// each document as a root with its members below it.
/// Assign <see cref="Documents"/> to show schemas (an observable collection is followed); <see cref="SelectedItem"/> reflects
/// (and can set) the selected item; a selected root is reported as the document itself.
/// An item that is not shown in the tree (a node def) is kept by the host but not selected in the tree.
/// The context menu of a group executes <see cref="InstantiateCommand"/> with that group as parameter.
/// </summary>
internal partial class SchemaTreeControl : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<EditDocument>?> DocumentsProperty =
        AvaloniaProperty.Register<SchemaTreeControl, IReadOnlyList<EditDocument>?>(nameof(Documents));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SchemaTreeControl, object?>(nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> InstantiateCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(InstantiateCommand));

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

    /// <summary>
    /// The documents shown as the roots of the tree.
    /// </summary>
    public IReadOnlyList<EditDocument>? Documents
    {
        get => GetValue(DocumentsProperty);
        set => SetValue(DocumentsProperty, value);
    }

    /// <summary>
    /// The selected <see cref="EditNode"/>, or the <see cref="Models.EditDocument"/> when the root is selected.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// The command of the Instantiate item in the context menu of a group; its parameter is the <see cref="EditGroup"/>.
    /// </summary>
    public ICommand? InstantiateCommand
    {
        get => GetValue(InstantiateCommandProperty);
        set => SetValue(InstantiateCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldDocuments)
            {
                oldDocuments.CollectionChanged -= OnDocumentsCollectionChanged;
            }
            if (change.NewValue is INotifyCollectionChanged newDocuments)
            {
                newDocuments.CollectionChanged += OnDocumentsCollectionChanged;
            }
            SynchronizeDocuments();
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

    private void OnDocumentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SynchronizeDocuments();

    private void SynchronizeDocuments()
    {
        var added = _viewModel.SetDocuments(Documents ?? []);
        if (added.Count > 0)
        {
            // a new root shows its children
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var root in added)
                {
                    ExpandTo([root]);
                }
            }, DispatcherPriority.Loaded);
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
    /// Expands the tree items along <paramref name="path"/> (from a root down) so the item below it becomes visible.
    /// </summary>
    private void ExpandTo(IReadOnlyList<object>? path)
    {
        if (path is null)
        {
            return;
        }
        // a root item may just have been added
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
