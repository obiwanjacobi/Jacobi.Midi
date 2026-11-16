using System.Text.Json.Nodes;

namespace Jacobi.Midi.Device.PropertyExchange;

/// <summary>
/// Collects the entries (<c>resId</c>s) of resources from the data of a device: every object in it with a string
/// <c>resource</c> and a string <c>resId</c> refers to one (the links of M2-103 §13, e.g. in a <c>ChannelList</c>
/// entry). There is no inquiry that lists the <c>resId</c>s of a resource; this is how a device tells them.
/// </summary>
internal static class PropertyLinks
{
    /// <summary>Adds the <c>resId</c>s the data refers to, by resource, in the order found (each once).</summary>
    public static void Collect(JsonNode? data, Dictionary<string, List<string>> resourceIds)
    {
        switch (data)
        {
            case JsonObject json:
                if (Text(json["resource"]) is { Length: > 0 } resource && Text(json["resId"]) is { Length: > 0 } resId)
                {
                    if (!resourceIds.TryGetValue(resource, out var ids))
                        resourceIds[resource] = ids = [];
                    if (!ids.Contains(resId))
                        ids.Add(resId);
                }
                foreach (var (_, value) in json)
                    Collect(value, resourceIds);
                break;
            case JsonArray array:
                foreach (var item in array)
                    Collect(item, resourceIds);
                break;
        }
    }

    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
