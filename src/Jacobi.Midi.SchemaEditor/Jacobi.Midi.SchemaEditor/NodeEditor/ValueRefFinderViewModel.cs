using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// The view model of the dialog (shown as an overlay by <see cref="ValueRefFinderView"/>) that finds the reference for a
/// value-or-reference property of a node: a field before the node ('ref:', see <see cref="ValueRefFinder"/>), with an
/// instance index for each repeat on its path, or a value a processor publishes ('pub:').
/// </summary>
internal sealed partial class ValueRefFinderViewModel : ViewModelBase
{
    public const int RefTab = 0;
    public const int PubTab = 1;

    private readonly ValueRefCandidates _candidates;
    private readonly Action<ValueRefFinderViewModel, string?> _close;

    /// <param name="title">What the reference is for, e.g. "Count of Root.Items".</param>
    /// <param name="candidates">The fields and published values the node can refer to.</param>
    /// <param name="current">The current text of the property: a reference in it is selected.</param>
    /// <param name="notes">The situations the user may not expect (e.g. the schema did not compile); empty when there are none.</param>
    /// <param name="close">Called with this view model and the chosen reference text, or null when cancelled.</param>
    /// <param name="relative">True to start with a relative path (e.g. in a node def); a reference in <paramref name="current"/> decides otherwise.</param>
    public ValueRefFinderViewModel(string title, ValueRefCandidates candidates, string current, IReadOnlyList<string> notes,
        Action<ValueRefFinderViewModel, string?> close, bool relative = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Title = title;
        _candidates = candidates;
        _close = close;
        IsRelative = relative;
        var allNotes = new List<string>(notes);
        Targets = candidates.Targets;
        Select(current?.Trim() ?? String.Empty, allNotes);
        Notes = String.Join(Environment.NewLine, allNotes);
    }

    /// <summary>
    /// True for a relative path: '^' per level up from this node to the nearest ancestor it shares with the field, then down.
    /// It keeps the instance of this node: the repeats it goes up through are the item of this node, and it cannot reach
    /// into another repeat. So it has no instance indices.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Result), nameof(Problem), nameof(HasProblem), nameof(ShowsRepeatIndexes))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool IsRelative { get; set; }

    public string Title { get; }

    /// <summary>The situations the user may not expect (e.g. the schema did not compile), one per line; empty when there are none.</summary>
    public string Notes { get; }

    public bool HasNotes => Notes.Length > 0;

    /// <summary>The tab shown: <see cref="RefTab"/> or <see cref="PubTab"/>; it decides the kind of <see cref="Result"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Result), nameof(Problem), nameof(HasProblem))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial int SelectedTab { get; set; }

    /// <summary>Text the <see cref="Targets"/> (their path) must contain; empty shows all.</summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = String.Empty;

    partial void OnFilterChanged(string value)
    {
        var filter = value.Trim();
        Targets = filter.Length == 0
            ? _candidates.Targets
            : [.. _candidates.Targets.Where(target => target.DisplayPath.Contains(filter, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>The fields that can be referenced (filtered), nearest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTargets))]
    public partial IReadOnlyList<ValueRefTarget> Targets { get; private set; }

    public bool HasNoTargets => Targets.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Result), nameof(Problem), nameof(HasProblem))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial ValueRefTarget? SelectedTarget { get; set; }

    partial void OnSelectedTargetChanged(ValueRefTarget? value)
        => RepeatIndexes = value is null ? [] : [.. value.Repeats.Select(repeat => new RepeatIndexBox(repeat, OnIndexChanged))];

    /// <summary>The instance index of each repeat on the path of the <see cref="SelectedTarget"/> (for an absolute path).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsRepeatIndexes))]
    public partial IReadOnlyList<RepeatIndexBox> RepeatIndexes { get; private set; } = [];

    /// <summary>True when the <see cref="RepeatIndexes"/> apply: an absolute path with repeats on it.</summary>
    public bool ShowsRepeatIndexes => !IsRelative && RepeatIndexes.Count > 0;

    /// <summary>The values processors publish before the node.</summary>
    public IReadOnlyList<ValueRefPublished> Published => _candidates.Published;

    public bool HasNoPublished => Published.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Result), nameof(Problem), nameof(HasProblem))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial ValueRefPublished? SelectedPublished { get; set; }

    /// <summary>The reference that is applied: of the selected field or published value of the <see cref="SelectedTab"/>; empty when none is selected.</summary>
    public string Result => SelectedTab == PubTab
        ? SelectedPublished?.Reference ?? String.Empty
        : IsRelative
            ? SelectedTarget?.ToRelativeReference(_candidates.Referrer) ?? String.Empty
            : SelectedTarget?.ToReference(segment => RepeatIndexes.First(box => box.Segment == segment).Index) ?? String.Empty;

    /// <summary>
    /// Why the <see cref="Result"/> cannot be applied (an invalid instance index, or a relative path into another repeat);
    /// null when it can.
    /// </summary>
    public string? Problem => SelectedTab != RefTab ? null
        : IsRelative
            ? SelectedTarget?.Repeats.FirstOrDefault(repeat => !repeat.HoldsReferrer) is { } other
                ? $"'{SelectedTarget.Path}' is in the repeat '{other.Path}' that does not hold this node: "
                    + "a relative path cannot reach into it, use an absolute path with an item index."
                : null
            : RepeatIndexes.Select(box => box.Problem).FirstOrDefault(problem => problem is not null);

    public bool HasProblem => Problem is not null;

    private bool CanApply() => Result.Length > 0 && Problem is null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => _close(this, Result);

    [RelayCommand]
    private void Cancel() => _close(this, null);

    private void OnIndexChanged()
    {
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    // selects the reference in the current text of the property
    private void Select(string current, List<string> notes)
    {
        if (SchemaNodeRef.TryParse(current, out var parsed))
        {
            IsRelative = parsed.IsRelative;
            if (parsed.Resolve(_candidates.Referrer) is not { } nodeRef)
            {
                notes.Add($"The current reference '{current}' goes up beyond '{_candidates.Referrer.Segments.First()}'.");
                return;
            }
            SelectedTarget = _candidates.Targets.FirstOrDefault(target => String.Equals(target.Path, nodeRef.Path, StringComparison.OrdinalIgnoreCase));
            if (SelectedTarget is null)
            {
                notes.Add($"The current reference '{current}' is not one of the fields before this node.");
                return;
            }
            foreach (var box in RepeatIndexes)
            {
                // no index is the first item
                var index = nodeRef.Indices.FirstOrDefault(index => String.Equals(index.Node, box.Segment.Path, StringComparison.OrdinalIgnoreCase));
                box.Text = index.Node is null ? "0" : index.IsCurrent ? String.Empty : index.Index.ToString(CultureInfo.InvariantCulture);
            }
        }
        else if (SchemaPubRef.TryParse(current, out _))
        {
            SelectedTab = PubTab;
            SelectedPublished = Published.FirstOrDefault(value => String.Equals(value.Reference, current, StringComparison.OrdinalIgnoreCase));
            if (SelectedPublished is null)
            {
                notes.Add($"The current reference '{current}' is not one of the values published before this node.");
            }
        }
    }
}

/// <summary>
/// The instance index of a repeat on the path of a referenced field: empty is '[]' (the same item as the referring node),
/// a number is that item ('[n]').
/// </summary>
internal sealed partial class RepeatIndexBox : ObservableObject
{
    // the item indexes offered for a repeat with a constant count (at most)
    private const int MaxOptions = 256;
    private readonly Action _changed;

    public RepeatIndexBox(ValueRefSegment segment, Action changed)
    {
        Segment = segment;
        _changed = changed;
        Options = segment.Count is int count
            ? [String.Empty, .. Enumerable.Range(0, Math.Min(count, MaxOptions)).Select(index => index.ToString(CultureInfo.InvariantCulture))]
            : [String.Empty];
        // '[]' is only valid inside the repeat: another repeat starts at its first item
        Text = segment.HoldsReferrer ? String.Empty : "0";
    }

    public ValueRefSegment Segment { get; }

    /// <summary>The path of the repeat.</summary>
    public string Path => Segment.Path;

    /// <summary>The choices: empty ('[]') and the item indexes of a repeat with a constant count.</summary>
    public IReadOnlyList<string> Options { get; }

    public string Placeholder => Segment.HoldsReferrer ? "[] the same item" : "item index";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string Text { get; set; }

    partial void OnTextChanged(string value) => _changed();

    /// <summary>The index: null for '[]' (and for text that is not an index, see <see cref="Problem"/>).</summary>
    public int? Index => Int32.TryParse(Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;

    /// <summary>Why the index is not valid; null when it is.</summary>
    public string? Problem
    {
        get
        {
            var text = Text?.Trim() ?? String.Empty;
            if (text.Length == 0)
            {
                return Segment.HoldsReferrer ? null : $"'[]' on '{Path}' is only valid for a node inside that repeat: give an item index.";
            }
            if (Index is not int index)
            {
                return $"'{text}' is not an item index of '{Path}'.";
            }
            return Segment.Count is int count && index >= count ? $"'{Path}' has {count} items: {index} is not one of them." : null;
        }
    }
}
