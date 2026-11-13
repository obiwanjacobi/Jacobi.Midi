using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Views;

internal partial class MainView : UserControl
{
    private MainViewModel? _viewModel;
    // the windows of the schema source viewers (Desktop host only)
    private readonly Dictionary<SchemaSourceViewModel, SchemaSourceWindow> _sourceWindows = [];

    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            viewModel.SetStorageProvider(topLevel.StorageProvider, topLevel.Clipboard);

            // in a window (Desktop) the schema source viewers get windows of their own; otherwise (Browser) the overlay shows them
            if (topLevel is Window && _viewModel is null)
            {
                _viewModel = viewModel;
                (SchemaSourceOverlay.Parent as Panel)?.Children.Remove(SchemaSourceOverlay);
                viewModel.SchemaSources.CollectionChanged += OnSchemaSourcesChanged;
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.SchemaSources.CollectionChanged -= OnSchemaSourcesChanged;
            _viewModel = null;
        }
    }

    private void OnSchemaSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var source in e.OldItems?.OfType<SchemaSourceViewModel>() ?? [])
        {
            // closed with its Close button; a window closed from its title bar is already removed
            if (_sourceWindows.Remove(source, out var window))
            {
                window.Close();
            }
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        foreach (var source in e.NewItems?.OfType<SchemaSourceViewModel>() ?? [])
        {
            var window = new SchemaSourceWindow(source);
            window.Closed += (_, _) => _sourceWindows.Remove(source);
            _sourceWindows[source] = window;
            window.Show(owner);
        }
    }
}
