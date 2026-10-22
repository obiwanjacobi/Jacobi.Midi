using CommunityToolkit.Mvvm.ComponentModel;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// A capture: one dump of the device as received or loaded, an ordered list of SysEx messages (F0 … F7).
/// One capture of the workbench session is the baseline; the others are variants that are compared to it.
/// A variant carries a free-text change note that tells what was changed on the device for this dump.
/// </summary>
internal sealed partial class Capture : ObservableObject
{
    /// <param name="label">The name of the capture, e.g. the file name or 'Received 12:34:56'.</param>
    /// <param name="messages">The messages of the dump, in the order they were received.</param>
    public Capture(string label, IReadOnlyList<byte[]> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        Label = label;
        Messages = messages;
        ByteCount = messages.Sum(message => message.Length);
    }

    /// <summary>The messages of the dump; the index of a message is its message slot.</summary>
    public IReadOnlyList<byte[]> Messages { get; }

    /// <summary>The number of bytes of all messages.</summary>
    public int ByteCount { get; }

    /// <summary>The number of messages and bytes, e.g. '14 msgs · 912 B'.</summary>
    public string Summary => $"{Messages.Count} {(Messages.Count == 1 ? "msg" : "msgs")} · {ByteCount} B";

    [ObservableProperty]
    public partial string Label { get; set; }

    /// <summary>What was changed on the device for this dump (free text), e.g. 'reverb level 40'.</summary>
    [ObservableProperty]
    public partial string ChangeNote { get; set; } = String.Empty;

    /// <summary>True when the capture is left out of the comparison (but kept in the session).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferenceText))]
    public partial bool IsHidden { get; set; }

    /// <summary>True for the capture all others are compared to. Set by the workbench.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferenceText))]
    public partial bool IsBaseline { get; internal set; }

    /// <summary>The number of bytes that differ from the baseline. Set by the workbench.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferenceText))]
    public partial int DifferenceCount { get; internal set; }

    /// <summary>The difference badge: 'baseline', 'hidden', 'no Δ' or e.g. '2 B Δ'.</summary>
    public string DifferenceText => IsBaseline ? "baseline"
        : IsHidden ? "hidden"
        : DifferenceCount == 0 ? "no Δ"
        : $"{DifferenceCount} B Δ";
}

/// <summary>
/// A range of bytes in one message slot: the selection in the byte view or the bytes an annotation is about.
/// </summary>
/// <param name="Slot">The index of the message slot.</param>
/// <param name="Start">The offset of the first byte in the message.</param>
/// <param name="Length">The number of bytes (at least 1).</param>
internal readonly record struct ByteRange(int Slot, int Start, int Length)
{
    /// <summary>The offset of the last byte in the message.</summary>
    public int End => Start + Length - 1;

    public bool Contains(int slot, int offset) => slot == Slot && offset >= Start && offset <= End;

    /// <summary>
    /// The range as text, e.g. 'slot 9 · 23–24'. Slots are numbered from 1 (message 1 of the dump),
    /// offsets count from 0, as in the byte view.
    /// </summary>
    public override string ToString() => Length == 1 ? $"slot {Slot + 1} · {Start}" : $"slot {Slot + 1} · {Start}–{End}";
}

/// <summary>
/// A free-text note of the user on a range of bytes that is not (yet) described by the schema.
/// Annotations are kept in the workbench session, not in the schema.
/// </summary>
/// <param name="Range">The bytes the note is about.</param>
/// <param name="Text">The note.</param>
internal sealed record Annotation(ByteRange Range, string Text);
