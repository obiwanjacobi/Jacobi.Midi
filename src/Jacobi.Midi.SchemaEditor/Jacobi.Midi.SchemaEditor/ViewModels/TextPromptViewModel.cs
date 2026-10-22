using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Jacobi.Midi.SchemaEditor.ViewModels;

/// <summary>
/// The view model of a text prompt (shown as an overlay by <see cref="Views.TextPromptView"/>):
/// asks the user for a line or a block of text, e.g. a title or pasted hex bytes.
/// An optional validation keeps the prompt open and shows why the text is not accepted.
/// </summary>
internal sealed partial class TextPromptViewModel : ViewModelBase
{
    private readonly Func<string, string?>? _validate;
    private readonly Action<TextPromptViewModel, bool> _close;

    /// <param name="caption">The small caption above the title, e.g. "NEW SESSION".</param>
    /// <param name="title">The title: what the text is for.</param>
    /// <param name="message">An explanation below the title; empty for none.</param>
    /// <param name="confirmText">The text of the confirm button, e.g. "Create".</param>
    /// <param name="close">Called with this view model and whether the user confirmed (true) or cancelled (false).</param>
    /// <param name="validate">Returns why the text is not accepted, or null when it is; the prompt stays open on an error.</param>
    public TextPromptViewModel(string caption, string title, string message, string confirmText,
        Action<TextPromptViewModel, bool> close, Func<string, string?>? validate = null)
    {
        Caption = caption;
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        _close = close;
        _validate = validate;
    }

    public string Caption { get; }
    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }

    public bool HasMessage => Message.Length > 0;

    /// <summary>True for a block of text (several lines), false for a single line.</summary>
    public bool AcceptsReturn { get; init; }

    public string Placeholder { get; init; } = String.Empty;

    [ObservableProperty]
    public partial string Text { get; set; } = String.Empty;

    /// <summary>Why the text was not accepted; empty when there is no problem.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; private set; } = String.Empty;

    public bool HasError => Error.Length > 0;

    partial void OnTextChanged(string value) => Error = String.Empty;

    [RelayCommand]
    private void Confirm()
    {
        if (_validate?.Invoke(Text) is { } error)
        {
            Error = error;
            return;
        }
        _close(this, true);
    }

    [RelayCommand]
    private void Cancel() => _close(this, false);
}
