using System.Globalization;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// What to find: a number (as shown on the device display, usually decimal) or a sequence of hex bytes.
/// </summary>
/// <param name="Text">The text the user entered.</param>
/// <param name="Value">The number to find; null when a byte sequence is searched.</param>
/// <param name="Sequence">The bytes to find; null when a number is searched.</param>
internal sealed record ValueQuery(string Text, long? Value, byte[]? Sequence);

/// <summary>
/// A place where the searched value was found: the bytes and how they hold the value.
/// </summary>
/// <param name="Range">The bytes (message slot and offsets).</param>
/// <param name="Interpretation">How the bytes hold the value, e.g. 'uint8' or 'nibbles, high first'.</param>
/// <param name="IsChanged">True when the bytes differ from the baseline: the most likely place of a value that was just changed.</param>
internal sealed record ValueMatch(ByteRange Range, string Interpretation, bool IsChanged);

/// <summary>
/// Finds a value in the messages of a capture: as one byte (unsigned or signed) or as two bytes that hold it
/// 7-bit packed or as nibbles, the common ways a device stores a parameter value in SysEx data bytes.
/// </summary>
internal static class ValueFinder
{
    /// <summary>
    /// Parses the search text: a decimal number (<c>70</c>, <c>-12</c>), a hex number (<c>0x46</c>, <c>$46</c>, <c>46h</c>)
    /// or a sequence of hex bytes separated by spaces or commas (<c>46 00</c>).
    /// </summary>
    public static bool TryParse(string text, out ValueQuery? query, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        query = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "Enter a value: 70, 0x46, 46h or hex bytes '46 00'.";
            return false;
        }

        if (trimmed.IndexOfAny([' ', ',', '\t']) >= 0)
        {
            if (!HexText.TryParse(trimmed, out var bytes, out error))
            {
                return false;
            }
            query = new ValueQuery(trimmed, null, bytes);
            return true;
        }

        var hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..]
            : trimmed.StartsWith('$') ? trimmed[1..]
            : trimmed.EndsWith('h') || trimmed.EndsWith('H') ? trimmed[..^1]
            : null;
        var parsed = hex is not null
            ? Int64.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
            : Int64.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        if (!parsed || value > UInt16.MaxValue || value < SByte.MinValue)
        {
            error = $"'{trimmed}' is not a value between -128 and 65535 (decimal, or hex as 0x46, $46 or 46h).";
            return false;
        }
        query = new ValueQuery(trimmed, value, null);
        error = null;
        return true;
    }

    /// <summary>
    /// Finds the query in the messages of <paramref name="capture"/>. The matches in bytes that differ from
    /// <paramref name="baseline"/> come first, the others follow in message order.
    /// </summary>
    public static IReadOnlyList<ValueMatch> Find(Capture capture, Capture? baseline, ValueQuery query)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(query);
        var matches = new List<ValueMatch>();
        for (var slot = 0; slot < capture.Messages.Count; slot++)
        {
            var message = capture.Messages[slot];
            var reference = baseline is not null && !ReferenceEquals(baseline, capture) && slot < baseline.Messages.Count
                ? baseline.Messages[slot]
                : null;
            foreach (var (start, length, interpretation) in FindInMessage(message, query))
            {
                var changed = reference is not null && Enumerable.Range(start, length)
                    .Any(offset => offset >= reference.Length || reference[offset] != message[offset]);
                matches.Add(new ValueMatch(new ByteRange(slot, start, length), interpretation, changed));
            }
        }
        return [.. matches.OrderByDescending(match => match.IsChanged)];
    }

    private static IEnumerable<(int Start, int Length, string Interpretation)> FindInMessage(byte[] message, ValueQuery query)
    {
        // the framing (F0, F7) holds no values
        var first = message is [0xF0, ..] ? 1 : 0;
        var end = message is [.., 0xF7] && message.Length > 1 ? message.Length - 1 : message.Length;

        if (query.Sequence is { Length: > 0 } sequence)
        {
            for (var start = first; start + sequence.Length <= end; start++)
            {
                if (message.AsSpan(start, sequence.Length).SequenceEqual(sequence))
                {
                    yield return (start, sequence.Length, "bytes");
                }
            }
            yield break;
        }
        if (query.Value is not { } value)
        {
            yield break;
        }

        for (var offset = first; offset < end; offset++)
        {
            var current = message[offset];
            if (value >= 0 && current == value)
            {
                yield return (offset, 1, "uint8");
            }
            else if (value < 0 && (sbyte)current == value)
            {
                yield return (offset, 1, "int8");
            }
            else if (value < 0 && current < 0x80 && current >= 0x40 && current - 0x80 == value)
            {
                yield return (offset, 1, "int7 (7-bit signed)");
            }

            if (offset + 1 >= end || value < 0)
            {
                continue;
            }
            var next = message[offset + 1];
            // a value that does not fit in one 7-bit byte is often split over two
            if (value > 0x7F && current < 0x80 && next < 0x80)
            {
                if (((current << 7) | next) == value)
                {
                    yield return (offset, 2, "7-bit, MSB first");
                }
                if (((next << 7) | current) == value)
                {
                    yield return (offset, 2, "7-bit, LSB first");
                }
            }
            if (value is > 0x0F and <= 0xFF && current <= 0x0F && next <= 0x0F)
            {
                if (((current << 4) | next) == value)
                {
                    yield return (offset, 2, "nibbles, high first");
                }
                if (((next << 4) | current) == value)
                {
                    yield return (offset, 2, "nibbles, low first");
                }
            }
        }
    }
}
