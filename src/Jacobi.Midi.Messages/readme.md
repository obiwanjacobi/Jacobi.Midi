# MIDI Messages

## MIDI v1

```text
MidiEvent (Timestamp)
|-- MidiMessage (short messages)
|   |-- Channel voice (status 0x8n-0xEn)
|   |   |-- NoteOffMessage
|   |   |-- NoteOnMessage
|   |   |-- PolyphonicKeyPressureMessage
|   |   |-- ControlChangeMessage
|   |   |   |-- ControllerMessage (CC 0-119)
|   |   |   |   |-- BankSelectMessage, ModulationWheelMessage, BreathControllerMessage
|   |   |   |   |-- FootControllerMessage, PortamentoTimeMessage, DataEntryMessage
|   |   |   |   |-- ChannelVolumeMessage, BalanceMessage, PanMessage, ExpressionMessage
|   |   |   |   |-- EffectControlMessage, GeneralPurposeControllerMessage
|   |   |   |   |-- ControlChangeLsbMessage, DamperPedalMessage, PortamentoSwitchMessage
|   |   |   |   |-- SostenutoMessage, SoftPedalMessage, LegatoFootswitchMessage, Hold2Message
|   |   |   |   |-- SoundControllerMessage, PortamentoControlMessage
|   |   |   |   |-- HighResolutionVelocityPrefixMessage, EffectsDepthMessage
|   |   |   |   `-- DataIncrementMessage, DataDecrementMessage, NrpnMessage, RpnMessage
|   |   |   |-- ChannelModeMessage (CC 120-127)
|   |   |       |-- AllSoundOffMessage, ResetAllControllersMessage, LocalControlMessage
|   |   |       |-- AllNotesOffMessage, OmniModeOffMessage, OmniModeOnMessage
|   |   |       |-- MonoModeOnMessage, PolyModeOnMessage
|   |   |-- ProgramChangeMessage
|   |   |-- ChannelPressureMessage
|   |   `-- PitchBendChangeMessage
|   |-- System common
|   |   |-- MtcQuarterFrameMessage, SongPositionPointerMessage, SongSelectMessage
|   |   `-- TuneRequestMessage
|   `-- System real-time
|       |-- TimingClockMessage, StartMessage, ContinueMessage, StopMessage
|       `-- ActiveSensingMessage, SystemResetMessage
|-- SystemExclusiveMessage (0xF0; raw/manufacturer-specific data)
|   `-- UniversalSysExMessage (0x7E non-real-time / 0x7F real-time)
|       |-- Non-real-time: SampleDumpHeaderMessage, SampleDataPacketMessage
|       |    SampleDumpRequestMessage, TimeCodeCueingMessage
|       |    IdentityRequestMessage, IdentityReplyMessage
|       |    GeneralMidi1SystemOnMessage, GeneralMidiSystemOffMessage
|       |    GeneralMidi2SystemOnMessage, DownloadableSoundsMessage
|       |    EndOfFileMessage, WaitMessage, CancelMessage, NakMessage, AckMessage
|       `-- Real-time: FullTimeCodeMessage, TimeCodeUserBitsMessage, TimeCodeCueingMessage
|            BarNumberMessage, TimeSignatureNotationMessage
|            MasterVolumeMessage, MasterBalanceMessage
|            MasterFineTuningMessage, MasterCoarseTuningMessage
`-- MidiMetaMessage (Standard MIDI File events)
    |-- SequenceNumberMessage
    |-- TextMessage, CopyrightMessage, TrackNameMessage, InstrumentNameMessage
    |-- LyricMessage, MarkerMessage, CuePointMessage, ProgramNameMessage, DeviceNameMessage
    |-- MidiChannelPrefixMessage, MidiPortMessage, EndOfTrackMessage
    |-- SetTempoMessage, SmpteOffsetMessage, TimeSignatureMessage, KeySignatureMessage
    `-- SequencerSpecificMessage
```

Typed views are tagged-union accessors, not an inheritance hierarchy. Universal SysEx messages without a typed view remain available through `UniversalSysExMessage`'s raw fields.

## MIDI v2

The `Jacobi.Midi.Messages.V2` namespace models all 15 defined **MIDI 2.0
Channel Voice** messages (UMP Message Type `0x4`) in M2-104-UM v1.1.2,
section 7.4 and the message table on page 119. Status `0x7` is reserved.
The namespace keeps the existing MIDI 1.0 types and their packed-byte format
unchanged.

Like the v1 types, these are readonly record structs with parameter constructors,
validating packed-data constructors, and nullable typed `TryGetValue` union
accessors. Constructors from fields validate their ranges and clear reserved bits.
Raw-data constructors validate the message type and status, but preserve reserved
bits and unknown attribute types for forward-compatible inspection.

Every message has a 4-bit Group and a 4-bit Channel (both zero-based, 0-15).
Every packet contains two 32-bit words. After the common header, the fields are:

| Status | Typed View | Word 0, Low 16 Bits | Word 1 |
| --- | --- | --- | --- |
| `0x0` | `RegisteredPerNoteControllerMessage` | Note (7), controller (8) | Value (32) |
| `0x1` | `AssignablePerNoteControllerMessage` | Note (7), controller (8) | Value (32) |
| `0x2` | `RegisteredControllerMessage` | Bank (7), index (7) | Value (32) |
| `0x3` | `AssignableControllerMessage` | Bank (7), index (7) | Value (32) |
| `0x4` | `RelativeRegisteredControllerMessage` | Bank (7), index (7) | Signed adjustment (32) |
| `0x5` | `RelativeAssignableControllerMessage` | Bank (7), index (7) | Signed adjustment (32) |
| `0x6` | `PerNotePitchBendMessage` | Note (7), reserved | Bend (32) |
| `0x8` | `NoteOffMessage` | Note (7), attribute type (8) | Velocity (16), attribute data (16) |
| `0x9` | `NoteOnMessage` | Note (7), attribute type (8) | Velocity (16), attribute data (16) |
| `0xA` | `PolyphonicKeyPressureMessage` | Note (7), reserved | Pressure (32) |
| `0xB` | `ControlChangeMessage` | Controller (7), reserved | Value (32) |
| `0xC` | `ProgramChangeMessage` | Reserved, bank-valid flag | Program (7), reserved, bank MSB (7), bank LSB (7) |
| `0xD` | `ChannelPressureMessage` | Reserved | Pressure (32) |
| `0xE` | `PitchBendChangeMessage` | Reserved | Bend (32) |
| `0xF` | `PerNoteManagementMessage` | Note (7), flags | Reserved |

Unused bits in 7-bit fields are reserved. All payload values are unsigned except
the relative-controller adjustments (`int`, two's complement). Pitch bend has its
neutral position at `0x80000000`. A zero-velocity Note On remains a Note On in
MIDI 2.0, unlike the MIDI 1.0 convention.

`NoteAttributeType` defines None, ManufacturerSpecific, ProfileSpecific, and Pitch.
Pitch attribute data is an unsigned Q7.9 value; registered per-note controller 3
instead carries Q7.25 pitch. With no attribute selected, attribute data must be
zero. These fixed-point values remain raw integers; no floating-point conversion
or MIDI 1.0 value scaling is performed.

`ProgramChangeMessage.Bank` is a nullable 14-bit bank number. `null` leaves the
current bank unchanged; bank zero explicitly selects bank zero. The wire format
separates this value into 7-bit MSB and LSB fields. Per-note management flags are
Reset (`0x01`) and Detach (`0x02`), and may be combined.

The existing `ControllerNumber` enum is shared with v1. `ControlChangeMessage`
also exposes `IsChannelMode` for controllers 120-127. Registered-controller
specializations (for example pitch-bend sensitivity) and named channel-mode
specializations are represented by their controller fields, not additional typed
views. Controller-specific semantics, including which controller numbers are
allowed by MIDI 2.0, are the caller's responsibility.

```csharp
using V2 = Jacobi.Midi.Messages.V2;

var note = new V2.NoteOnMessage(group: 0, channel: 0, note: 60, velocity: 0xC000);
var message = new V2.MidiMessage(note.Data);
var packet = message.Packet; // UniversalPacket64: ready for UMP transport

// Receive a packet, or construct directly from two UMP words.
var received = new V2.MidiMessage(packet);
if (received.TryGetValue(out V2.NoteOnMessage? noteOn))
{
    Console.WriteLine($"{noteOn.Value.Note}: {noteOn.Value.Velocity}");
}
```

`V2.MidiMessage` implements `IUniversalPacket`. Its indexer and `CopyTo` expose
words in transmission order. Its packed `ulong Data` places **word 0 in the low
32 bits**, matching `UniversalPacket64`; it is not a big-endian 64-bit wire
integer or the v1 packed-byte representation. Construct from `(word0, word1)`
when receiving individual UMP words.

This is complete coverage of the MIDI 2.0 Channel Voice status set, not of every
UMP family in the PDF. SysEx7 and SysEx8 are modeled below. Utility, System,
MIDI 1.0-over-UMP, Mixed Data Set, Flex Data, and UMP Stream packets are not yet
modeled here. MIDI file readers/writers and MIDI 1.0 byte stream readers/writers
are unchanged; these v2 messages use UMP transport.

### System Exclusive UMPs

`V2.SysEx7Message` and `V2.SysEx8Message` model the packet formats from
M2-104-UM v1.1.2, sections 7.7 and 7.8. Each value represents **one UMP**, which
may contain a complete message or a fragment. Both implement `IUniversalPacket`
and expose Group, Status, PayloadLength, packed Data, and a typed Packet property.

| Type | Message Type | Packet Size | Maximum Payload | Wire Byte Count |
| --- | --- | --- | --- | --- |
| `SysEx7Message` | `0x3` | 64 bits | 6 seven-bit bytes | Payload length (0-6) |
| `SysEx8Message` | `0x5` | 128 bits | 13 eight-bit bytes | Payload length + Stream ID (1-14) |

`SysExPacketStatus` selects Complete (`0`), Start (`1`), Continue (`2`), or End
(`3`). Short Start/Continue packets do not imply a message end. Payloads omit the
MIDI 1.0 `F0` and `F7` framing bytes. In SysEx8, `F0` and `F7` are ordinary valid
payload values, not delimiters.

The word indexer and `CopyTo(Span<uint>)` expose the complete UMP in transmission
order. `GetPayloadByte` and `CopyPayloadTo(Span<byte>)` expose only valid payload
bytes, excluding headers, Stream ID, and padding. Field constructors clear
padding and validate field ranges. Packed-data and word constructors validate
the type, status, count, and active SysEx7 data bytes, while preserving padding
for inspection. The default struct values have `HasValue == false`.

```csharp
using V2 = Jacobi.Midi.Messages.V2;

// Universal non-real-time Identity Request in one SysEx7 UMP.
var request = new V2.SysEx7Message(
    group: 0, status: V2.SysExPacketStatus.Complete,
    payload: [0x7E, 0x7F, 0x06, 0x01]);
var packet = request.Packet; // UniversalPacket64

if (request.TryGetSystemExclusive(out var sysEx)
    && sysEx.Value.TryGetUniversal(out var universal)
    && universal.Value.TryGetValue(out Jacobi.Midi.Messages.IdentityRequestMessage? identity))
{
    Console.WriteLine(identity.Value);
}

// Raw 8-bit payload fragment; manufacturer/protocol headers belong in the Start packet.
var fragment = new V2.SysEx8Message(
    group: 0, status: V2.SysExPacketStatus.Continue,
    streamId: 1, payload: [0x80, 0xFF, 0xF0, 0xF7]);
var packet128 = fragment.Packet; // UniversalPacket128
var abort = V2.SysEx8Message.CreateAbort(group: 0, streamId: 1);
```

`TryGetSystemExclusive` succeeds only for nonempty Complete SysEx7 packets. It
allocates a framed byte array so the existing manufacturer and universal SysEx
views can be reused. Start/Continue/End packets must first be reassembled; a
manufacturer ID or universal header can span more than one packet.
`V2.SysEx7Assembler` does that for the packets of one Group: the reverse of
`SysEx7Message.FromSystemExclusive`, it returns the framed (`F0` … `F7`) message
when an End (or Complete) packet arrives. A new Start discards an unfinished
message; Continue/End packets without a Start and other packets are ignored.

SysEx8 exposes the Stream ID independently from its payload. `CreateAbort`
produces an End packet with wire ByteCount `0xF`, zero data bytes, and
`IsAborted == true`. This special count is not a length: an abort has
PayloadLength zero. An ordinary End with an empty payload has ByteCount `1`
and retains the preceding data instead of aborting it.

SysEx8 manufacturer IDs use the specification's 16-bit encoding rather than the
MIDI 1.0 one-/three-byte encoding. SysEx8 payloads therefore cannot be fed directly
to the existing MIDI 1.0 SysEx views or translated by adding `F0`/`F7` framing.
These packet models expose the payload without interpreting manufacturer-specific
content. Stream negotiation, message fragmentation/reassembly, interleaving
policy, and conversion of manufacturer identifiers are not performed by these
value types. SysEx7 sequencing is per Group; SysEx8 sequencing is per Group and
Stream ID, and sending multiple streams requires receiver support.
