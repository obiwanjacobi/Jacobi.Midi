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
