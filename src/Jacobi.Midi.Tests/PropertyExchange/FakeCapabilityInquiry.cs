using System.Text;
using System.Text.Json.Nodes;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Tests.PropertyExchange;

/// <summary>
/// MIDI-CI with one responder that supports Property Exchange and answers a Get with the data of its resources, as
/// the test gives them (by resource name); a resource it does not have gets status 404. A Set is recorded and answered
/// with <see cref="SetStatus"/> (the data of the resource does not change).
/// </summary>
internal sealed class FakeCapabilityInquiry(IReadOnlyDictionary<string, string> resources) : IMidiCapabilityInquiry
{
    public static readonly MidiMuid ResponderMuid = new(0x0123456);

    public MidiMuid Muid { get; } = new(0x0654321);

    /// <summary>The headers of the Get inquiries, in order.</summary>
    public List<JsonObject> Gets { get; } = [];

    public event EventHandler<MidiPropertySubscriptionUpdate>? SubscriptionUpdated { add { } remove { } }

    public Task<IReadOnlyList<MidiCiResponder>> Discover(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MidiCiResponder>>([new MidiCiResponder(ResponderMuid, 0, SupportsPropertyExchange: true, 1024)]);

    public Task<MidiPropertyResponse> GetProperty(MidiMuid responder, JsonObject header, CancellationToken ct)
    {
        Gets.Add(header);
        var resource = header["resource"]!.GetValue<string>();
        var key = header["resId"] is { } id ? $"{resource}/{id}" : resource;
        return Task.FromResult(resources.TryGetValue(key, out var json)
            ? new MidiPropertyResponse(200, new JsonObject { ["status"] = 200 }, Encoding.UTF8.GetBytes(json))
            : new MidiPropertyResponse(404, new JsonObject { ["status"] = 404 }, ReadOnlyMemory<byte>.Empty));
    }

    /// <summary>The headers and bodies of the Set inquiries, in order.</summary>
    public List<(JsonObject Header, JsonNode? Body)> Sets { get; } = [];

    /// <summary>The status a Set is answered with.</summary>
    public int SetStatus { get; set; } = 200;

    public Task<MidiPropertyResponse> SetProperty(MidiMuid responder, JsonObject header, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        Sets.Add((header, body.IsEmpty ? null : JsonNode.Parse(Encoding.UTF8.GetString(body.Span))));
        return Task.FromResult(new MidiPropertyResponse(SetStatus, new JsonObject { ["status"] = SetStatus }, ReadOnlyMemory<byte>.Empty));
    }

    public Task<MidiPropertySubscription> Subscribe(MidiMuid responder, JsonObject header, CancellationToken ct)
        => throw new NotSupportedException();

    public Task Unsubscribe(MidiPropertySubscription subscription, CancellationToken ct) => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
