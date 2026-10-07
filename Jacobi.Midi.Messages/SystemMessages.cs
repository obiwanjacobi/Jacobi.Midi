namespace Jacobi.Midi.Messages;

/// <summary>MIDI Time Code Quarter Frame (0xF1): one quarter of a MIDI Time Code frame.</summary>
public readonly record struct MtcQuarterFrameMessage
{
    private readonly uint _data;

    /// <summary>Creates a MtcQuarterFrameMessage from its parameters.</summary>
    public MtcQuarterFrameMessage(byte messageType, byte value)
        : this(PackedMessage.PackSystem(MidiMessageKind.MtcQuarterFrame, PackedMessage.MtcData(messageType, value)))
    { }

    public MtcQuarterFrameMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.MtcQuarterFrame, nameof(MtcQuarterFrameMessage));

    /// <summary>Which of the eight time code pieces this is (0-7).</summary>
    public byte MessageType => (byte)((_data.Byte(1) >> 4) & 0x07);

    /// <summary>The 4 bits of time code data.</summary>
    public byte Value => (byte)(_data.Byte(1) & 0x0F);
}

/// <summary>Song Position Pointer (0xF2): the position in the song in MIDI beats (sixteenth notes).</summary>
public readonly record struct SongPositionPointerMessage
{
    private readonly uint _data;

    /// <summary>Creates a SongPositionPointerMessage from its parameters.</summary>
    public SongPositionPointerMessage(int position)
        : this(PackedMessage.PackSystem(MidiMessageKind.SongPositionPointer, (byte)(PackedMessage.CheckRange(position, 0, 0x3FFF) & 0x7F), (byte)(position >> 7)))
    { }

    public SongPositionPointerMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.SongPositionPointer, nameof(SongPositionPointerMessage));

    // 14-bit value: LSB in byte 1, MSB in byte 2
    public int Position => (_data.Byte(2) << 7) | _data.Byte(1);
}

/// <summary>Song Select (0xF3): selects the song or sequence to play.</summary>
public readonly record struct SongSelectMessage
{
    private readonly uint _data;

    /// <summary>Creates a SongSelectMessage from its parameters.</summary>
    public SongSelectMessage(byte song)
        : this(PackedMessage.PackSystem(MidiMessageKind.SongSelect, song))
    { }

    public SongSelectMessage(uint data)
        => _data = MidiMessage.Validate(data, MidiMessageKind.SongSelect, nameof(SongSelectMessage));

    public byte Song => _data.Byte(1);
}

/// <summary>Tune Request (0xF6): asks analog synthesizers to tune their oscillators.</summary>
public readonly record struct TuneRequestMessage
{

    public TuneRequestMessage(uint data)
        => MidiMessage.Validate(data, MidiMessageKind.TuneRequest, nameof(TuneRequestMessage));
}

/// <summary>Timing Clock (0xF8): sent 24 times per quarter note to synchronize.</summary>
public readonly record struct TimingClockMessage
{

    public TimingClockMessage(uint data)
        => MidiMessage.Validate(data, MidiMessageKind.TimingClock, nameof(TimingClockMessage));
}

/// <summary>Start (0xFA): starts the current sequence from the beginning.</summary>
public readonly record struct StartMessage
{

    public StartMessage(uint data)
        => MidiMessage.Validate(data, MidiMessageKind.Start, nameof(StartMessage));
}

/// <summary>Continue (0xFB): continues the sequence from where it was stopped.</summary>
public readonly record struct ContinueMessage
{

    public ContinueMessage(uint data)
        => MidiMessage.Validate(data, MidiMessageKind.Continue, nameof(ContinueMessage));
}

/// <summary>Stop (0xFC): stops the current sequence.</summary>
public readonly record struct StopMessage
{

    public StopMessage(uint data)
        => MidiMessage.Validate(data, MidiMessageKind.Stop, nameof(StopMessage));
}

/// <summary>Active Sensing (0xFE): a keep-alive that tells the receiver the connection is still there.</summary>
public readonly record struct ActiveSensingMessage
{

    public ActiveSensingMessage(uint data)
        => MidiMessage.Validate(data, MidiMessageKind.ActiveSensing, nameof(ActiveSensingMessage));
}

/// <summary>System Reset (0xFF): resets all receivers to their power-up state. Not used in MIDI files (see meta events).</summary>
public readonly record struct SystemResetMessage
{

    public SystemResetMessage(uint data)
        => MidiMessage.Validate(data, MidiMessageKind.SystemReset, nameof(SystemResetMessage));
}
