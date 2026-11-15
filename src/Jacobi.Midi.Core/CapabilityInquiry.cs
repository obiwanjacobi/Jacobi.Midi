using System.Globalization;
using System.Text.Json.Nodes;

namespace Jacobi.Midi.Core;

/// <summary>
/// MIDI Capability Inquiry (MIDI-CI, M2-101) on one connection, as the initiator: finding the responders (devices,
/// function blocks) and Property Exchange with them (M2-103). Raw by design: a property request is a JSON header and
/// a body, as the specification puts them on the wire; the platform does the messages, request ids, chunking,
/// replies and timeouts. Created with <see cref="IMidiApi.CreateCapabilityInquiry"/>; it does not own the connection.
/// Disposing it ends its subscriptions.
/// </summary>
public interface IMidiCapabilityInquiry : IAsyncDisposable
{
    /// <summary>The identifier of this initiator on the connection.</summary>
    MidiMuid Muid { get; }

    /// <summary>
    /// Finds the responders on the connection: waits the platform's discovery time for every answer. Empty when
    /// nothing answers (the endpoint does not implement MIDI-CI).
    /// </summary>
    Task<IReadOnlyList<MidiCiResponder>> Discover(CancellationToken ct);

    /// <summary>
    /// Gets the data of a resource (Inquiry: Get Property Data). The header holds at least <c>resource</c>, and e.g.
    /// <c>resId</c>, <c>offset</c>/<c>limit</c> (M2-103 section 7.2).
    /// </summary>
    /// <exception cref="MidiException">The responder did not reply, or replied with a NAK.</exception>
    Task<MidiPropertyResponse> GetProperty(MidiMuid responder, JsonObject header, CancellationToken ct);

    /// <summary>
    /// Sets the data of a resource (Inquiry: Set Property Data): the whole resource, or with <c>setPartial</c> in the
    /// header the values at the JSON Pointers of the body (M2-103 section 10).
    /// </summary>
    /// <exception cref="MidiException">The responder did not reply, or replied with a NAK.</exception>
    Task<MidiPropertyResponse> SetProperty(MidiMuid responder, JsonObject header, ReadOnlyMemory<byte> body, CancellationToken ct);

    /// <summary>
    /// Starts a subscription to a resource (the header as for <see cref="GetProperty"/>); its updates are raised with
    /// <see cref="SubscriptionUpdated"/>. Only for a resource the responder declares subscribable (<c>canSubscribe</c>).
    /// </summary>
    /// <exception cref="MidiException">The responder did not reply, refused, or replied with a NAK.</exception>
    Task<MidiPropertySubscription> Subscribe(MidiMuid responder, JsonObject header, CancellationToken ct);

    /// <summary>Ends a subscription (<c>command</c> <c>end</c>, by the initiator).</summary>
    Task Unsubscribe(MidiPropertySubscription subscription, CancellationToken ct);

    /// <summary>
    /// A responder sent an update of a subscription: the data (full or partial), a request to get it again (notify),
    /// or the end. Raised on a background thread.
    /// </summary>
    event EventHandler<MidiPropertySubscriptionUpdate>? SubscriptionUpdated;
}

/// <summary>
/// The MIDI-CI identifier of an initiator or responder (MUID): 28 bits, picked at random by each device and not kept
/// across a restart.
/// </summary>
public readonly record struct MidiMuid
{
    /// <summary>The largest value: the broadcast MUID (0x0FFFFFFF), addressed to every device.</summary>
    public const uint BroadcastValue = 0x0FFFFFFF;

    public MidiMuid(uint value)
    {
        if (value > BroadcastValue)
            throw new ArgumentOutOfRangeException(nameof(value), value, "A MUID has 28 bits.");
        Value = value;
    }

    /// <summary>The 28-bit value.</summary>
    public uint Value { get; }

    /// <summary>The broadcast MUID: addressed to every device.</summary>
    public static MidiMuid Broadcast { get; } = new(BroadcastValue);

    public bool IsBroadcast => Value == BroadcastValue;

    public override string ToString() => Value.ToString("X7", CultureInfo.InvariantCulture);
}

/// <summary>
/// A responder found by <see cref="IMidiCapabilityInquiry.Discover"/>: a device, or one function block of it.
/// </summary>
/// <param name="Muid">The identifier to address it with.</param>
/// <param name="FunctionBlock">The function block it answers for (0x7F: the whole device, or not given).</param>
/// <param name="SupportsPropertyExchange">True when it declares Property Exchange.</param>
/// <param name="MaxSysExSize">The largest SysEx message, in bytes, it can receive.</param>
public sealed record MidiCiResponder(MidiMuid Muid, byte FunctionBlock, bool SupportsPropertyExchange, int MaxSysExSize);

/// <summary>
/// The reply to a Property Exchange inquiry: the status and the header of the reply, and its body (JSON text, or the
/// bytes of a resource with a <c>mediaType</c>).
/// </summary>
/// <param name="Status">The status of the reply header (M2-103 section 7.4.1), e.g. 200 OK, 404 resource unsupported.</param>
/// <param name="Header">The whole header of the reply (e.g. <c>totalCount</c>, <c>cacheTime</c>, <c>message</c>).</param>
/// <param name="Body">The property data; empty when there is none (e.g. the reply to a set).</param>
public sealed record MidiPropertyResponse(int Status, JsonObject Header, ReadOnlyMemory<byte> Body)
{
    /// <summary>True for a status that tells the inquiry succeeded (2xx).</summary>
    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>
/// A running subscription to a resource of a responder (see <see cref="IMidiCapabilityInquiry.Subscribe"/>).
/// </summary>
/// <param name="Responder">The responder of the resource.</param>
/// <param name="SubscribeId">The id the responder gave the subscription.</param>
/// <param name="Header">The header the subscription was started with (<c>resource</c>, <c>resId</c>).</param>
public sealed record MidiPropertySubscription(MidiMuid Responder, string SubscribeId, JsonObject Header);

/// <summary>What a subscription update says (the <c>command</c> of its header, M2-103 section 11).</summary>
public enum MidiSubscriptionCommand
{
    /// <summary>The whole data of the resource, in the body.</summary>
    Full,
    /// <summary>Some values, in the body as JSON Pointers with their values (as a partial set).</summary>
    Partial,
    /// <summary>The data changed: get it again (no body).</summary>
    Notify,
    /// <summary>The responder ended the subscription.</summary>
    End,
}

/// <summary>An update of a subscription (see <see cref="IMidiCapabilityInquiry.SubscriptionUpdated"/>).</summary>
public sealed class MidiPropertySubscriptionUpdate(MidiPropertySubscription subscription, MidiSubscriptionCommand command,
    JsonObject header, ReadOnlyMemory<byte> body) : EventArgs
{
    public MidiPropertySubscription Subscription { get; } = subscription;
    public MidiSubscriptionCommand Command { get; } = command;

    /// <summary>The whole header of the update.</summary>
    public JsonObject Header { get; } = header;

    /// <summary>The data of a <see cref="MidiSubscriptionCommand.Full"/> or <see cref="MidiSubscriptionCommand.Partial"/> update; empty otherwise.</summary>
    public ReadOnlyMemory<byte> Body { get; } = body;
}
