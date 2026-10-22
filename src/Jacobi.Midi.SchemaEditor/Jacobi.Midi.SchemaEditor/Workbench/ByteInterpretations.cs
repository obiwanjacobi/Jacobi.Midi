using System.Globalization;
using System.Text;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// One candidate interpretation of the selected bytes: its name and the value it gives in each capture.
/// </summary>
/// <param name="Name">The interpretation, e.g. 'uint8' or '7-bit, MSB first'.</param>
/// <param name="Values">The value per capture, in the order of the samples; '—' when a capture has no such bytes.</param>
internal sealed record Interpretation(string Name, IReadOnlyList<string> Values);

/// <summary>
/// Computes the candidate interpretations of a byte range (the interpret strip of the workbench):
/// the common ways a device stores a value in SysEx data bytes, applied to the same bytes of every capture.
/// </summary>
internal static class ByteInterpretations
{
    /// <summary>The value shown for a capture that does not have the bytes.</summary>
    public const string NoValue = "—";

    // longer ranges only get the string and hex rows
    private const int MaxNumericLength = 8;

    /// <summary>
    /// Interprets the samples: the selected bytes of each capture (null when a capture does not have them).
    /// The rows depend on the number of bytes (the length of the first sample that is not null).
    /// The ASCII row comes first when all bytes of the first sample are printable; the hex row is always last.
    /// </summary>
    public static IReadOnlyList<Interpretation> Interpret(IReadOnlyList<byte[]?> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.FirstOrDefault(sample => sample is not null) is not { Length: > 0 } first)
        {
            return [];
        }

        var length = first.Length;
        var rows = new List<Interpretation>();
        if (length == 1)
        {
            rows.Add(Row("uint8", samples, length, bytes => Number(bytes[0])));
            rows.Add(Row("int8", samples, length, bytes => Number((sbyte)bytes[0])));
            rows.Add(Row("int7 (7-bit signed)", samples, length, bytes => Number(bytes[0] >= 0x40 ? (bytes[0] & 0x7F) - 0x80 : bytes[0] & 0x7F)));
        }
        if (length is 2 or 4)
        {
            var bits = length * 8;
            rows.Add(Row($"uint{bits} big endian", samples, length, bytes => Number(Join(bytes, 8, msbFirst: true))));
            rows.Add(Row($"uint{bits} little endian", samples, length, bytes => Number(Join(bytes, 8, msbFirst: false))));
        }
        if (length is >= 2 and <= 4)
        {
            rows.Add(Row("7-bit, MSB first", samples, length, bytes => Number(Join(bytes, 7, msbFirst: true))));
            rows.Add(Row("7-bit, LSB first", samples, length, bytes => Number(Join(bytes, 7, msbFirst: false))));
        }
        if (length is >= 2 and <= MaxNumericLength)
        {
            rows.Add(Row("nibbles, high first", samples, length, bytes => Number(Join(bytes, 4, msbFirst: true))));
            rows.Add(Row("nibbles, low first", samples, length, bytes => Number(Join(bytes, 4, msbFirst: false))));
        }
        if (length <= 2)
        {
            rows.Add(Row("bits", samples, length, Bits));
        }

        var ascii = Row("ascii", samples, length, Ascii);
        if (first.All(IsPrintable))
        {
            rows.Insert(0, ascii);
        }
        else
        {
            rows.Add(ascii);
        }
        rows.Add(Row("hex", samples, length, bytes => HexText.Format(bytes)));
        return rows;
    }

    private static Interpretation Row(string name, IReadOnlyList<byte[]?> samples, int length, Func<byte[], string> format)
        => new(name, samples.Select(sample => sample is not null && sample.Length == length ? format(sample) : NoValue).ToList());

    // joins the low 'width' bits of each byte into one number
    private static ulong Join(byte[] bytes, int width, bool msbFirst)
    {
        var mask = (1UL << width) - 1;
        ulong value = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            var part = bytes[msbFirst ? index : bytes.Length - 1 - index] & mask;
            value = (value << width) | part;
        }
        return value;
    }

    private static string Number<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

    // the 7 data bits of a SysEx byte; the 8th bit only when it is set
    private static string Bits(byte[] bytes)
        => String.Join(' ', bytes.Select(value => Convert.ToString(value, 2).PadLeft(value > 0x7F ? 8 : 7, '0')));

    private static string Ascii(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            text.Append(IsPrintable(value) ? (char)value : '·');
        }
        return text.ToString();
    }

    private static bool IsPrintable(byte value) => value is >= 0x20 and < 0x7F;
}
