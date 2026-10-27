using System.Text.Json;

namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// The names of enum values in the console's files: camel case (<c>numberBox</c>, <c>xyPad</c>); read case-insensitively.
/// </summary>
internal static class FileNames
{
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public static bool TryParse<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        // Enum.TryParse also accepts numbers, which are no names
        if (text.Length > 0 && Char.IsLetter(text[0]) && Enum.TryParse(text, ignoreCase: true, out value))
            return true;
        value = default;
        return false;
    }
}
