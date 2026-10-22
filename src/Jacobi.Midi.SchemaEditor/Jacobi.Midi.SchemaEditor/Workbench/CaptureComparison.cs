using CommunityToolkit.Mvvm.ComponentModel;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// The comparison state of one byte of the baseline.
/// </summary>
/// <param name="DifferenceCount">The number of compared variants in which the byte differs.</param>
/// <param name="ChangedBits">
/// The bits that differ: of the focused variant when there is one, otherwise of all compared variants.
/// Zero when the byte is missing in a variant (a shorter message).
/// </param>
/// <param name="Tint">
/// How strongly the difference is highlighted (0 … 1): 1 when the focused variant differs,
/// or the share of the compared variants that differ when no variant is focused.
/// </param>
/// <param name="IsDerived">
/// True when the byte probably does not hold a parameter but is derived from the others (a checksum, a counter):
/// it differs in every variant that differs in the message, while those variants changed different bytes otherwise.
/// </param>
internal readonly record struct ByteState(int DifferenceCount, byte ChangedBits, double Tint, bool IsDerived)
{
    public bool IsDifferent => DifferenceCount > 0;
}

/// <summary>
/// The comparison of one message slot: the message of the baseline and the state of each of its bytes,
/// with the note of the user on the slot (set by the workbench).
/// </summary>
internal sealed partial class SlotComparison : ObservableObject
{
    /// <summary>The note of the user on the slot, e.g. 'probably the compressor'; empty when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; } = String.Empty;

    public bool HasNote => Note.Length > 0;

    public SlotComparison(int index, byte[] bytes, ByteState[] states, string lengthText)
    {
        Index = index;
        Bytes = bytes;
        States = states;
        LengthText = lengthText;
        HasDifference = lengthText.Length > 0 || states.Any(state => state.IsDifferent);
    }

    /// <summary>The index of the slot (the message index in the dump).</summary>
    public int Index { get; }

    /// <summary>The message of the baseline.</summary>
    public byte[] Bytes { get; }

    /// <summary>The comparison state per byte of <see cref="Bytes"/>.</summary>
    public ByteState[] States { get; }

    /// <summary>True when a compared variant differs in this slot (bytes, length or a missing message).</summary>
    public bool HasDifference { get; }

    /// <summary>A note on the message length in the variants, e.g. 'length 88 → 90' or 'missing in 1'; empty when they match.</summary>
    public string LengthText { get; }

    /// <summary>The slot header of the byte view, e.g. 'Slot 9 · 88 bytes · length 88 → 90'.</summary>
    public string Header => LengthText.Length > 0
        ? $"Slot {Index + 1} · {Bytes.Length} bytes · {LengthText}"
        : $"Slot {Index + 1} · {Bytes.Length} bytes";
}

/// <summary>
/// Compares the visible variants of a workbench session with the baseline, message slot by message slot (aligned by index),
/// byte by byte. The result is drawn on the baseline by the byte view.
/// </summary>
internal sealed class CaptureComparison
{
    private readonly Dictionary<Capture, int> _differenceCounts;

    private CaptureComparison(IReadOnlyList<SlotComparison> slots, Dictionary<Capture, int> differenceCounts, IReadOnlyList<string> remarks)
    {
        Slots = slots;
        _differenceCounts = differenceCounts;
        Remarks = remarks;
    }

    /// <summary>A comparison without a baseline: no slots.</summary>
    public static CaptureComparison Empty { get; } = new([], [], []);

    /// <summary>The slots of the baseline.</summary>
    public IReadOnlyList<SlotComparison> Slots { get; }

    /// <summary>Remarks on the comparison, e.g. a variant with a different number of messages.</summary>
    public IReadOnlyList<string> Remarks { get; }

    /// <summary>The number of bytes in which <paramref name="variant"/> differs from the baseline; 0 when it was not compared.</summary>
    public int GetDifferenceCount(Capture variant) => _differenceCounts.GetValueOrDefault(variant);

    /// <param name="baseline">The capture to compare with.</param>
    /// <param name="variants">The captures to compare (the baseline itself is skipped).</param>
    /// <param name="focus">The variant to highlight; null to highlight the differences of all <paramref name="variants"/>.</param>
    public static CaptureComparison Compare(Capture baseline, IReadOnlyList<Capture> variants, Capture? focus)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(variants);
        var compared = variants.Where(variant => !ReferenceEquals(variant, baseline)).ToList();
        if (focus is not null && !compared.Contains(focus))
        {
            focus = null;
        }

        var differenceCounts = compared.ToDictionary(variant => variant, _ => 0);
        var slots = new List<SlotComparison>(baseline.Messages.Count);
        for (var slot = 0; slot < baseline.Messages.Count; slot++)
        {
            slots.Add(CompareSlot(slot, baseline.Messages[slot], compared, focus, differenceCounts));
        }

        var remarks = new List<string>();
        foreach (var variant in compared.Where(variant => variant.Messages.Count != baseline.Messages.Count))
        {
            // the bytes of the extra messages differ as well
            differenceCounts[variant] += variant.Messages.Skip(baseline.Messages.Count).Sum(message => message.Length);
            remarks.Add($"{variant.Label}: {variant.Messages.Count} messages, the baseline has {baseline.Messages.Count}.");
        }
        return new CaptureComparison(slots, differenceCounts, remarks);
    }

    private static SlotComparison CompareSlot(int slot, byte[] bytes, List<Capture> variants, Capture? focus, Dictionary<Capture, int> differenceCounts)
    {
        var counts = new int[bytes.Length];
        var changedBits = new byte[bytes.Length];
        var focusDifferent = new bool[bytes.Length];
        // the differing offsets of each variant that differs in this slot
        var differing = new List<HashSet<int>>();
        var lengthChanges = new List<string>();
        var missing = 0;

        foreach (var variant in variants)
        {
            var message = slot < variant.Messages.Count ? variant.Messages[slot] : null;
            var offsets = new HashSet<int>();
            for (var offset = 0; offset < bytes.Length; offset++)
            {
                if (message is null || offset >= message.Length)
                {
                    offsets.Add(offset);
                    continue;
                }
                var bits = (byte)(bytes[offset] ^ message[offset]);
                if (bits == 0)
                {
                    continue;
                }
                offsets.Add(offset);
                if (focus is null || ReferenceEquals(variant, focus))
                {
                    changedBits[offset] |= bits;
                }
            }

            foreach (var offset in offsets)
            {
                counts[offset]++;
                if (ReferenceEquals(variant, focus))
                {
                    focusDifferent[offset] = true;
                }
            }

            var extra = message is null ? 0 : Math.Max(0, message.Length - bytes.Length);
            differenceCounts[variant] += offsets.Count + extra;
            if (offsets.Count > 0 || extra > 0)
            {
                differing.Add(offsets);
            }

            if (message is null)
            {
                missing += focus is null || ReferenceEquals(variant, focus) ? 1 : 0;
            }
            else if (message.Length != bytes.Length && (focus is null || ReferenceEquals(variant, focus)))
            {
                lengthChanges.Add($"length {bytes.Length} → {message.Length}");
            }
        }

        var derived = FindDerived(differing);
        var states = new ByteState[bytes.Length];
        for (var offset = 0; offset < bytes.Length; offset++)
        {
            var tint = focus is not null
                ? focusDifferent[offset] ? 1.0 : 0.0
                : variants.Count == 0 ? 0.0 : (double)counts[offset] / variants.Count;
            states[offset] = new ByteState(counts[offset], changedBits[offset], tint, derived.Contains(offset));
        }

        var lengthText = String.Join(" · ", lengthChanges.Distinct().Append(missing > 0 ? $"missing in {missing}" : null).OfType<string>());
        return new SlotComparison(slot, bytes, states, lengthText);
    }

    // The bytes that differ in every variant that differs, when the variants changed different bytes otherwise.
    // Two variants of the same parameter (e.g. two values of one knob) change the same bytes: then nothing is marked as derived.
    private static HashSet<int> FindDerived(List<HashSet<int>> differing)
    {
        if (differing.Count < 2)
        {
            return [];
        }

        var candidates = new HashSet<int>(differing[0]);
        foreach (var offsets in differing.Skip(1))
        {
            candidates.IntersectWith(offsets);
        }
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var others = differing
            .Select(offsets => offsets.Except(candidates).Order().ToArray())
            .Where(rest => rest.Length > 0)
            .DistinctBy(rest => String.Join(',', rest))
            .Count();
        return others >= 2 ? candidates : [];
    }
}
