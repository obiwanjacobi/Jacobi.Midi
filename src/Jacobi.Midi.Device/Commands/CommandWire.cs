using Jacobi.Midi.Core;
using ControllerNumber = Jacobi.Midi.Messages.ControllerNumber;
using Jacobi.Midi.Messages.V2;

namespace Jacobi.Midi.Device.Commands;

/// <summary>
/// The channel messages of command fields on the wire, as UMP packets: writes the packets of a field's physical value
/// in the form of the root's protocol (MIDI 1.0: type 2, with the CC sequences of an RPN/NRPN and a 14-bit CC;
/// MIDI 2.0: type 4), and scales values between resolutions (M2-104 appendix D.1).
/// </summary>
internal static class CommandWire
{
    // the controllers of the RPN/NRPN sequences of MIDI 1.0
    internal const byte DataEntryMsb = 6;
    internal const byte DataEntryLsb = 38;
    internal const byte NrpnLsb = 98;
    internal const byte NrpnMsb = 99;
    internal const byte RpnLsb = 100;
    internal const byte RpnMsb = 101;

    private const byte ControlChangeStatus = 0xB;
    private const byte ProgramChangeStatus = 0xC;

    /// <summary>The largest physical value of a resolution.</summary>
    public static uint MaxValue(int bits) => bits >= 32 ? UInt32.MaxValue : (1u << bits) - 1;

    /// <summary>The packets that set the field to the physical value, in the form of the protocol.</summary>
    public static IReadOnlyList<UniversalPacket> Write(CommandField field, uint value, int protocol, byte group, byte channel)
        => protocol == 1 ? WriteMidi1(field, value, group, channel) : [WriteMidi2(field, value, group, channel)];

    private static List<UniversalPacket> WriteMidi1(CommandField field, uint value, byte group, byte channel)
    {
        var command = field.Command;
        var packets = new List<UniversalPacket>();
        switch (command.Kind)
        {
            case CommandKind.ControlChange when field.Bits == 14:
                packets.Add(Midi1(group, ControlChangeStatus, channel, (byte)command.Number, (byte)(value >> 7)));
                packets.Add(Midi1(group, ControlChangeStatus, channel, (byte)(command.Number + 32), (byte)(value & 0x7F)));
                break;
            case CommandKind.ControlChange:
                packets.Add(Midi1(group, ControlChangeStatus, channel, (byte)command.Number, (byte)value));
                break;
            case CommandKind.RegisteredParameter or CommandKind.NonRegisteredParameter:
                var registered = command.Kind == CommandKind.RegisteredParameter;
                packets.Add(Midi1(group, ControlChangeStatus, channel, registered ? RpnMsb : NrpnMsb, command.Msb));
                packets.Add(Midi1(group, ControlChangeStatus, channel, registered ? RpnLsb : NrpnLsb, command.Lsb));
                if (field.Bits == 14)
                {
                    packets.Add(Midi1(group, ControlChangeStatus, channel, DataEntryMsb, (byte)(value >> 7)));
                    packets.Add(Midi1(group, ControlChangeStatus, channel, DataEntryLsb, (byte)(value & 0x7F)));
                }
                else
                {
                    packets.Add(Midi1(group, ControlChangeStatus, channel, DataEntryMsb, (byte)value));
                }
                break;
            case CommandKind.ProgramChange:
                packets.Add(Midi1(group, ProgramChangeStatus, channel, (byte)value, 0));
                break;
        }
        return packets;
    }

    private static UniversalPacket WriteMidi2(CommandField field, uint value, byte group, byte channel)
    {
        var command = field.Command;
        var data = command.Kind switch
        {
            CommandKind.ControlChange => new ControlChangeMessage(group, channel, (ControllerNumber)command.Number, value).Data,
            CommandKind.RegisteredParameter => new RegisteredControllerMessage(group, channel, command.Msb, command.Lsb, value).Data,
            CommandKind.NonRegisteredParameter => new AssignableControllerMessage(group, channel, command.Msb, command.Lsb, value).Data,
            _ => new ProgramChangeMessage(group, channel, (byte)value).Data,
        };
        return new UniversalPacket([(uint)data, (uint)(data >> 32)]);
    }

    // a MIDI 1.0 channel voice message in a UMP of type 2
    private static UniversalPacket Midi1(byte group, byte status, byte channel, byte data1, byte data2)
        => new([((uint)MessageKind.Midi1ChannelVoice32 << 28) | ((uint)group << 24) | ((uint)status << 20)
            | ((uint)channel << 16) | ((uint)(data1 & 0x7F) << 8) | (uint)(data2 & 0x7F)]);

    /// <summary>
    /// A value of one resolution in another: up with the Min-Center-Max method, down by cutting off the lower bits
    /// (M2-104 appendix D.1.3 and D.1.4).
    /// </summary>
    public static uint Scale(uint value, int fromBits, int toBits)
    {
        if (fromBits == toBits)
        {
            return value;
        }
        if (fromBits > toBits)
        {
            return value >> (fromBits - toBits);
        }

        var scaleBits = toBits - fromBits;
        var shifted = value << scaleBits;
        var center = 1u << (fromBits - 1);
        if (value <= center)
        {
            return shifted;
        }

        // repeat all but the highest bit in the lower bits
        var repeatBits = fromBits - 1;
        var repeatValue = value & ((1u << repeatBits) - 1);
        repeatValue = scaleBits > repeatBits ? repeatValue << (scaleBits - repeatBits) : repeatValue >> (repeatBits - scaleBits);
        while (repeatValue != 0)
        {
            shifted |= repeatValue;
            repeatValue >>= repeatBits;
        }
        return shifted;
    }
}
