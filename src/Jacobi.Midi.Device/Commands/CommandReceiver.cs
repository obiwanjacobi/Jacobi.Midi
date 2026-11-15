using Jacobi.Midi.Core;
using Jacobi.Midi.Messages.V2;

namespace Jacobi.Midi.Device.Commands;

/// <summary>
/// Recognises the received channel messages of one group and channel as fields of a command map, with their physical
/// value in the resolution of the field. Both UMP forms are accepted, whatever the protocol of the root: MIDI 1.0
/// (type 2), whose RPN/NRPN sequences (CC 101/100 or 99/98, then 6/38) are assembled here, and MIDI 2.0 (type 4); a
/// 14-bit control change arrives as two messages in both forms (MSB, then LSB). A value of another resolution is
/// scaled. Messages of other groups or channels, of other kinds, or for fields outside <paramref name="fields"/> are
/// ignored. Keeps the state of the sequences: call it from one thread at a time (the receive calls of a connection
/// are serialized).
/// </summary>
internal sealed class CommandReceiver(CommandMap map, IReadOnlyCollection<CommandField> fields, byte group, byte channel)
{
    private const byte NullParameter = 0x7F;

    // the RPN/NRPN selected by the last CC 101/100 or 99/98, and the data entry MSB that was received for it
    private CommandKind? _parameterKind;
    private byte? _parameterMsb;
    private byte? _parameterLsb;
    private byte? _dataMsb;
    // the MSB of each 14-bit control change, by controller number
    private readonly Dictionary<int, uint> _controlMsb = [];

    /// <summary>The fields the packet sets, with their physical values; empty when it sets none.</summary>
    public IReadOnlyList<(CommandField Field, uint Value)> Receive(UniversalPacket packet)
    {
        if (!packet.HasValue)
        {
            return [];
        }
        var received = packet.MessageKind switch
        {
            MessageKind.Midi1ChannelVoice32 when packet.PacketKind == PacketKind.Packet32 => ReceiveMidi1(packet[0]),
            MessageKind.Midi2ChannelVoice64 when packet.PacketKind == PacketKind.Packet64 => ReceiveMidi2(new MidiMessage(packet[0], packet[1])),
            _ => null,
        };
        return received is { } value && Assign(value) is { } assigned ? [assigned] : [];
    }

    // what a channel message sets: the command, and the value in its resolution
    private readonly record struct Received(Command Command, uint Value, int Bits);

    private Received? ReceiveMidi1(uint word)
    {
        if (((word >> 24) & 0xF) != group || ((word >> 16) & 0xF) != channel)
        {
            return null;
        }
        var status = (word >> 20) & 0xF;
        var data1 = (byte)((word >> 8) & 0x7F);
        var data2 = (byte)(word & 0x7F);
        return status switch
        {
            0xB => ReceiveControlChange(data1, data2),
            0xC => new Received(new Command(CommandKind.ProgramChange, 0), data1, 7),
            _ => null,
        };
    }

    private Received? ReceiveControlChange(byte controller, byte value)
    {
        switch (controller)
        {
            case CommandWire.RpnMsb or CommandWire.RpnLsb or CommandWire.NrpnMsb or CommandWire.NrpnLsb:
                Select(controller is CommandWire.RpnMsb or CommandWire.RpnLsb ? CommandKind.RegisteredParameter : CommandKind.NonRegisteredParameter,
                    controller is CommandWire.RpnMsb or CommandWire.NrpnMsb, value);
                return null;
            case CommandWire.DataEntryMsb:
                _dataMsb = value;
                return Parameter() is { } selected ? new Received(selected, (uint)value << 7, 14) : null;
            case CommandWire.DataEntryLsb:
                return _dataMsb is { } msb && Parameter() is { } current ? new Received(current, ((uint)msb << 7) | value, 14) : null;
            default:
                return new Received(new Command(CommandKind.ControlChange, controller), value, 7);
        }
    }

    // a CC 101/100 or 99/98: selects (a byte of) the parameter; another kind starts a new selection
    private void Select(CommandKind kind, bool isMsb, byte value)
    {
        if (_parameterKind != kind)
        {
            _parameterKind = kind;
            _parameterMsb = null;
            _parameterLsb = null;
        }
        if (isMsb)
        {
            _parameterMsb = value;
        }
        else
        {
            _parameterLsb = value;
        }
        _dataMsb = null;
    }

    // the selected RPN/NRPN; null when it is not complete or is the null function (127/127)
    private Command? Parameter()
        => _parameterKind is { } kind && _parameterMsb is { } msb && _parameterLsb is { } lsb
            && !(msb == NullParameter && lsb == NullParameter)
            ? new Command(kind, (msb << 7) | lsb)
            : null;

    private Received? ReceiveMidi2(MidiMessage message)
    {
        if (message.Group != group || message.Channel != channel)
        {
            return null;
        }
        return message.Kind switch
        {
            MidiMessageKind.ControlChange when message.TryGetValue(out ControlChangeMessage? cc)
                => new Received(new Command(CommandKind.ControlChange, cc.Value.Controller), cc.Value.Value, 32),
            MidiMessageKind.RegisteredController when message.TryGetValue(out RegisteredControllerMessage? rpn)
                => new Received(new Command(CommandKind.RegisteredParameter, (rpn.Value.Bank << 7) | rpn.Value.Index), rpn.Value.Value, 32),
            MidiMessageKind.AssignableController when message.TryGetValue(out AssignableControllerMessage? nrpn)
                => new Received(new Command(CommandKind.NonRegisteredParameter, (nrpn.Value.Bank << 7) | nrpn.Value.Index), nrpn.Value.Value, 32),
            MidiMessageKind.ProgramChange when message.TryGetValue(out ProgramChangeMessage? pc)
                => new Received(new Command(CommandKind.ProgramChange, 0), pc.Value.Program, 7),
            _ => null,
        };
    }

    // the field the received command sets, with the value in the field's resolution
    private (CommandField, uint)? Assign(Received received)
    {
        var command = received.Command;
        if (map.Find(command) is { } field)
        {
            if (!fields.Contains(field))
            {
                return null;
            }
            if (field is { Command.Kind: CommandKind.ControlChange, Bits: 14 })
            {
                // the MSB of a 14-bit control change: the LSB starts again at 0 (MIDI 1.0)
                var msb = CommandWire.Scale(received.Value, received.Bits, 7);
                _controlMsb[command.Number] = msb;
                return (field, msb << 7);
            }
            return (field, CommandWire.Scale(received.Value, received.Bits, field.Bits));
        }

        // the LSB of a 14-bit control change (controller 32-63)
        if (command is { Kind: CommandKind.ControlChange, Number: >= 32 and < 64 }
            && map.Find(command with { Number = command.Number - 32 }) is { Bits: 14 } pair
            && fields.Contains(pair)
            && _controlMsb.TryGetValue(pair.Command.Number, out var known))
        {
            return (pair, (known << 7) | CommandWire.Scale(received.Value, received.Bits, 7));
        }
        return null;
    }
}
