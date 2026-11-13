using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Views;

namespace Jacobi.Midi.SchemaEditor.SchemaTree;

/// <summary>
/// Self-contained control that shows <see cref="Models.EditDocument"/>s as a tree:
/// each document as a root with a fixed <see cref="NodeDefsItem"/> (its node defs) and its members below it.
/// Assign <see cref="Documents"/> to show schemas (an observable collection is followed); <see cref="SelectedItem"/> reflects
/// (and can set) the selected item; a selected root is reported as the document itself.
/// An item that is not shown in the tree is kept by the host but not selected in the tree.
/// The context menu of a group executes <see cref="InstantiateCommand"/> or <see cref="MatchCommand"/> with that group as parameter;
/// the context menu of the node defs item executes <see cref="AddNodeDefCommand"/> with the node kind as parameter.
/// The arrange commands (<see cref="MoveUpCommand"/>, <see cref="MoveDownCommand"/>, <see cref="IndentCommand"/> and
/// <see cref="OutdentCommand"/>, executed without parameter on the selected node) are in the context menu of a node and on Alt+arrow keys;
/// <see cref="ChangeKindCommand"/> (with the node kind as parameter) is in the context menu of a group.
/// The context menu of a document root executes <see cref="ViewSourceCommand"/> with the document as parameter.
/// A right click selects the node before its context menu opens.
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

    public static readonly StyledProperty<ICommand?> MatchCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(MatchCommand));

    public static readonly StyledProperty<ICommand?> AddNodeDefCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(AddNodeDefCommand));

    public static readonly StyledProperty<ICommand?> MoveUpCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(MoveUpCommand));

    public static readonly StyledProperty<ICommand?> MoveDownCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(MoveDownCommand));

    public static readonly StyledProperty<ICommand?> IndentCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(IndentCommand));

    public static readonly StyledProperty<ICommand?> OutdentCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(OutdentCommand));

    public static readonly StyledProperty<ICommand?> ChangeKindCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(ChangeKindCommand));

    public static readonly StyledProperty<ICommand?> ViewSourceCommandProperty =
        AvaloniaProperty.Register<SchemaTreeControl, ICommand?>(nameof(ViewSourceCommand));

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
        // tunnel: the tree handles the arrow keys itself (navigation) before they bubble
        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// The command that moves the selected node up among its siblings (Alt+Up).
    /// </summary>
    public ICommand? MoveUpCommand
    {
        get => GetValue(MoveUpCommandProperty);
        set => SetValue(MoveUpCommandProperty, value);
    }

    /// <summary>
    /// The command that moves the selected node down among its siblings (Alt+Down).
    /// </summary>
    public ICommand? MoveDownCommand
    {
        get => GetValue(MoveDownCommandProperty);
        set => SetValue(MoveDownCommandProperty, value);
    }

    /// <summary>
    /// The command that moves the selected node into the group before it (Alt+Right).
    /// </summary>
    public ICommand? IndentCommand
    {
        get => GetValue(IndentCommandProperty);
        set => SetValue(IndentCommandProperty, value);
    }

    /// <summary>
    /// The command that moves the selected node out of its parent group (Alt+Left).
    /// </summary>
    public ICommand? OutdentCommand
    {
        get => GetValue(OutdentCommandProperty);
        set => SetValue(OutdentCommandProperty, value);
    }

    /// <summary>
    /// The command that changes the type of the selected group; its parameter is the <see cref="Jacobi.BinarySerializer.Schema.SchemaNodeKind"/>.
    /// </summary>
    public ICommand? ChangeKindCommand
    {
        get => GetValue(ChangeKindCommandProperty);
        set => SetValue(ChangeKindCommandProperty, value);
    }

    /// <summary>
    /// The command that shows the serialized schema of a document; executed with the <see cref="Models.EditDocument"/> as parameter.
    /// </summary>
    public ICommand? ViewSourceCommand
    {
        get => GetValue(ViewSourceCommandProperty);
        set => SetValue(ViewSourceCommandProperty, value);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt)
        {
            return;
        }
        var command = e.Key switch
        {
            Key.Up => MoveUpCommand,
            Key.Down => MoveDownCommand,
            Key.Right => IndentCommand,
            Key.Left => OutdentCommand,
            _ => null
        };
        if (command is null)
        {
            return;
        }
        if (command.CanExecute(null))
        {
            command.Execute(null);
            // the moved item gets a new container: keep the keyboard focus on it
            Dispatcher.UIThread.Post(FocusSelectedItem, DispatcherPriority.Loaded);
        }
        e.Handled = true;
    }

    private void FocusSelectedItem()
    {
        if (_viewModel.SelectedItem is not { } treeItem || _viewModel.GetPath(SchemaTreeViewModel.ToItem(treeItem)!) is not { } path)
        {
            return;
        }
        ItemsControl level = Tree;
        foreach (var ancestor in path)
        {
            if (level.ContainerFromItem(ancestor) is not TreeViewItem container)
            {
                return;
            }
            level = container;
        }
        (level.ContainerFromItem(treeItem) as TreeViewItem)?.Focus();
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

    /// <summary>
    /// The command of the Match item in the context menu of a group; its parameter is the <see cref="EditGroup"/>.
    /// </summary>
    public ICommand? MatchCommand
    {
        get => GetValue(MatchCommandProperty);
        set => SetValue(MatchCommandProperty, value);
    }

    /// <summary>
    /// The command of the add items in the context menu of the node defs item; its parameter is the <see cref="Jacobi.BinarySerializer.Schema.SchemaNodeKind"/>.
    /// The node defs item is selected (by the right click) before the menu opens.
    /// </summary>
    public ICommand? AddNodeDefCommand
    {
        get => GetValue(AddNodeDefCommandProperty);
        set => SetValue(AddNodeDefCommandProperty, value);
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
                // an item that is not in the tree is not selected in the tree
                object? treeItem = null;
                if (SelectedItem is { } item && _viewModel.GetPath(item) is { } path)
                {
                    treeItem = _viewModel.ToTreeItem(item);
                    ExpandTo(path, treeItem);
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
    /// <param name="item">The item below the path to bring into view; null to only expand.</param>
    private void ExpandTo(IReadOnlyList<object>? path, object? item = null)
    {
        if (path is not null)
        {
            TreeViewReveal.Reveal(Tree, path, item);
        }
    }
}
