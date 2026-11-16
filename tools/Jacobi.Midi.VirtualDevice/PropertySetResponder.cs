using System.Text;
using System.Text.Json.Nodes;
using Windows.Devices.Midi2;
using Windows.Devices.Midi2.CapabilityInquiry;

namespace Jacobi.Midi.VirtualDevice;

/// <summary>
/// Answers Set Property Data (MIDI-CI 0x36) for the virtual device, which the platform's responder leaves to the
/// application: assembles the chunks of an inquiry, applies the data to the resource (whole, or with <c>setPartial</c>
/// the values by JSON Pointer), publishes the new data to the responder (so a Get and the subscribers see it) and
/// replies (0x37) with the status. It keeps the data of the resources it can set.
/// </summary>
internal sealed class PropertySetResponder(MidiCapabilityInquiryDeviceResponder responder, MidiEndpointConnection connection)
{
    // the largest SysEx message of the reply: it only holds a small header
    private const uint ReplyMaximumSystemExclusiveSize = 512;

    private readonly Lock _lock = new();
    private readonly Dictionary<(string Resource, string ResId), JsonNode?> _data = [];
    private readonly Dictionary<string, string> _canSet = [];
    private readonly Dictionary<(uint Initiator, byte RequestId), Pending> _pending = [];

    /// <summary>Publishes a resource with its data per resource id (an empty id for a resource without entries).</summary>
    public void Publish(CustomResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_lock)
        {
            _canSet[resource.Name] = resource.CanSet;
            foreach (var (resId, json) in resource.Data)
            {
                _data[(resource.Name, resId)] = JsonNode.Parse(json);
                responder.SetResource(resource.Name, resId, json);
            }
        }
    }

    /// <summary>Handles a capability inquiry message the platform's responder did not answer; true for a Set.</summary>
    public bool Handle(MidiCapabilityInquiryMessage message, MidiGroup group)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.MessageType != MidiCapabilityInquiryMessageType.PropertySetDataInquiry || !message.HasPropertyExchangeFields)
        {
            return false;
        }

        var key = (message.SourceMuid.AsCombined28BitValue, message.RequestId);
        Pending pending;
        lock (_lock)
        {
            if (message.ChunkNumber <= 1 || !_pending.TryGetValue(key, out pending!))
            {
                pending = new Pending(message.HeaderText);
                _pending[key] = pending;
            }
            pending.Body.AddRange(message.Body);
            if (message.ChunkNumber < message.ChunkCount)
            {
                return true;
            }
            _pending.Remove(key);
        }

        var (status, text) = Apply(pending.Header, Encoding.UTF8.GetString([.. pending.Body]));
        Console.WriteLine($"Set {pending.Header}: {status}{(text is null ? String.Empty : $" ({text})")}");
        Reply(message, group, status, text);
        return true;
    }

    // the data of the Set into the resource; the status of the reply and a message for a failure
    private (int Status, string? Message) Apply(string headerText, string bodyText)
    {
        JsonObject header;
        JsonNode? body;
        try
        {
            header = JsonNode.Parse(headerText)?.AsObject() ?? throw new FormatException("The header is not a JSON object.");
            body = bodyText.Length == 0 ? null : JsonNode.Parse(bodyText);
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return (400, exception.Message);
        }

        var resource = header["resource"]?.GetValue<string>() ?? String.Empty;
        var resId = header["resId"]?.GetValue<string>() ?? String.Empty;
        var partial = header["setPartial"]?.GetValue<bool>() ?? false;
        lock (_lock)
        {
            if (!_canSet.TryGetValue(resource, out var canSet) || !_data.TryGetValue((resource, resId), out var current))
            {
                return (404, $"There is no resource '{resource}'{(resId.Length == 0 ? String.Empty : $" with resId '{resId}'")}.");
            }
            if (canSet == "none" || (partial && canSet != "partial"))
            {
                return (405, $"'{resource}' cannot be set{(partial ? " partially" : String.Empty)}.");
            }

            JsonNode? data;
            if (partial)
            {
                if (body is not JsonObject values)
                {
                    return (400, "A partial Set needs an object of JSON Pointers and values.");
                }
                data = current?.DeepClone();
                foreach (var (pointer, value) in values)
                {
                    if (!JsonPointer.TrySet(ref data, pointer, value?.DeepClone(), out var error))
                    {
                        return (400, $"'{pointer}': {error}");
                    }
                }
            }
            else
            {
                data = body;
            }

            _data[(resource, resId)] = data;
            responder.SetResource(resource, resId, data?.ToJsonString() ?? String.Empty);
        }
        responder.NotifyResourceChanged(resource, resId);
        return (200, null);
    }

    // the reply to Set (0x37) with the status
    private void Reply(MidiCapabilityInquiryMessage inquiry, MidiGroup group, int status, string? text)
    {
        var header = new Windows.Data.Json.JsonObject
        {
            ["status"] = Windows.Data.Json.JsonValue.CreateNumberValue(status),
        };
        if (text is not null)
        {
            header["message"] = Windows.Data.Json.JsonValue.CreateStringValue(text);
        }
        var packets = MidiCapabilityInquiryMessageBuilder.BuildPropertyMessage(0, group,
            MidiCapabilityInquiryMessageType.PropertySetDataInquiryReply, responder.GetMuid(0), inquiry.SourceMuid,
            inquiry.RequestId, header, [], ReplyMaximumSystemExclusiveSize);
        foreach (var packet in packets)
        {
            connection.SendSingleMessagePacket(packet);
        }
    }

    // a Set whose chunks are arriving: the header of the first chunk and the body so far
    private sealed class Pending(string header)
    {
        public string Header { get; } = header;
        public List<byte> Body { get; } = [];
    }
}
