using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Core;
using Jacobi.Midi.Messages;
using Jacobi.Midi.Messages.V2;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// A session file picked by the user: its name and the platform handle to write it again (opaque to the workbench).
/// </summary>
internal sealed record SessionFile(string Name, object? Handle);

/// <summary>
/// The file access the workbench needs, provided by the host view model (it owns the platform storage provider).
/// Each function throws <see cref="InvalidOperationException"/> when the platform does not provide file access.
/// </summary>
internal sealed class WorkbenchStorage
{
    /// <summary>Lets the user pick a SysEx file; returns its name and contents, or null when the user cancelled.</summary>
    public required Func<Task<(string Name, byte[] Bytes)?>> OpenSysEx { get; init; }

    /// <summary>Lets the user pick a session file; returns it with its text, or null when the user cancelled.</summary>
    public required Func<Task<(SessionFile File, string Text)?>> OpenSession { get; init; }

    /// <summary>
    /// Writes the text to the session file; with a null file (or one without a handle) the user picks one, starting with the suggested name.
    /// Returns the written file, or null when the user cancelled.
    /// </summary>
    public required Func<SessionFile?, string, string, Task<SessionFile?>> SaveSession { get; init; }
}

/// <summary>
/// The view model of the reverse-engineering workbench (the Workbench mode of the main window).
/// It holds a workbench session: captures of a device dump, one of them the baseline, compared message slot by message slot.
/// The differences are drawn on the baseline by the byte view; the selected bytes are interpreted in every capture.
/// Captures come from a .syx file, pasted hex or are received from the MIDI connection of the MIDI bar.
/// </summary>
internal sealed partial class WorkbenchViewModel : ViewModelBase
{
    // a dump ends when no SysEx message arrives for this long
    private static readonly TimeSpan ReceiveIdleTime = TimeSpan.FromMilliseconds(500);

    private readonly WorkbenchStorage _storage;
    private readonly MidiBarViewModel _midi;
    private readonly WorkbenchSchemaHost _schemaHost;
    private SessionFile? _sessionFile;

    // receiving: the assembler is only used on the thread that raises the received packets
    private SysEx7Assembler? _assembler;
    private readonly List<byte[]> _received = [];
    private DispatcherTimer? _receiveTimer;

    public WorkbenchViewModel(WorkbenchStorage storage, MidiBarViewModel midi, WorkbenchSchemaHost schemaHost)
    {
        _storage = storage;
        _midi = midi;
        _schemaHost = schemaHost;
        _midi.PropertyChanged += OnMidiPropertyChanged;
        Captures.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCaptures));
        Field = new WorkbenchFieldViewModel(schemaHost, RefreshSchema, status => Status = status);
    }

    /// <summary>The field panel: defines, changes or removes the field of the selected bytes.</summary>
    public WorkbenchFieldViewModel Field { get; }

    // ---- display ----

    /// <summary>True to show the bytes (and offsets) in decimal, as most device displays show values; false for hex.</summary>
    [ObservableProperty]
    public partial bool ShowDecimal { get; set; }

    // ---- slot notes ----

    private Dictionary<int, string> _slotNotes = [];

    /// <summary>The notes of the user on the message slots, by slot index (replaced on every change).</summary>
    [ObservableProperty]
    public partial IReadOnlyDictionary<int, string> SlotNotes { get; private set; } = new Dictionary<int, string>();

    /// <summary>The slot the slot note is about: the slot of the selection, or the selected slot.</summary>
    public int? NoteSlot => Selection?.Slot ?? SelectedSlot?.Index;

    public bool HasNoteSlot => NoteSlot is not null;

    /// <summary>The caption of the slot note, e.g. 'SLOT 3 NOTE'.</summary>
    public string SlotNoteCaption => NoteSlot is { } slot ? $"SLOT {slot + 1} NOTE" : "SLOT NOTE";

    /// <summary>The note on the <see cref="NoteSlot"/>; setting it adds, changes or (when empty) removes it.</summary>
    public string SlotNoteText
    {
        get => NoteSlot is { } slot ? _slotNotes.GetValueOrDefault(slot) ?? String.Empty : String.Empty;
        set
        {
            if (NoteSlot is not { } slot || String.Equals(value, SlotNoteText, StringComparison.Ordinal))
            {
                return;
            }
            if (String.IsNullOrWhiteSpace(value))
            {
                _slotNotes.Remove(slot);
            }
            else
            {
                _slotNotes[slot] = value.Trim();
            }
            ApplySlotNotes();
            IsDirty = true;
            OnPropertyChanged();
        }
    }

    private void ApplySlotNotes()
    {
        SlotNotes = new Dictionary<int, string>(_slotNotes);
        foreach (var slot in Slots)
        {
            slot.Note = _slotNotes.GetValueOrDefault(slot.Index) ?? String.Empty;
        }
    }

    private void OnNoteSlotChanged()
    {
        OnPropertyChanged(nameof(NoteSlot));
        OnPropertyChanged(nameof(HasNoteSlot));
        OnPropertyChanged(nameof(SlotNoteCaption));
        OnPropertyChanged(nameof(SlotNoteText));
    }

    // ---- find a value ----

    private ValueQuery? _query;
    private int _matchIndex = -1;

    /// <summary>The value to find: decimal (70), hex (0x46, $46, 46h) or hex bytes (46 00).</summary>
    [ObservableProperty]
    public partial string FindText { get; set; } = String.Empty;

    /// <summary>Where the value was found; the matches in changed bytes first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches))]
    [NotifyCanExecuteChangedFor(nameof(NextMatchCommand), nameof(PreviousMatchCommand))]
    public partial IReadOnlyList<ValueMatch> Matches { get; private set; } = [];

    public bool HasMatches => Matches.Count > 0;

    /// <summary>The bytes of the matches, marked in the byte view.</summary>
    public IReadOnlyList<ByteRange> MatchRanges => Matches.Select(match => match.Range).ToList();

    /// <summary>The outcome of the search, e.g. '12 matches in reverb 40 (2 in changed bytes)'.</summary>
    [ObservableProperty]
    public partial string FindStatus { get; private set; } = String.Empty;

    /// <summary>
    /// Finds the value in the focused variant (where a value that was just set on the device is), or in the baseline when
    /// no variant is focused, and selects the first match.
    /// </summary>
    [RelayCommand]
    private void Find()
    {
        if (String.IsNullOrWhiteSpace(FindText))
        {
            ClearFind();
            return;
        }
        if (!ValueFinder.TryParse(FindText, out var query, out var error))
        {
            ClearFind();
            FindStatus = error!;
            return;
        }
        _query = query;
        RunFind();
        if (Matches.Count > 0)
        {
            ShowMatch(0);
        }
    }

    [RelayCommand]
    private void ClearFind()
    {
        _query = null;
        _matchIndex = -1;
        Matches = [];
        OnPropertyChanged(nameof(MatchRanges));
        FindStatus = String.Empty;
    }

    // (again) after the captures or the focus changed
    private void RunFind()
    {
        if (_query is null)
        {
            return;
        }
        var capture = FocusedVariant ?? Baseline;
        Matches = capture is null ? [] : ValueFinder.Find(capture, Baseline, _query);
        OnPropertyChanged(nameof(MatchRanges));
        _matchIndex = -1;
        var changed = Matches.Count(match => match.IsChanged);
        FindStatus = capture is null ? "There is no capture to search."
            : Matches.Count == 0 ? $"{_query.Text} is not found in {GetColumnName(capture)}."
            : $"{Matches.Count} match(es) of {_query.Text} in {GetColumnName(capture)}{(changed > 0 ? $", {changed} in changed bytes" : String.Empty)}.";
    }

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void NextMatch() => ShowMatch((_matchIndex + 1) % Matches.Count);

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void PreviousMatch() => ShowMatch((_matchIndex - 1 + Matches.Count) % Matches.Count);

    private void ShowMatch(int index)
    {
        _matchIndex = index;
        var match = Matches[index];
        Selection = match.Range;
        FindStatus = $"Match {index + 1} of {Matches.Count}: {match.Range} as {match.Interpretation}{(match.IsChanged ? " (changed)" : String.Empty)}.";
    }

    /// <summary>The layout of the root of each slot in the target schema; null for a slot without a root.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MessageLayout?> Layouts { get; private set; } = [];

    /// <summary>The state of the target schema, e.g. whether it is open.</summary>
    [ObservableProperty]
    public partial string SchemaText { get; private set; } = String.Empty;

    /// <summary>
    /// Finds the target schema (the open document named after the session) again and lays out its message roots on the baseline.
    /// Called after the schema was changed here, and by the host when the workbench is shown (it may have been edited in the meantime).
    /// </summary>
    public void RefreshSchema()
    {
        var document = HasSession ? _schemaHost.FindDocument(Title) : null;
        Layouts = document is null
            ? []
            : Slots.Select(slot => WorkbenchSchema.FindRoot(document.Schema, slot.Index) is { } root
                ? WorkbenchSchema.GetLayout(root, slot.Bytes)
                : null).ToList();
        SchemaText = !HasSession ? String.Empty
            : document is null ? $"Schema {Title} is not open: the first field creates it. To continue an existing schema, open it in Schema mode."
            : $"Schema {document.DisplayName}: {Layouts.Count(layout => layout is not null)} of {Slots.Count} slot(s) described.";
        UpdateField();
    }

    private void UpdateField()
    {
        Field.Update(Title, Selection, Slots.Select(slot => slot.Bytes).ToList(), Layouts,
            Baseline is null ? [] : ComparedVariants.Prepend(Baseline).Select(capture => (capture, GetColumnName(capture))).ToList());
    }

    /// <summary>The name of the reverse-engineering work, e.g. 'GX-700'; empty when there is no session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSession), nameof(HasNoSession), nameof(DisplayTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveSessionCommand), nameof(SaveSessionAsCommand), nameof(LoadSysExCommand), nameof(PasteHexCommand), nameof(ReceiveCommand))]
    public partial string Title { get; private set; } = String.Empty;

    public bool HasSession => Title.Length > 0;
    public bool HasNoSession => !HasSession;

    /// <summary>True when the session has changes that are not saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial bool IsDirty { get; private set; }

    public string DisplayTitle => IsDirty ? $"{Title} •" : Title;

    /// <summary>The name of the session file; empty when the session was not saved yet.</summary>
    public string SessionFileName => _sessionFile?.Name ?? String.Empty;

    /// <summary>The captures of the session.</summary>
    public ObservableCollection<Capture> Captures { get; } = [];

    public bool HasCaptures => Captures.Count > 0;

    /// <summary>The capture all others are compared to; null when there are no captures.</summary>
    [ObservableProperty]
    public partial Capture? Baseline { get; private set; }

    /// <summary>
    /// The capture selected in the captures list. A selected variant that is not hidden is focused:
    /// the byte view then shows only its differences.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusedVariant))]
    public partial Capture? SelectedCapture { get; set; }

    /// <summary>The variant whose differences are highlighted; null to highlight the differences of all visible variants.</summary>
    public Capture? FocusedVariant => SelectedCapture is { IsBaseline: false, IsHidden: false } capture ? capture : null;

    /// <summary>The comparison of the visible variants with the baseline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Slots), nameof(ComparisonRemarks), nameof(HasComparisonRemarks))]
    public partial CaptureComparison Comparison { get; private set; } = CaptureComparison.Empty;

    /// <summary>The message slots of the baseline.</summary>
    public IReadOnlyList<SlotComparison> Slots => Comparison.Slots;

    public string ComparisonRemarks => String.Join(Environment.NewLine, Comparison.Remarks);
    public bool HasComparisonRemarks => Comparison.Remarks.Count > 0;

    /// <summary>The slot selected in the slots list; selecting one selects its first data byte.</summary>
    [ObservableProperty]
    public partial SlotComparison? SelectedSlot { get; set; }

    /// <summary>The bytes selected in the byte view; null when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectionText), nameof(AnnotationText))]
    public partial ByteRange? Selection { get; set; }

    public bool HasSelection => Selection is not null;

    /// <summary>The selection as text, e.g. 'slot 9 · 23–24 (2 bytes)'.</summary>
    public string SelectionText => Selection is { } range
        ? $"{range} ({range.Length} {(range.Length == 1 ? "byte" : "bytes")})"
        : "Select bytes in the byte view.";

    /// <summary>The notes of the user on byte ranges.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Annotation> Annotations { get; private set; } = [];

    /// <summary>
    /// The note on exactly the selected bytes. Setting it adds, changes or (when empty) removes that annotation.
    /// </summary>
    public string AnnotationText
    {
        get => Selection is { } range ? Annotations.FirstOrDefault(annotation => annotation.Range == range)?.Text ?? String.Empty : String.Empty;
        set
        {
            if (Selection is not { } range || String.Equals(value, AnnotationText, StringComparison.Ordinal))
            {
                return;
            }
            var others = Annotations.Where(annotation => annotation.Range != range);
            Annotations = String.IsNullOrWhiteSpace(value) ? others.ToList() : others.Append(new Annotation(range, value)).ToList();
            IsDirty = true;
            OnPropertyChanged();
        }
    }

    /// <summary>The column headers of the interpret strip: the compared captures, baseline first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> InterpretationColumns { get; private set; } = [];

    /// <summary>The candidate interpretations of the selected bytes, with a value per compared capture.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Interpretation> Interpretations { get; private set; } = [];

    /// <summary>The outcome of the last action.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "Start a new session or open a saved one.";

    /// <summary>The text prompt while it is shown (title, pasted hex); null otherwise.</summary>
    [ObservableProperty]
    public partial TextPromptViewModel? Prompt { get; private set; }

    /// <summary>The confirmation dialog while it is shown (discard changes, remove a capture); null otherwise.</summary>
    [ObservableProperty]
    public partial ConfirmViewModel? Confirmation { get; private set; }

    /// <summary>True while waiting for (more) SysEx messages of a dump from MIDI.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiveButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ReceiveCommand))]
    public partial bool IsReceiving { get; private set; }

    public string ReceiveButtonText => IsReceiving ? "Stop receiving" : "Receive";

    // ---- session ----

    /// <summary>Starts a new session: asks for its title (after confirming to discard unsaved changes).</summary>
    [RelayCommand]
    private void NewSession() => WhenChangesMayBeDiscarded(() =>
        Prompt = new TextPromptViewModel("NEW SESSION", "Reverse engineer a device",
            "The title names the work, e.g. the device: 'GX-700'. It is also the name of the schema that is built from it.",
            "Create", CloseNewSessionPrompt, text => String.IsNullOrWhiteSpace(text) ? "Enter a title." : null)
        {
            Placeholder = "Device name"
        });

    private void CloseNewSessionPrompt(TextPromptViewModel prompt, bool confirmed)
    {
        Prompt = null;
        if (confirmed)
        {
            Load(new WorkbenchSession(prompt.Text.Trim(), [], -1, []), file: null);
            IsDirty = true;
            Status = $"Created session {Title}. Load, paste or receive the first dump: it becomes the baseline.";
        }
    }

    [RelayCommand]
    private void OpenSession() => WhenChangesMayBeDiscarded(() => _ = OpenSessionFile());

    private async Task OpenSessionFile()
    {
        try
        {
            if (await _storage.OpenSession() is not { } opened)
            {
                return;
            }
            var session = WorkbenchSessionFile.Deserialize(opened.Text);
            if (String.IsNullOrWhiteSpace(session.Title))
            {
                session = session with { Title = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(opened.File.Name)) };
            }
            Load(session, opened.File);
            Status = $"Opened {opened.File.Name}: {Captures.Count} capture(s).";
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or IOException
            or UnauthorizedAccessException or NotSupportedException)
        {
            Status = $"Could not open the session: {exception.Message}";
        }
    }

    private bool CanSave() => HasSession;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveSession() => Save(_sessionFile);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveSessionAs() => Save(null);

    private async Task Save(SessionFile? file)
    {
        try
        {
            var text = WorkbenchSessionFile.Serialize(ToSession());
            if (await _storage.SaveSession(file, Title + WorkbenchSessionFile.Extension, text) is not { } saved)
            {
                return;
            }
            _sessionFile = saved;
            OnPropertyChanged(nameof(SessionFileName));
            IsDirty = false;
            Status = $"Saved {saved.Name}.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or NotSupportedException)
        {
            Status = $"Could not save the session: {exception.Message}";
        }
    }

    /// <summary>The current state as a session (for saving).</summary>
    internal WorkbenchSession ToSession()
        => new(Title, Captures.ToList(), Baseline is null ? -1 : Captures.IndexOf(Baseline), Annotations)
        {
            SlotNotes = new Dictionary<int, string>(_slotNotes)
        };

    /// <summary>Replaces the session; the state is not dirty afterwards.</summary>
    internal void Load(WorkbenchSession session, SessionFile? file)
    {
        StopReceiving();
        foreach (var capture in Captures)
        {
            capture.PropertyChanged -= OnCapturePropertyChanged;
        }
        Captures.Clear();
        Selection = null;
        SelectedCapture = null;
        ClearFind();
        Annotations = session.Annotations;
        _slotNotes = new Dictionary<int, string>(session.SlotNotes);
        foreach (var capture in session.Captures)
        {
            capture.PropertyChanged += OnCapturePropertyChanged;
            Captures.Add(capture);
        }
        _sessionFile = file;
        OnPropertyChanged(nameof(SessionFileName));
        Title = session.Title;
        SetBaselineCore(session.Baseline >= 0 && session.Baseline < Captures.Count ? Captures[session.Baseline] : Captures.FirstOrDefault());
        IsDirty = false;
        // the target schema is found by the title
        RefreshSchema();
    }

    // asks to discard unsaved changes before running the action
    private void WhenChangesMayBeDiscarded(Action action)
    {
        if (!IsDirty)
        {
            action();
            return;
        }
        Confirmation = new ConfirmViewModel("UNSAVED CHANGES", $"Discard the changes to {Title}?",
            "The session has changes that are not saved. They are lost when you continue.", "Discard",
            (_, confirmed) =>
            {
                Confirmation = null;
                if (confirmed)
                {
                    action();
                }
            });
    }

    // ---- captures ----

    /// <summary>Loads a SysEx file as a capture.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task LoadSysEx()
    {
        try
        {
            if (await _storage.OpenSysEx() is not { } file)
            {
                return;
            }
            if (file.Bytes.Length == 0)
            {
                Status = $"{file.Name} is empty.";
                return;
            }
            AddCapture(new Capture(Path.GetFileNameWithoutExtension(file.Name), SysExSplitter.Split(file.Bytes)));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Status = $"Could not load the file: {exception.Message}";
        }
    }

    /// <summary>Asks for hex text and adds it as a capture.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void PasteHex()
        => Prompt = new TextPromptViewModel("PASTE HEX", "Add a capture from hex text",
            "Bytes separated by spaces, commas or nothing (F0 41 … F7). Several messages are split at F7.",
            "Add", ClosePasteHexPrompt, text => HexText.TryParse(text, out _, out var error) ? null : error)
        {
            AcceptsReturn = true,
            Placeholder = "F0 41 00 79 12 … F7"
        };

    private void ClosePasteHexPrompt(TextPromptViewModel prompt, bool confirmed)
    {
        Prompt = null;
        if (confirmed && HexText.TryParse(prompt.Text, out var bytes, out _))
        {
            AddCapture(new Capture($"Pasted {DateTime.Now:HH:mm:ss}", SysExSplitter.Split(bytes)));
        }
    }

    /// <summary>Adds a capture; the first capture becomes the baseline. The new capture is selected (focused).</summary>
    internal void AddCapture(Capture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        capture.PropertyChanged += OnCapturePropertyChanged;
        Captures.Add(capture);
        IsDirty = true;
        if (Baseline is null)
        {
            SetBaselineCore(capture);
            Status = $"Added {capture.Label} ({capture.Summary}) as the baseline. Change one parameter on the device and capture again.";
        }
        else
        {
            Recompare();
            Status = $"Added {capture.Label} ({capture.Summary}): {capture.DifferenceCount} byte(s) differ from the baseline. Describe the change in its note.";
        }
        SelectedCapture = capture;
    }

    [RelayCommand]
    private void SetBaseline(Capture? capture)
    {
        if (capture is null || ReferenceEquals(capture, Baseline))
        {
            return;
        }
        SetBaselineCore(capture);
        IsDirty = true;
        Status = $"{capture.Label} is the baseline.";
    }

    private void SetBaselineCore(Capture? capture)
    {
        foreach (var other in Captures)
        {
            other.IsBaseline = ReferenceEquals(other, capture);
        }
        if (capture is not null)
        {
            capture.IsHidden = false;
        }
        Baseline = capture;
        OnPropertyChanged(nameof(FocusedVariant));
        Recompare();
    }

    [RelayCommand]
    private void RemoveCapture(Capture? capture)
    {
        if (capture is null)
        {
            return;
        }
        Confirmation = new ConfirmViewModel("REMOVE CAPTURE", $"Remove {capture.Label}?",
            "The capture is removed from the session. This cannot be undone.", "Remove",
            (_, confirmed) =>
            {
                Confirmation = null;
                if (confirmed)
                {
                    RemoveCaptureCore(capture);
                }
            });
    }

    internal void RemoveCaptureCore(Capture capture)
    {
        capture.PropertyChanged -= OnCapturePropertyChanged;
        Captures.Remove(capture);
        IsDirty = true;
        if (ReferenceEquals(capture, SelectedCapture))
        {
            SelectedCapture = null;
        }
        if (ReferenceEquals(capture, Baseline))
        {
            SetBaselineCore(Captures.FirstOrDefault());
        }
        else
        {
            Recompare();
        }
        Status = $"Removed {capture.Label}.";
    }

    /// <summary>Highlights the differences of all visible variants again (no variant focused).</summary>
    [RelayCommand]
    private void ClearFocus() => SelectedCapture = null;

    private void OnCapturePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Capture.Label):
            case nameof(Capture.ChangeNote):
                IsDirty = true;
                UpdateInterpretations();
                UpdateField();
                break;
            case nameof(Capture.IsHidden):
                IsDirty = true;
                OnPropertyChanged(nameof(FocusedVariant));
                Recompare();
                break;
        }
    }

    partial void OnSelectedCaptureChanged(Capture? value) => Recompare();

    partial void OnSelectedSlotChanged(SlotComparison? value)
    {
        if (value is not null && Selection?.Slot != value.Index)
        {
            // the first data byte: after F0
            var start = value.Bytes is [0xF0, _, ..] ? 1 : 0;
            Selection = value.Bytes.Length > 0 ? new ByteRange(value.Index, start, 1) : null;
        }
    }

    partial void OnSelectionChanged(ByteRange? value)
    {
        if (value is { } range && SelectedSlot?.Index != range.Slot)
        {
            SelectedSlot = Slots.ElementAtOrDefault(range.Slot);
        }
        UpdateInterpretations();
        UpdateField();
        OnNoteSlotChanged();
    }

    partial void OnSelectedSlotChanged(SlotComparison? oldValue, SlotComparison? newValue) => OnNoteSlotChanged();

    // ---- comparison ----

    /// <summary>The captures that are compared with the baseline: the visible variants.</summary>
    private List<Capture> ComparedVariants
        => Captures.Where(capture => !capture.IsHidden && !ReferenceEquals(capture, Baseline)).ToList();

    private void Recompare()
    {
        if (Baseline is null)
        {
            Comparison = CaptureComparison.Empty;
        }
        else
        {
            Comparison = CaptureComparison.Compare(Baseline, ComparedVariants, FocusedVariant);
        }
        foreach (var capture in Captures)
        {
            capture.DifferenceCount = Comparison.GetDifferenceCount(capture);
        }
        ApplySlotNotes();
        RunFind();

        // keep the selection inside the baseline
        if (Selection is { } range)
        {
            if (Slots.ElementAtOrDefault(range.Slot) is not { } slot || range.Start >= slot.Bytes.Length)
            {
                Selection = null;
            }
            else if (range.End >= slot.Bytes.Length)
            {
                Selection = range with { Length = slot.Bytes.Length - range.Start };
            }
        }
        SelectedSlot = Selection is { } selection ? Slots.ElementAtOrDefault(selection.Slot) : null;
        UpdateInterpretations();
        RefreshSchema();
    }

    private void UpdateInterpretations()
    {
        if (Baseline is null || Selection is not { } range)
        {
            InterpretationColumns = [];
            Interpretations = [];
            return;
        }

        var captures = ComparedVariants.Prepend(Baseline).ToList();
        InterpretationColumns = captures.Select(GetColumnName).ToList();
        Interpretations = ByteInterpretations.Interpret(captures.Select(capture => GetBytes(capture, range)).ToList());
    }

    // a variant is named by its change note when it has one
    private static string GetColumnName(Capture capture)
        => capture.IsBaseline || String.IsNullOrWhiteSpace(capture.ChangeNote) ? capture.Label : capture.ChangeNote;

    /// <summary>The bytes of <paramref name="range"/> in <paramref name="capture"/>; null when the capture does not have them.</summary>
    internal static byte[]? GetBytes(Capture capture, ByteRange range)
        => range.Slot < capture.Messages.Count && capture.Messages[range.Slot] is { } message && range.End < message.Length
            ? message[range.Start..(range.End + 1)]
            : null;

    /// <summary>
    /// The tooltip of a byte in the byte view: its offset, its value in the baseline and in each compared variant, and its annotations.
    /// </summary>
    public string? DescribeByte(int slot, int offset)
    {
        if (Baseline is null || slot >= Baseline.Messages.Count || offset >= Baseline.Messages[slot].Length)
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append($"Slot {slot + 1} · offset {offset} (0x{offset:X2})");
        if (Layouts.ElementAtOrDefault(slot)?.Find(offset) is { } span)
        {
            text.AppendLine().Append(span.IsPlaceholder
                ? $"Unknown span {span.Field.Name} ({span.Length} bytes)"
                : $"Field {span.Field.Name}: {span.Field.DataType}{(span.Field.Processors.FirstOrDefault() is { } processor ? $" · {processor.Processor}" : String.Empty)}");
        }
        foreach (var capture in ComparedVariants.Prepend(Baseline))
        {
            var bytes = GetBytes(capture, new ByteRange(slot, offset, 1));
            text.AppendLine().Append($"{GetColumnName(capture)}: {(bytes is null ? ByteInterpretations.NoValue : $"{bytes[0]:X2} ({bytes[0]})")}");
        }
        foreach (var annotation in Annotations.Where(annotation => annotation.Range.Contains(slot, offset)))
        {
            text.AppendLine().Append($"Note ({annotation.Range}): {annotation.Text}");
        }
        return text.ToString();
    }

    // ---- receive ----

    private bool CanReceive() => IsReceiving || (HasSession && _midi.IsConnected);

    /// <summary>
    /// Collects the SysEx7 messages of a dump from the group selected in the MIDI bar; the dump ends when no message
    /// arrives for a short time and becomes a capture. Pressed while receiving it ends the dump at once.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReceive))]
    private void Receive()
    {
        if (IsReceiving)
        {
            FinishReceiving();
            return;
        }

        _received.Clear();
        _assembler = new SysEx7Assembler(_midi.Group.Value);
        _midi.PacketsReceived += OnPacketsReceived;
        IsReceiving = true;
        Status = $"Waiting for a dump on group {_midi.Group.Value + 1}… Send it from the device.";
    }

    // on the UI thread
    private void OnReceived(byte[] sysEx)
    {
        if (!IsReceiving)
        {
            return;
        }
        _received.Add(sysEx);
        Status = $"Receiving… {_received.Count} message(s).";
        _receiveTimer ??= CreateReceiveTimer();
        _receiveTimer.Stop();
        _receiveTimer.Start();
    }

    private DispatcherTimer CreateReceiveTimer()
    {
        var timer = new DispatcherTimer { Interval = ReceiveIdleTime };
        timer.Tick += (_, _) => FinishReceiving();
        return timer;
    }

    private void FinishReceiving()
    {
        var messages = _received.ToList();
        StopReceiving();
        if (messages.Count == 0)
        {
            Status = "Stopped waiting for a dump.";
            return;
        }
        AddCapture(new Capture($"Received {DateTime.Now:HH:mm:ss}", messages));
    }

    private void StopReceiving()
    {
        _receiveTimer?.Stop();
        _midi.PacketsReceived -= OnPacketsReceived;
        _assembler = null;
        _received.Clear();
        IsReceiving = false;
    }

    // raised on a background thread; the span is only valid during the call
    private void OnPacketsReceived(ReadOnlySpan<StampedPacket> packets)
    {
        if (_assembler is not { } assembler)
        {
            return;
        }
        foreach (var packet in packets)
        {
            if (packet.Packet.HasValue && assembler.TryAdd(packet.Packet, out var sysEx))
            {
                Dispatcher.UIThread.Post(() => OnReceived(sysEx));
            }
        }
    }

    private void OnMidiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MidiBarViewModel.IsConnected))
        {
            if (!_midi.IsConnected && IsReceiving)
            {
                FinishReceiving();
            }
            ReceiveCommand.NotifyCanExecuteChanged();
        }
    }
}
