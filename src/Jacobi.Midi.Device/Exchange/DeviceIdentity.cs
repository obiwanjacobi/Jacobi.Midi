using Jacobi.Midi.Core;
using Jacobi.Midi.Messages;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Asks the devices on an endpoint to identify themselves: a universal Identity Request on one UMP group, over a
/// connection of its own, and the Identity Replies that come back. Independent of the schemas, so it also works for a
/// device without an address map; older devices do not answer it.
/// </summary>
public static class DeviceIdentity
{
    /// <summary>The device id that asks all devices.</summary>
    public const byte AllDevices = 0x7F;

    /// <summary>
    /// Sends an Identity Request and collects the replies that arrive within <paramref name="wait"/>; several devices
    /// can answer (with <see cref="AllDevices"/>). No reply is an empty list.
    /// </summary>
    public static async Task<IReadOnlyList<IdentityReplyMessage>> Request(IMidiSession session, string endpointId, byte group, byte deviceId,
        TimeSpan wait, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(endpointId);

        var replies = new List<IdentityReplyMessage>();
        await using var port = new MidiConnectionSysExPort(group);
        port.Received += sysEx =>
        {
            if (TryReadReply(sysEx, out var reply))
            {
                lock (replies)
                {
                    replies.Add(reply);
                }
            }
        };
        port.Connection = await session.Connect(endpointId, new MidiConnectionOptions(), port.OnPacketsReceived, ct);

        await port.Send(IdentityRequestMessage.Encode(deviceId), ct);
        await Task.Delay(wait, ct);
        lock (replies)
        {
            return [.. replies];
        }
    }

    private static bool TryReadReply(ReadOnlyMemory<byte> sysEx, out IdentityReplyMessage reply)
    {
        reply = default;
        if (!new SystemExclusiveMessage(sysEx).TryGetUniversal(out var universal) || !universal.Value.TryGetValue(out IdentityReplyMessage? found))
        {
            return false;
        }
        reply = found.Value;
        return true;
    }
}
