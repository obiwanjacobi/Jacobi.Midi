namespace Jacobi.Midi.Messages;

/// <summary>
/// Splits a byte stream (e.g. the contents of a .syx file) into its SysEx messages (each F0 … F7).
/// </summary>
public static class SysExSplitter
{
    /// <summary>
    /// Splits <paramref name="data"/> into its messages. Data that does not start with F0 is one message.
    /// Anything between an F7 and the next F0 is skipped. An unterminated last message is kept as it is.
    /// </summary>
    public static IReadOnlyList<byte[]> Split(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0 || data[0] != SysEx.Start)
        {
            return [data];
        }

        var messages = new List<byte[]>();
        var start = 0;
        while (start < data.Length)
        {
            var end = Array.IndexOf(data, SysEx.End, start);
            var next = end < 0 ? data.Length : end + 1;
            messages.Add(data[start..next]);
            // skip anything between the messages
            start = Array.IndexOf(data, SysEx.Start, next);
            if (start < 0)
            {
                break;
            }
        }
        return messages;
    }
}
