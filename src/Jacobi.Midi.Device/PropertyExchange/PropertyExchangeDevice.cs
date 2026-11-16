using System.Text;
using System.Text.Json.Nodes;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Device.PropertyExchange;

/// <summary>
/// A device found with MIDI-CI that supports Property Exchange: the responder to address, and the resources of its
/// <c>ResourceList</c>. It generates the schema document of its resources (<see cref="GenerateSchema"/>), and a
/// <see cref="DeviceSession"/> transfers the <c>pe</c> roots of that document (or of a saved one) with it.
/// </summary>
public sealed class PropertyExchangeDevice
{
    private const string ResourceListName = "ResourceList";
    private const string JsonSchemaName = "JSONSchema";

    /// <param name="capabilityInquiry">MIDI-CI on the connection to the device.</param>
    /// <param name="responder">The responder that supports Property Exchange.</param>
    /// <param name="resources">The entries of its <c>ResourceList</c>.</param>
    public PropertyExchangeDevice(IMidiCapabilityInquiry capabilityInquiry, MidiMuid responder, IReadOnlyList<PropertyResource> resources)
    {
        ArgumentNullException.ThrowIfNull(capabilityInquiry);
        ArgumentNullException.ThrowIfNull(resources);
        CapabilityInquiry = capabilityInquiry;
        Responder = responder;
        Resources = resources;
    }

    public IMidiCapabilityInquiry CapabilityInquiry { get; }

    public MidiMuid Responder { get; }

    public IReadOnlyList<PropertyResource> Resources { get; }

    /// <summary>The entry of a resource; null when the device does not have it.</summary>
    public PropertyResource? Find(string resource)
        => Resources.FirstOrDefault(entry => String.Equals(entry.Name, resource, StringComparison.Ordinal));

    /// <summary>
    /// Finds the first responder on the connection that supports Property Exchange, and reads its <c>ResourceList</c>;
    /// null when no responder supports it.
    /// </summary>
    /// <exception cref="MidiException">The responder does not answer, or does not give its <c>ResourceList</c>.</exception>
    /// <exception cref="FormatException">The <c>ResourceList</c> is not valid.</exception>
    public static async Task<PropertyExchangeDevice?> Discover(IMidiCapabilityInquiry capabilityInquiry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(capabilityInquiry);
        var responders = await capabilityInquiry.Discover(ct);
        if (responders.FirstOrDefault(responder => responder.SupportsPropertyExchange) is not { } found)
            return null;

        var list = await Get(capabilityInquiry, found.Muid, new JsonObject { ["resource"] = ResourceListName }, ct);
        return new PropertyExchangeDevice(capabilityInquiry, found.Muid, PropertyResource.FromResourceList(list));
    }

    /// <summary>
    /// Generates the schema document of the resources (see <see cref="PropertySchemaGenerator"/>), with the JSON
    /// Schemas the entries refer to read from the device (<c>JSONSchema</c>), and the entries (<c>resId</c>s) of the
    /// resources that the links in the data of the device refer to: the data of every resource that can be read without
    /// a <c>resId</c> is read for that. A referred schema the device does not give, and a resource that could not be
    /// read, are reported in the notes.
    /// </summary>
    /// <param name="documentName">The name of the document; null: after the model of the device (<c>DeviceInfo</c>),
    /// else <c>PropertyExchange</c>.</param>
    public async Task<PropertySchema> GenerateSchema(string? documentName, CancellationToken ct)
    {
        documentName ??= await ModelName(ct) ?? "PropertyExchange";
        var referenced = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var notes = new List<string>();
        foreach (var id in Resources.Select(resource => resource.SchemaReference).OfType<string>().Distinct())
        {
            try
            {
                if (await Get(CapabilityInquiry, Responder, new JsonObject { ["resource"] = JsonSchemaName, ["resId"] = id }, ct) is JsonObject schema)
                    referenced[id] = schema;
            }
            catch (MidiException exception)
            {
                notes.Add($"The JSON Schema '{id}' could not be read: {exception.Message}");
            }
        }
        var resourceIds = await ResourceIds(notes, ct);
        var generated = PropertySchemaGenerator.Generate(documentName, Resources, referenced,
            resourceIds.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value, StringComparer.Ordinal));
        return generated with { Notes = [.. notes, .. generated.Notes] };
    }

    // the resIds the links in the data of the resources refer to, by resource
    private async Task<Dictionary<string, List<string>>> ResourceIds(List<string> notes, CancellationToken ct)
    {
        var resourceIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var resource in Resources.Where(resource => resource is { CanGet: true, RequireResId: false, IsJson: true }))
        {
            try
            {
                PropertyLinks.Collect(await Get(new JsonObject { ["resource"] = resource.Name }, ct), resourceIds);
            }
            catch (Exception exception) when (exception is MidiException or System.Text.Json.JsonException)
            {
                notes.Add($"'{resource.Name}' could not be read for the links to the entries (resId) of resources: {exception.Message}");
            }
        }
        return resourceIds;
    }

    // the model of the device as a schema name; null when it has no DeviceInfo (with a model)
    private async Task<string?> ModelName(CancellationToken ct)
    {
        if (Find("DeviceInfo") is null)
            return null;
        try
        {
            return (await Get(new JsonObject { ["resource"] = "DeviceInfo" }, ct))?["model"]?.GetValue<string>() is { Length: > 0 } model
                ? PropertySchemaGenerator.NameOf(model)
                : null;
        }
        catch (Exception exception) when (exception is MidiException or FormatException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Gets the data of a resource as JSON.</summary>
    /// <exception cref="MidiException">No reply, a NAK, or a status that is not a success.</exception>
    internal async Task<JsonNode?> Get(JsonObject header, CancellationToken ct)
        => await Get(CapabilityInquiry, Responder, header, ct);

    private static async Task<JsonNode?> Get(IMidiCapabilityInquiry capabilityInquiry, MidiMuid responder, JsonObject header, CancellationToken ct)
    {
        var response = await capabilityInquiry.GetProperty(responder, header, ct);
        if (!response.IsSuccess)
        {
            var message = response.Header["message"]?.ToString();
            throw new MidiException($"'{header["resource"]}' of {responder}: status {response.Status}{(message is null ? "" : $" ({message})")}.");
        }
        return response.Body.IsEmpty ? null : JsonNode.Parse(Encoding.UTF8.GetString(response.Body.Span));
    }
}
