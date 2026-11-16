using System.Text.Json.Nodes;

namespace Jacobi.Midi.VirtualDevice;

/// <summary>
/// The manufacturer specific Property Exchange resources the virtual device publishes, next to the standard ones
/// (<c>DeviceInfo</c>, <c>ChannelList</c>, <c>ProgramList</c>): one per kind of Set the Device API handles, with
/// its JSON Schema in the <c>ResourceList</c> and its initial data per resource id.
/// </summary>
/// <param name="Name">The resource name.</param>
/// <param name="CanSet"><c>none</c>, <c>full</c> or <c>partial</c>.</param>
/// <param name="RequireResId">An inquiry must name an entry (<c>resId</c>).</param>
/// <param name="Schema">The JSON Schema of the data.</param>
/// <param name="Data">The initial data by resource id (empty: a resource without entries).</param>
internal sealed record CustomResource(string Name, string CanSet, bool RequireResId, string Schema, IReadOnlyDictionary<string, string> Data)
{
    public static IReadOnlyList<CustomResource> All { get; } =
    [
        // a single value, set whole
        new("X-Tempo", "full", RequireResId: false,
            """{"title":"Tempo","description":"The tempo in BPM.","type":"integer","minimum":40,"maximum":240}""",
            new Dictionary<string, string> { [String.Empty] = "120" }),

        // an object, set whole; its links name the entries of X-ProgramEdit
        new("X-Globset", "full", RequireResId: false,
            """
            {"title":"Global Settings","type":"object","properties":{
              "deviceName":{"type":"string","description":"The name of the device."},
              "level":{"type":"integer","minimum":0,"maximum":127,"description":"The master level."},
              "audioOut":{"type":"string","enum":["USB","line"]},
              "links":{"type":"array","items":{"type":"object","properties":{"resource":{"type":"string"},"resId":{"type":"string"}}}}}}
            """,
            new Dictionary<string, string>
            {
                [String.Empty] = """
                    {"deviceName":"Jacobi Test Device","level":100,"audioOut":"USB",
                     "links":[{"resource":"X-ProgramEdit","resId":"patch1"},{"resource":"X-ProgramEdit","resId":"patch2"}]}
                    """,
            }),

        // entries (resId), set partially by JSON Pointer
        new("X-ProgramEdit", "partial", RequireResId: true,
            """
            {"title":"Program Edit","type":"object","properties":{
              "name":{"type":"string","description":"The name of the program."},
              "lfoSpeed":{"type":"integer","minimum":0,"maximum":127},
              "lfoWaveform":{"type":"string","enum":["triangle","square","saw"]},
              "rates":{"type":"array","minItems":4,"maxItems":4,"items":{"type":"integer","minimum":0,"maximum":127}}}}
            """,
            new Dictionary<string, string>
            {
                ["patch1"] = """{"name":"Warm Pad","lfoSpeed":20,"lfoWaveform":"triangle","rates":[10,40,80,127]}""",
                ["patch2"] = """{"name":"Bright Lead","lfoSpeed":90,"lfoWaveform":"saw","rates":[0,20,60,100]}""",
            }),
    ];

    /// <summary>The JSON Schema, parsed.</summary>
    public JsonObject SchemaJson => JsonNode.Parse(Schema)!.AsObject();
}
