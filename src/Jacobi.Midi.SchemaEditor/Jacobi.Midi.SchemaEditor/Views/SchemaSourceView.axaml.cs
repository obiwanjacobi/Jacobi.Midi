using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Views;

/// <summary>
/// The overlay view of the schema source viewer: puts the text of the <see cref="SchemaSourceViewModel"/> in the
/// <see cref="AvaloniaEdit.TextEditor"/> (its text is not a bindable property) with the syntax coloring and folding
/// of the selected format (<see cref="SourceSyntax"/>).
/// </summary>
internal partial class SchemaSourceView : UserControl
{
    private SchemaSourceViewModel? _viewModel;
    private FoldingManager? _foldingManager;

    public SchemaSourceView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = DataContext as SchemaSourceViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        ShowText();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SchemaSourceViewModel.Text))
        {
            ShowText();
        }
    }

    private void ShowText()
    {
        if (_foldingManager is not null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        if (_viewModel is null)
        {
            Editor.Text = String.Empty;
            return;
        }

        try
        {
            Editor.SyntaxHighlighting = SourceSyntax.GetHighlighting(_viewModel.SelectedFormat, this);
            Editor.Text = _viewModel.Text;
        }
        catch (Exception exception)
        {
            Editor.SyntaxHighlighting = null;
            Editor.Text = $"The schema cannot be serialized as {_viewModel.SelectedFormat}: {exception.Message}";
            return;
        }
        Editor.ScrollToHome();
        _foldingManager = FoldingManager.Install(Editor.TextArea);
        SourceSyntax.UpdateFoldings(_viewModel.SelectedFormat, _foldingManager, Editor.Document);
        SpaceLeftMargins();
    }

    // the margins left of the text (line numbers, separator, folding) are packed tight by default;
    // the folding margin is added on every install, so this runs after it
    private void SpaceLeftMargins()
    {
        foreach (var margin in Editor.TextArea.LeftMargins)
        {
            margin.Margin = margin switch
            {
                LineNumberMargin => new Thickness(8, 0, 8, 0),
                FoldingMargin => new Thickness(4, 0, 8, 0),
                _ => margin.Margin
            };
        }
    }
}
