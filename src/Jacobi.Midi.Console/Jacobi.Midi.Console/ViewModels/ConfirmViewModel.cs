using CommunityToolkit.Mvvm.Input;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The view model of a confirmation dialog (shown as an overlay by <see cref="Views.ConfirmView"/>):
/// asks the user to confirm an action that cannot be undone, such as closing a dashboard with unsaved changes.
/// </summary>
internal sealed partial class ConfirmViewModel : ViewModelBase
{
    private readonly Action<bool> _close;

    /// <param name="caption">The small caption above the title, e.g. "UNSAVED CHANGES".</param>
    /// <param name="title">The title: what the action applies to.</param>
    /// <param name="message">The question to confirm.</param>
    /// <param name="confirmText">The text of the confirm button, e.g. "Close".</param>
    /// <param name="close">Called with whether the user confirmed (true) or cancelled (false).</param>
    public ConfirmViewModel(string caption, string title, string message, string confirmText, Action<bool> close)
    {
        Caption = caption;
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        _close = close;
    }

    public string Caption { get; }
    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }

    [RelayCommand]
    private void Confirm() => _close(true);

    [RelayCommand]
    private void Cancel() => _close(false);
}
