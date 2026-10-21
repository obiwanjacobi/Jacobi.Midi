using System.Diagnostics.CodeAnalysis;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Messages.V2;

/// <summary>
/// Reassembles the SysEx7 UMPs of one group into complete MIDI 1.0 SysEx messages (framed by F0/F7):
/// the reverse of <see cref="SysEx7Message.FromSystemExclusive"/>.
/// </summary>
/// <remarks>
/// A Start packet begins a new message (an unfinished one is discarded); Continue and End packets
/// without a preceding Start are ignored. Packets of other groups and other message kinds are ignored.
/// Not thread-safe: feed the packets of one stream in order.
/// </remarks>
public sealed class SysEx7Assembler
{
    private readonly List<byte> _payload = [];
    private bool _started;

    /// <param name="group">The UMP group (0-15) whose packets are assembled.</param>
    public SysEx7Assembler(byte group)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(group, (byte)15);
        Group = group;
    }

    /// <summary>The UMP group whose packets are assembled.</summary>
    public byte Group { get; }

    /// <summary>True while a message has started (a Start packet) but not ended.</summary>
    public bool IsPending => _started;

    /// <summary>
    /// Adds a packet; ignores anything that is not a SysEx7 UMP (64-bit, message type 3).
    /// </summary>
    /// <param name="sysEx">The complete message (F0, payload, F7) when the packet completes one.</param>
    /// <returns>True when the packet completed a message.</returns>
    public bool TryAdd(IUniversalPacket packet, [NotNullWhen(true)] out byte[]? sysEx)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.PacketKind != PacketKind.Packet64 || packet[0] >> 28 != 3)
        {
            sysEx = null;
            return false;
        }
        return TryAdd(new SysEx7Message(packet[0], packet[1]), out sysEx);
    }

    /// <inheritdoc cref="TryAdd(IUniversalPacket, out byte[])"/>
    public bool TryAdd(SysEx7Message packet, [NotNullWhen(true)] out byte[]? sysEx)
    {
        sysEx = null;
        if (packet.Group != Group)
        {
            return false;
        }

        switch (packet.Status)
        {
            case SysExPacketStatus.Complete:
                _payload.Clear();
                _started = false;
                Append(packet);
                sysEx = Complete();
                return true;
            case SysExPacketStatus.Start:
                _payload.Clear();
                _started = true;
                Append(packet);
                return false;
            case SysExPacketStatus.Continue when _started:
                Append(packet);
                return false;
            case SysExPacketStatus.End when _started:
                Append(packet);
                _started = false;
                sysEx = Complete();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Discards an unfinished message.</summary>
    public void Reset()
    {
        _payload.Clear();
        _started = false;
    }

    private void Append(SysEx7Message packet)
    {
        for (var index = 0; index < packet.PayloadLength; index++)
        {
            _payload.Add(packet.GetPayloadByte(index));
        }
    }

    private byte[] Complete()
    {
        var bytes = new byte[_payload.Count + 2];
        bytes[0] = 0xF0;
        _payload.CopyTo(bytes, 1);
        bytes[^1] = 0xF7;
        _payload.Clear();
        return bytes;
    }
}
