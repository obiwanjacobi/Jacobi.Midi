using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;

namespace Jacobi.Midi.SchemaEditor.ViewModels;

/// <summary>
/// The view model of the schema source viewer (shown as an overlay by <see cref="Views.SchemaSourceView"/>):
/// shows the serialized schema of a document (open or embedded in an assembly), read-only, in one of the <see cref="Formats"/>.
/// The text is serialized from the schema itself (<see cref="SchemaFileService.SerializeCanonical"/>),
/// not taken from the file or resource it was loaded from.
/// </summary>
internal sealed partial class SchemaSourceViewModel : ViewModelBase
{
    private readonly Action<SchemaSourceViewModel> _close;
    private readonly Dictionary<SchemaFormat, string> _texts = [];

    /// <param name="document">The document whose schema is shown.</param>
    /// <param name="close">Called with this view model when the user closes the viewer.</param>
    public SchemaSourceViewModel(EditDocument document, Action<SchemaSourceViewModel> close)
    {
        Document = document;
        _close = close;
        Title = document.Name;
    }

    /// <summary>
    /// The document whose schema is shown; the text is a snapshot of it, taken when a format is first shown.
    /// </summary>
    public EditDocument Document { get; }

    public string Title { get; }

    /// <summary>
    /// Raised when the viewer is asked to come to the front (it was opened again for the same document).
    /// </summary>
    public event EventHandler? ActivateRequested;

    /// <summary>
    /// Asks the view to bring this viewer to the front.
    /// </summary>
    public void Activate() => ActivateRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The formats the schema can be shown in (the tabs of the viewer).
    /// </summary>
    public IReadOnlyList<SchemaFormat> Formats { get; } = [SchemaFormat.Xml, SchemaFormat.Json];

    /// <summary>
    /// The names of the <see cref="Formats"/> to show (the tab headers), in the same order.
    /// </summary>
    public IReadOnlyList<string> FormatNames => Formats.Select(SchemaFileService.DisplayName).ToList();

    /// <summary>
    /// The index in <see cref="Formats"/> of the format the schema is shown in (the selected tab).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFormat), nameof(Text))]
    public partial int SelectedIndex { get; set; }

    /// <summary>
    /// The format the schema is shown in.
    /// </summary>
    public SchemaFormat SelectedFormat => Formats[Math.Clamp(SelectedIndex, 0, Formats.Count - 1)];

    /// <summary>
    /// The schema serialized in the <see cref="SelectedFormat"/>; serialized once per format.
    /// </summary>
    public string Text
    {
        get
        {
            if (!_texts.TryGetValue(SelectedFormat, out var text))
            {
                text = SchemaFileService.SerializeCanonical(Document, SelectedFormat);
                _texts[SelectedFormat] = text;
            }
            return text;
        }
    }

    [RelayCommand]
    private void Close() => _close(this);
}
