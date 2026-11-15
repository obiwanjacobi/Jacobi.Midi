using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jacobi.Midi.Core;
using Ci = Windows.Devices.Midi2.CapabilityInquiry;
using Native = Windows.Devices.Midi2;
using NativeJson = Windows.Data.Json;

namespace Jacobi.Midi.Windows;

/// <summary>
/// MIDI Capability Inquiry on a connection of Windows MIDI Services: the Core abstraction over the SDK's
/// <see cref="Ci.MidiCapabilityInquirySession"/>, which does the messages, request ids, chunking and timeouts. JSON
/// headers cross over as text, between <c>System.Text.Json</c> and <c>Windows.Data.Json</c>. The session does not own
/// the connection; disposing this closes the session (its MUID is withdrawn, its subscriptions end).
/// </summary>
internal sealed class WindowsCapabilityInquiry : IMidiCapabilityInquiry
{
    private const string ResourceKey = "resource";
    private const string ResourceIdKey = "resId";

    private readonly Ci.MidiCapabilityInquirySession _session;
    // the native subscriptions by subscribe id, with the subscription the client got
    private readonly ConcurrentDictionary<string, (Ci.MidiPropertySubscription Native, MidiPropertySubscription Subscription)> _subscriptions = new();

    internal WindowsCapabilityInquiry(Native.MidiEndpointConnection connection, MidiGroup group)
    {
        if (group.Value > 15)
            throw new ArgumentOutOfRangeException(nameof(group), group.Value, "A UMP group is from 0 to 15.");
        _session = Ci.MidiCapabilityInquirySession.Create(connection)
            ?? throw new MidiException("Windows MIDI Services could not start a capability inquiry session on the connection.");
        _session.Group = new Native.MidiGroup(group.Value);
        _session.PropertySubscriptionUpdated += OnSubscriptionUpdated;
    }

    public MidiMuid Muid => ToMuid(_session.SourceMuid);

    public event EventHandler<MidiPropertySubscriptionUpdate>? SubscriptionUpdated;

    public async Task<IReadOnlyList<MidiCiResponder>> Discover(CancellationToken ct)
    {
        var responders = await _session.DiscoverAsync().AsTask(ct).ConfigureAwait(false);
        return [.. responders.Select(responder => new MidiCiResponder(ToMuid(responder.Muid), responder.FunctionBlockNumber,
            responder.SupportsPropertyExchange, (int)responder.ReceivableMaximumSystemExclusiveSize))];
    }

    public async Task<MidiPropertyResponse> GetProperty(MidiMuid responder, JsonObject header, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(header);
        var response = await _session.GetPropertyDataAsync(ToNative(responder), ToNative(header)).AsTask(ct).ConfigureAwait(false);
        return ToResponse("Get Property Data", header, response);
    }

    public async Task<MidiPropertyResponse> SetProperty(MidiMuid responder, JsonObject header, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(header);
        var response = await _session.SetPropertyDataAsync(ToNative(responder), ToNative(header), body.ToArray()).AsTask(ct).ConfigureAwait(false);
        return ToResponse("Set Property Data", header, response);
    }

    /// <remarks>The SDK subscribes by resource and resource id only: a header with other fields is refused.</remarks>
    public async Task<MidiPropertySubscription> Subscribe(MidiMuid responder, JsonObject header, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.Select(property => property.Key).FirstOrDefault(key => key is not (ResourceKey or ResourceIdKey)) is { } other)
            throw new ArgumentException($"Windows MIDI Services subscribes by '{ResourceKey}' and '{ResourceIdKey}' only, not with '{other}'.", nameof(header));
        var resource = header[ResourceKey]?.GetValue<string>()
            ?? throw new ArgumentException($"The header has no '{ResourceKey}'.", nameof(header));
        var resourceId = header[ResourceIdKey]?.GetValue<string>() ?? String.Empty;

        var native = await _session.SubscribeAsync(ToNative(responder), resource, resourceId).AsTask(ct).ConfigureAwait(false);
        if (native is null || native.Status != Ci.MidiCapabilityInquiryStatus.Success || native.ResourceStatus is < 200 or >= 300)
        {
            throw new MidiException($"The subscription to '{resource}' of {responder} was not started: "
                + (native is null ? "no subscription." : $"{native.Status}, resource status {native.ResourceStatus}."));
        }
        var subscription = new MidiPropertySubscription(responder, native.SubscribeId, (JsonObject)header.DeepClone());
        _subscriptions[native.SubscribeId] = (native, subscription);
        return subscription;
    }

    public async Task Unsubscribe(MidiPropertySubscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (!_subscriptions.TryRemove(subscription.SubscribeId, out var known))
            return;
        if (!await _session.UnsubscribeAsync(known.Native).AsTask(ct).ConfigureAwait(false))
            throw new MidiException($"The subscription '{subscription.SubscribeId}' of {subscription.Responder} could not be ended.");
    }

    public ValueTask DisposeAsync()
    {
        _session.PropertySubscriptionUpdated -= OnSubscriptionUpdated;
        _subscriptions.Clear();
        _session.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnSubscriptionUpdated(Ci.MidiCapabilityInquirySession sender, Ci.MidiPropertySubscriptionUpdatedEventArgs args)
    {
        var native = args.Subscription;
        var subscription = _subscriptions.TryGetValue(native.SubscribeId, out var known)
            ? known.Subscription
            : new MidiPropertySubscription(ToMuid(native.ResponderMuid), native.SubscribeId,
                new JsonObject { [ResourceKey] = native.Resource, [ResourceIdKey] = native.ResourceId });
        var command = args.IsSubscriptionEnded ? MidiSubscriptionCommand.End : args.Command switch
        {
            "full" => MidiSubscriptionCommand.Full,
            "partial" => MidiSubscriptionCommand.Partial,
            "notify" => MidiSubscriptionCommand.Notify,
            _ => MidiSubscriptionCommand.End,
        };
        if (command == MidiSubscriptionCommand.End)
            _subscriptions.TryRemove(native.SubscribeId, out _);

        var update = args.Update;
        SubscriptionUpdated?.Invoke(this, new MidiPropertySubscriptionUpdate(subscription, command,
            ToHeader(update?.HeaderText), update?.Body is { } body ? body.ToArray() : ReadOnlyMemory<byte>.Empty));
    }

    // a reply that came: its resource status, header and body; no reply or a NAK is an exception. The SDK reports a
    // reply with an error status (e.g. 404) as a negative acknowledgment too; it has a header, a NAK has none
    private static MidiPropertyResponse ToResponse(string inquiry, JsonObject request, Ci.MidiPropertyExchangeResponse? response)
    {
        if (response is null)
            throw new MidiException($"{inquiry} of '{request[ResourceKey]}' gave no response.");
        var isReply = response.Status == Ci.MidiCapabilityInquiryStatus.Success
            || (response.Status == Ci.MidiCapabilityInquiryStatus.NegativeAcknowledgment && response.ResourceStatus > 0);
        return response.Status switch
        {
            _ when isReply
                => new MidiPropertyResponse(response.ResourceStatus, ToHeader(response.HeaderText), response.Body?.ToArray() ?? []),
            Ci.MidiCapabilityInquiryStatus.Canceled
                => throw new OperationCanceledException($"{inquiry} of '{request[ResourceKey]}' was canceled."),
            Ci.MidiCapabilityInquiryStatus.NegativeAcknowledgment
                => throw new MidiException($"{inquiry} of '{request[ResourceKey]}' was refused (NAK 0x{response.NakStatusCode:X2}): {response.NakStatusMessage}"),
            var status
                => throw new MidiException($"{inquiry} of '{request[ResourceKey]}' failed: {status}."),
        };
    }

    private static JsonObject ToHeader(string? text)
        => String.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text) as JsonObject ?? [];

    private static NativeJson.JsonObject ToNative(JsonObject header) => NativeJson.JsonObject.Parse(header.ToJsonString());

    private static Ci.MidiUniqueId ToNative(MidiMuid muid) => new(muid.Value);

    private static MidiMuid ToMuid(Ci.MidiUniqueId muid) => new(muid.AsCombined28BitValue);
}
