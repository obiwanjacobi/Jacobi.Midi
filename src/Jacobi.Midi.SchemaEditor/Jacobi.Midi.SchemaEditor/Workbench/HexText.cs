using System.Globalization;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// Converts bytes to and from hexadecimal text, as pasted by the user and stored in a workbench session file.
/// </summary>
internal static class HexText
{
    /// <summary>The bytes as space-separated hex pairs, e.g. <c>F0 41 F7</c>.</summary>
    public static string Format(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes) is { Length: > 0 } hex
        ? String.Join(' ', Enumerable.Range(0, bytes.Length).Select(index => hex.Substring(index * 2, 2)))
        : String.Empty;

    /// <summary>
    /// Parses hex text into bytes. Bytes are separated by whitespace, commas or nothing at all (<c>F04110F7</c>);
    /// a <c>0x</c> prefix is allowed per byte. A separated token of one digit is one byte (<c>F0 1 F7</c>).
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="bytes">The parsed bytes; empty when parsing failed.</param>
    /// <param name="error">Why the text could not be parsed; null on success.</param>
    /// <returns>True when the text holds at least one byte and nothing else.</returns>
    public static bool TryParse(string text, out byte[] bytes, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        bytes = [];
        var tokens = text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = "There are no hex bytes in the text.";
            return false;
        }

        var result = new List<byte>();
        foreach (var token in tokens)
        {
            var digits = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
            if (digits.Length == 0 || (digits.Length > 2 && digits.Length % 2 != 0))
            {
                error = $"'{token}' is not a hex byte (or a sequence of hex byte pairs).";
                return false;
            }
            for (var index = 0; index < digits.Length; index += 2)
            {
                var pair = digits.Length == 1 ? digits : digits.Substring(index, 2);
                if (!Byte.TryParse(pair, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                {
                    error = $"'{token}' is not a hex byte (or a sequence of hex byte pairs).";
                    return false;
                }
                result.Add(value);
            }
        }

        bytes = [.. result];
        error = null;
        return true;
    }
}
