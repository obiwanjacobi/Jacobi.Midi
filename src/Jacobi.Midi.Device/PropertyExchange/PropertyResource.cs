using System.Text.Json.Nodes;

namespace Jacobi.Midi.Device.PropertyExchange;

/// <summary>
/// One entry of the <c>ResourceList</c> of a Property Exchange device (M2-103 section 14.2): the resource, what can be
/// done with it and its JSON Schema. A property that is not given has its default value of the specification.
/// </summary>
/// <param name="Name">The name of the resource, e.g. <c>DeviceInfo</c> or <c>X-ProgramEdit</c>.</param>
/// <param name="Schema">The JSON Schema of the resource as the list gives it; null when it gives none (the resources
/// of the specifications, whose schemas are published).</param>
public sealed record PropertyResource(string Name, JsonObject? Schema)
{
    private const string JsonMediaType = "application/json";
    private const string SchemaReferencePrefix = "midi+jsonschema://";

    /// <summary>The <c>canSet</c> value of a resource that cannot be written (the default).</summary>
    public const string CanSetNone = "none";

    /// <summary>The <c>canSet</c> value of a resource that can only be written whole.</summary>
    public const string CanSetFull = "full";

    /// <summary>The resource can be read (<c>canGet</c>, default true).</summary>
    public bool CanGet { get; init; } = true;

    /// <summary>How the resource can be written (<c>canSet</c>): <c>none</c> (default), <c>full</c> or <c>partial</c>.</summary>
    public string CanSet { get; init; } = CanSetNone;

    /// <summary>The resource accepts subscriptions (<c>canSubscribe</c>, default false).</summary>
    public bool CanSubscribe { get; init; }

    /// <summary>An inquiry must name an entry of the resource (<c>requireResId</c>, default false).</summary>
    public bool RequireResId { get; init; }

    /// <summary>The media types of the data (<c>mediaTypes</c>, default <c>application/json</c>).</summary>
    public IReadOnlyList<string> MediaTypes { get; init; } = [JsonMediaType];

    /// <summary>True when the data of the resource is JSON.</summary>
    public bool IsJson => MediaTypes.Any(type => String.Equals(type, JsonMediaType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The id of the JSON Schema the device holds for the resource (<c>"$ref": "midi+jsonschema://id"</c>), to get with
    /// the <c>JSONSchema</c> resource; null when the schema is given in full or not at all.
    /// </summary>
    public string? SchemaReference
        => Schema?["$ref"]?.GetValue<string>() is { } reference && reference.StartsWith(SchemaReferencePrefix, StringComparison.OrdinalIgnoreCase)
            ? reference[SchemaReferencePrefix.Length..]
            : null;

    /// <summary>The entries of the data of a <c>ResourceList</c> reply.</summary>
    /// <exception cref="FormatException">The data is not an array of resource objects.</exception>
    public static IReadOnlyList<PropertyResource> FromResourceList(JsonNode? resourceList)
    {
        if (resourceList is not JsonArray entries)
            throw new FormatException("A ResourceList is an array of objects.");
        var resources = new List<PropertyResource>();
        foreach (var entry in entries)
        {
            if (entry is not JsonObject properties || properties["resource"]?.GetValue<string>() is not { Length: > 0 } name)
                throw new FormatException($"A ResourceList entry needs a 'resource': {entry?.ToJsonString()}");
            resources.Add(new PropertyResource(name, properties["schema"] as JsonObject)
            {
                CanGet = properties["canGet"]?.GetValue<bool>() ?? true,
                CanSet = properties["canSet"]?.GetValue<string>() ?? CanSetNone,
                CanSubscribe = properties["canSubscribe"]?.GetValue<bool>() ?? false,
                RequireResId = properties["requireResId"]?.GetValue<bool>() ?? false,
                MediaTypes = properties["mediaTypes"] is JsonArray types
                    ? [.. types.Select(type => type?.GetValue<string>()).OfType<string>()]
                    : [JsonMediaType],
            });
        }
        return resources;
    }
}
