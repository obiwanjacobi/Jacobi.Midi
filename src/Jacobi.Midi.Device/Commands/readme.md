# Commands

Device parameters that are set with ordinary channel messages (Control Change, RPN, NRPN, Program Change), described
in the schemas like the SysEx data of the message maps, and used with the same value model. The design decisions are
in [docs/decisions.md](../../../docs/decisions.md), *Parameter sources other than message maps*; the plan in
[parameter-sources-plan.md](../parameter-sources-plan.md).

## Command roots

A command root is a root group with `midi.role="cmd1"` (a MIDI 1.0 device) or `midi.role="cmd2"` (a MIDI 2.0 device).
Its fields are the parameters; groups structure them. Each field has a `midi.cmd` tag with the channel message it is
transferred with:

| `midi.cmd` | Message | Number |
|------------|---------|--------|
| `cc#<number>` | Control Change | the controller number, 0-127 |
| `rpn#<number>` or `rpn#<msb>/<lsb>` | Registered Parameter Number | the parameter number, 0-16383 |
| `nrpn#<number>` or `nrpn#<msb>/<lsb>` | Non-Registered Parameter Number | the parameter number, 0-16383 |
| `pc` | Program Change | none: the value is the program |

The data type of a field gives the logical value (facets, value processors) and, by its size, the resolution of the
value in the message:

| Root | 1 byte | 2 bytes | 4 bytes |
|------|--------|---------|---------|
| `cmd1` | 7 bits | 14 bits (a CC as MSB + LSB on number + 32; an RPN/NRPN with data entry MSB + LSB) | - |
| `cmd2` | 7 bits (`pc` only) | - | 32 bits |

The MIDI channel and the UMP group are device values (`channel`, `group`, 0-15): the root can fix them with
`midi.device.channel` and `midi.device.group`, the values of the client override them.

The rules of a command root (checked by `CommandMap`, and shown by `MessageMapValidator` as for the message maps):
groups and fields only (no repeats or choices); every field has a valid `midi.cmd` and a matching data type, no fixed
value and no representation processors; no two fields use the same command; no message roles; no controller numbers
of other functions (RPN/NRPN controllers, channel mode messages, and in `cmd2` the bank select).

A command root is not a message map: `MessageMaps` refuses it (`MidiProperties.IsCommandRoot` decides, for both).

## Sending and receiving

`CommandMessages` works on an `IMidiConnection` the client opened and owns, with the client's device values:

- `Send(range, source)`: one message (or sequence) per field of the range, in the form of the root's protocol: a
  `cmd1` root as MIDI 1.0 (UMP type 2), including the CC sequences of an RPN/NRPN and a 14-bit CC; a `cmd2` root as
  MIDI 2.0 (UMP type 4). Every field needs a value; otherwise nothing is sent.
- `Listen(range, sink)`: the values of the fields of the range that the device sends, until cancelled. Both UMP forms
  are accepted for both protocols (another resolution is scaled, M2-104 appendix D.1); the MIDI 1.0 sequences are
  assembled. Messages of other channels, groups or kinds are ignored.
- `Validate(root)`: the problems of a command root.

The values cannot be read back: there is no request.

The value of a field is converted between its logical and physical value by the serializer, writing or reading a
range of only that field. A conversion without bytes is on the Jacobi.BinarySerializer todo list.
