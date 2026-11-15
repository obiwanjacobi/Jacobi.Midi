using System.Text.Json.Nodes;

namespace Jacobi.Midi.Device.PropertyExchange;

/// <summary>
/// The published JSON Schemas of the standard Property Exchange resources (M2-105 Foundational Resources, M2-117
/// controller resources), which a device does not repeat in its <c>ResourceList</c>. Embedded in the assembly
/// (<c>PropertyExchange/*.jsonschema</c>, from the MIT licensed MIDI 2.0 Workbench of MIDI2.dev); they are not schema
/// documents, so whoever loads the embedded schemas of an assembly leaves them out (<see cref="IsResource"/>).
/// </summary>
public static class StandardSchemas
{
    private const string Extension = ".jsonschema";

    // the resource name and the file of its schema
    private static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DeviceInfo"] = "M2-105-S_v1-0_DeviceInfo",
        ["ChannelList"] = "M2-105-S_v1-1_ChannelList",
        ["ProgramList"] = "M2-105-S_v1-0_ProgramList",
        ["ChCtrlList"] = "M2-117-S_v1-0_ChCtrlList",
    };

    /// <summary>The names of the resources with a published schema.</summary>
    public static IEnumerable<string> Resources => Files.Keys;

    /// <summary>True when an embedded resource is one of these JSON Schemas, not a schema document.</summary>
    public static bool IsResource(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return resourceName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The published JSON Schema of a standard resource (a copy); null for any other resource.</summary>
    public static JsonObject? Find(string resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!Files.TryGetValue(resource, out var file))
            return null;
        using var stream = typeof(StandardSchemas).Assembly.GetManifestResourceStream($"PropertyExchange/{file}{Extension}")
            ?? throw new InvalidOperationException($"The JSON Schema of '{resource}' is not embedded.");
        return JsonNode.Parse(stream)?.AsObject();
    }
}
