using System.Globalization;
using System.Text.Json.Nodes;

namespace Jacobi.Midi.VirtualDevice;

/// <summary>
/// Sets a value at a JSON Pointer (RFC 6901), as a partial Set of Property Exchange addresses it (M2-103 section 10):
/// an existing member or array element is replaced, a missing member of an object is added.
/// </summary>
internal static class JsonPointer
{
    /// <summary>Sets the value at the pointer in the data; false (with the reason) when the pointer does not fit the data.</summary>
    public static bool TrySet(ref JsonNode? data, string pointer, JsonNode? value, out string? error)
    {
        error = null;
        if (pointer.Length == 0)
        {
            data = value;
            return true;
        }
        if (pointer[0] != '/')
        {
            error = "a JSON Pointer starts with '/'.";
            return false;
        }

        var tokens = pointer[1..].Split('/').Select(token => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToList();
        var node = data;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var last = index == tokens.Count - 1;
            switch (node)
            {
                case JsonObject json:
                    if (last)
                    {
                        json[token] = value;
                        return true;
                    }
                    node = json[token];
                    break;

                case JsonArray array:
                    if (!Int32.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var item) || item >= array.Count)
                    {
                        error = $"'{token}' is not an element of the array.";
                        return false;
                    }
                    if (last)
                    {
                        array[item] = value;
                        return true;
                    }
                    node = array[item];
                    break;

                default:
                    error = $"'{token}' is below a value.";
                    return false;
            }
        }
        return true;
    }
}
