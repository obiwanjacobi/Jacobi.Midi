using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Jacobi.Midi.Messages;

/// <summary>
/// The MIDI 1.0 control change controller numbers (second data byte of a control change message).
/// Undefined controller numbers are not listed; they can still be cast from a byte.
/// </summary>
public enum ControllerNumber : byte
{
    BankSelect = 0,
    ModulationWheel = 1,
    BreathController = 2,
    FootController = 4,
    PortamentoTime = 5,
    DataEntry = 6,
    ChannelVolume = 7,
    Balance = 8,
    Pan = 10,
    Expression = 11,
    EffectControl1 = 12,
    EffectControl2 = 13,
    GeneralPurposeController1 = 16,
    GeneralPurposeController2 = 17,
    GeneralPurposeController3 = 18,
    GeneralPurposeController4 = 19,

    BankSelectLsb = 32,
    ModulationWheelLsb = 33,
    BreathControllerLsb = 34,
    FootControllerLsb = 36,
    PortamentoTimeLsb = 37,
    DataEntryLsb = 38,
    ChannelVolumeLsb = 39,
    BalanceLsb = 40,
    PanLsb = 42,
    ExpressionLsb = 43,
    EffectControl1Lsb = 44,
    EffectControl2Lsb = 45,
    GeneralPurposeController1Lsb = 48,
    GeneralPurposeController2Lsb = 49,
    GeneralPurposeController3Lsb = 50,
    GeneralPurposeController4Lsb = 51,

    DamperPedal = 64,
    PortamentoSwitch = 65,
    Sostenuto = 66,
    SoftPedal = 67,
    LegatoFootswitch = 68,
    Hold2 = 69,
    SoundController1 = 70,
    SoundController2 = 71,
    SoundController3 = 72,
    SoundController4 = 73,
    SoundController5 = 74,
    SoundController6 = 75,
    SoundController7 = 76,
    SoundController8 = 77,
    SoundController9 = 78,
    SoundController10 = 79,
    GeneralPurposeController5 = 80,
    GeneralPurposeController6 = 81,
    GeneralPurposeController7 = 82,
    GeneralPurposeController8 = 83,
    PortamentoControl = 84,
    HighResolutionVelocityPrefix = 88,
    Effects1Depth = 91,
    Effects2Depth = 92,
    Effects3Depth = 93,
    Effects4Depth = 94,
    Effects5Depth = 95,
    DataIncrement = 96,
    DataDecrement = 97,
    NrpnLsb = 98,
    NrpnMsb = 99,
    RpnLsb = 100,
    RpnMsb = 101,

    // channel mode messages
    AllSoundOff = 120,
    ResetAllControllers = 121,
    LocalControl = 122,
    AllNotesOff = 123,
    OmniModeOff = 124,
    OmniModeOn = 125,
    MonoModeOn = 126,
    PolyModeOn = 127,
}

/// <summary>
/// A union over the specific control change messages for controller numbers 0-119 (channel mode messages are
/// in <see cref="ChannelModeMessage"/>), backed by the raw message bytes (no copy).
/// </summary>
[Union]
public readonly record struct ControllerMessage : IUnion
{
    internal const byte LastController = 119;

    private readonly uint _data;

    public ControllerMessage(uint data)
    {
        MidiMessage.Validate(data, MidiMessageKind.ControlChange, nameof(ControllerMessage));
        if (data.Byte(1) > LastController)
            throw new ArgumentException($"{nameof(ControllerMessage)} requires a controller number of {LastController} or lower, but got {data.Byte(1)}.", nameof(data));
        _data = data;
    }

    public ControllerMessage(ControlChangeMessage message)
        : this(message.Data)
    { }

    // validates status, length and that the controller number is in the (inclusive) range
    internal static uint Validate(uint data, byte first, byte last, string messageName)
    {
        MidiMessage.Validate(data, MidiMessageKind.ControlChange, messageName);
        var controller = data.Byte(1);
        if (controller < first || controller > last)
        {
            var expected = first == last
                ? $"controller number {first}"
                : $"a controller number from {first} to {last}";
            throw new ArgumentException($"{messageName} requires {expected}, but got {controller}.", nameof(data));
        }
        return data;
    }

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>Identifies which controller this message is for.</summary>
    public ControllerNumber ControllerNumber => (ControllerNumber)_data.Byte(1);

    // IUnion implementation
    public object? Value => HasValue ? _data : null;

    public bool HasValue => (_data.Byte(0) & 0xF0) == (byte)MidiMessageKind.ControlChange
        && _data.Byte(1) <= LastController;

    private bool Is(byte first, byte last)
        => HasValue && _data.Byte(1) >= first && _data.Byte(1) <= last;

    // non-boxing union accessors (typed views over the same memory, no copy)
    public bool TryGetValue([NotNullWhen(true)] out BankSelectMessage? value)
    {
        if (Is(BankSelectMessage.Controller, BankSelectMessage.Controller)) { value = new BankSelectMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ModulationWheelMessage? value)
    {
        if (Is(ModulationWheelMessage.Controller, ModulationWheelMessage.Controller)) { value = new ModulationWheelMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out BreathControllerMessage? value)
    {
        if (Is(BreathControllerMessage.Controller, BreathControllerMessage.Controller)) { value = new BreathControllerMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out FootControllerMessage? value)
    {
        if (Is(FootControllerMessage.Controller, FootControllerMessage.Controller)) { value = new FootControllerMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PortamentoTimeMessage? value)
    {
        if (Is(PortamentoTimeMessage.Controller, PortamentoTimeMessage.Controller)) { value = new PortamentoTimeMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out DataEntryMessage? value)
    {
        if (Is(DataEntryMessage.Controller, DataEntryMessage.Controller)) { value = new DataEntryMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ChannelVolumeMessage? value)
    {
        if (Is(ChannelVolumeMessage.Controller, ChannelVolumeMessage.Controller)) { value = new ChannelVolumeMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out BalanceMessage? value)
    {
        if (Is(BalanceMessage.Controller, BalanceMessage.Controller)) { value = new BalanceMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PanMessage? value)
    {
        if (Is(PanMessage.Controller, PanMessage.Controller)) { value = new PanMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ExpressionMessage? value)
    {
        if (Is(ExpressionMessage.Controller, ExpressionMessage.Controller)) { value = new ExpressionMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out EffectControlMessage? value)
    {
        if (Is(EffectControlMessage.FirstController, EffectControlMessage.LastController)) { value = new EffectControlMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out GeneralPurposeControllerMessage? value)
    {
        if (HasValue && GeneralPurposeControllerMessage.Handles(_data.Byte(1))) { value = new GeneralPurposeControllerMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ControlChangeLsbMessage? value)
    {
        if (Is(ControlChangeLsbMessage.FirstController, ControlChangeLsbMessage.LastController)) { value = new ControlChangeLsbMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out DamperPedalMessage? value)
    {
        if (Is(DamperPedalMessage.Controller, DamperPedalMessage.Controller)) { value = new DamperPedalMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PortamentoSwitchMessage? value)
    {
        if (Is(PortamentoSwitchMessage.Controller, PortamentoSwitchMessage.Controller)) { value = new PortamentoSwitchMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SostenutoMessage? value)
    {
        if (Is(SostenutoMessage.Controller, SostenutoMessage.Controller)) { value = new SostenutoMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SoftPedalMessage? value)
    {
        if (Is(SoftPedalMessage.Controller, SoftPedalMessage.Controller)) { value = new SoftPedalMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out LegatoFootswitchMessage? value)
    {
        if (Is(LegatoFootswitchMessage.Controller, LegatoFootswitchMessage.Controller)) { value = new LegatoFootswitchMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out Hold2Message? value)
    {
        if (Is(Hold2Message.Controller, Hold2Message.Controller)) { value = new Hold2Message(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SoundControllerMessage? value)
    {
        if (Is(SoundControllerMessage.FirstController, SoundControllerMessage.LastController)) { value = new SoundControllerMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PortamentoControlMessage? value)
    {
        if (Is(PortamentoControlMessage.Controller, PortamentoControlMessage.Controller)) { value = new PortamentoControlMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out HighResolutionVelocityPrefixMessage? value)
    {
        if (Is(HighResolutionVelocityPrefixMessage.Controller, HighResolutionVelocityPrefixMessage.Controller)) { value = new HighResolutionVelocityPrefixMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out EffectsDepthMessage? value)
    {
        if (Is(EffectsDepthMessage.FirstController, EffectsDepthMessage.LastController)) { value = new EffectsDepthMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out DataIncrementMessage? value)
    {
        if (Is(DataIncrementMessage.Controller, DataIncrementMessage.Controller)) { value = new DataIncrementMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out DataDecrementMessage? value)
    {
        if (Is(DataDecrementMessage.Controller, DataDecrementMessage.Controller)) { value = new DataDecrementMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out NrpnMessage? value)
    {
        if (Is(NrpnMessage.FirstController, NrpnMessage.LastController)) { value = new NrpnMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out RpnMessage? value)
    {
        if (Is(RpnMessage.FirstController, RpnMessage.LastController)) { value = new RpnMessage(_data); return true; }
        value = null;
        return false;
    }
}

/// <summary>Bank Select (CC 0): selects the sound bank (MSB) for subsequent program changes.</summary>
public readonly record struct BankSelectMessage
{
    public const byte Controller = 0;
    private readonly uint _data;

    /// <summary>Creates a BankSelectMessage from its parameters.</summary>
    public BankSelectMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public BankSelectMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(BankSelectMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Modulation Wheel or Lever (CC 1): the amount of modulation (MSB).</summary>
public readonly record struct ModulationWheelMessage
{
    public const byte Controller = 1;
    private readonly uint _data;

    /// <summary>Creates a ModulationWheelMessage from its parameters.</summary>
    public ModulationWheelMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public ModulationWheelMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(ModulationWheelMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Breath Controller (CC 2): the breath controller position (MSB).</summary>
public readonly record struct BreathControllerMessage
{
    public const byte Controller = 2;
    private readonly uint _data;

    /// <summary>Creates a BreathControllerMessage from its parameters.</summary>
    public BreathControllerMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public BreathControllerMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(BreathControllerMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Foot Controller (CC 4): the foot controller position (MSB).</summary>
public readonly record struct FootControllerMessage
{
    public const byte Controller = 4;
    private readonly uint _data;

    /// <summary>Creates a FootControllerMessage from its parameters.</summary>
    public FootControllerMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public FootControllerMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(FootControllerMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Portamento Time (CC 5): the time (rate) of the portamento glide (MSB).</summary>
public readonly record struct PortamentoTimeMessage
{
    public const byte Controller = 5;
    private readonly uint _data;

    /// <summary>Creates a PortamentoTimeMessage from its parameters.</summary>
    public PortamentoTimeMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public PortamentoTimeMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(PortamentoTimeMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Data Entry MSB (CC 6): the value for the parameter selected by RPN or NRPN.</summary>
public readonly record struct DataEntryMessage
{
    public const byte Controller = 6;
    private readonly uint _data;

    /// <summary>Creates a DataEntryMessage from its parameters.</summary>
    public DataEntryMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public DataEntryMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(DataEntryMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Channel Volume (CC 7): the volume of the channel (MSB), formerly Main Volume.</summary>
public readonly record struct ChannelVolumeMessage
{
    public const byte Controller = 7;
    private readonly uint _data;

    /// <summary>Creates a ChannelVolumeMessage from its parameters.</summary>
    public ChannelVolumeMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public ChannelVolumeMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(ChannelVolumeMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Balance (CC 8): the left/right balance (MSB).</summary>
public readonly record struct BalanceMessage
{
    public const byte Controller = 8;
    private readonly uint _data;

    /// <summary>Creates a BalanceMessage from its parameters.</summary>
    public BalanceMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public BalanceMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(BalanceMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Pan (CC 10): the left/right position in the stereo field (MSB).</summary>
public readonly record struct PanMessage
{
    public const byte Controller = 10;
    private readonly uint _data;

    /// <summary>Creates a PanMessage from its parameters.</summary>
    public PanMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public PanMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(PanMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Expression Controller (CC 11): the expression (a percentage of channel volume, MSB).</summary>
public readonly record struct ExpressionMessage
{
    public const byte Controller = 11;
    private readonly uint _data;

    /// <summary>Creates a ExpressionMessage from its parameters.</summary>
    public ExpressionMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public ExpressionMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(ExpressionMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>Effect Control 1 and 2 (CC 12-13): parameters of an effect (MSB).</summary>
public readonly record struct EffectControlMessage
{
    public const byte FirstController = 12;
    public const byte LastController = 13;
    private readonly uint _data;

    /// <summary>Creates a EffectControlMessage from its parameters.</summary>
    public EffectControlMessage(byte channel, int number, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)(FirstController + PackedMessage.CheckRange(number, 1, LastController - FirstController + 1) - 1), value))
    { }

    public EffectControlMessage(uint data)
        => _data = ControllerMessage.Validate(data, FirstController, LastController, nameof(EffectControlMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>The effect control number: 1 or 2.</summary>
    public int Number => _data.Byte(1) - FirstController + 1;

    public byte Value => _data.Byte(2);
}

/// <summary>General Purpose Controller 1-4 (CC 16-19) and 5-8 (CC 80-83): freely assignable controllers.</summary>
public readonly record struct GeneralPurposeControllerMessage
{
    private readonly uint _data;

    /// <summary>Creates a GeneralPurposeControllerMessage from its parameters.</summary>
    public GeneralPurposeControllerMessage(byte channel, int number, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)(PackedMessage.CheckRange(number, 1, 8) <= 4 ? 15 + number : 75 + number), value))
    { }

    public GeneralPurposeControllerMessage(uint data)
    {
        MidiMessage.Validate(data, MidiMessageKind.ControlChange, nameof(GeneralPurposeControllerMessage));
        if (!Handles(data.Byte(1)))
            throw new ArgumentException($"{nameof(GeneralPurposeControllerMessage)} requires controller number 16-19 or 80-83, but got {data.Byte(1)}.", nameof(data));
        _data = data;
    }

    internal static bool Handles(byte controller)
        => controller is >= 16 and <= 19 or >= 80 and <= 83;

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>The general purpose controller number: 1 to 8.</summary>
    public int Number => _data.Byte(1) <= 19 ? _data.Byte(1) - 15 : _data.Byte(1) - 79;

    public byte Value => _data.Byte(2);
}

/// <summary>LSB for Controls 0-31 (CC 32-63): the least significant byte of a 14-bit controller value.</summary>
public readonly record struct ControlChangeLsbMessage
{
    public const byte FirstController = 32;
    public const byte LastController = 63;
    private readonly uint _data;

    /// <summary>Creates a ControlChangeLsbMessage from its parameters.</summary>
    public ControlChangeLsbMessage(byte channel, ControllerNumber msbController, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)(PackedMessage.CheckRange((byte)msbController, 0, 31) + FirstController), value))
    { }

    public ControlChangeLsbMessage(uint data)
        => _data = ControllerMessage.Validate(data, FirstController, LastController, nameof(ControlChangeLsbMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>The controller (CC 0-31) this is the LSB for.</summary>
    public ControllerNumber MsbParameter => (ControllerNumber)(_data.Byte(1) - FirstController);

    public byte Value => _data.Byte(2);
}

/// <summary>Damper Pedal (CC 64): sustain pedal on or off.</summary>
public readonly record struct DamperPedalMessage
{
    public const byte Controller = 64;
    private readonly uint _data;

    /// <summary>Creates a DamperPedalMessage from its parameters.</summary>
    public DamperPedalMessage(byte channel, bool isOn)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, (byte)(isOn ? 127 : 0)))
    { }

    public DamperPedalMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(DamperPedalMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>63 and below is off, 64 and above is on.</summary>
    public bool IsOn => _data.Byte(2) >= 64;
}

/// <summary>Portamento On/Off (CC 65): portamento on or off.</summary>
public readonly record struct PortamentoSwitchMessage
{
    public const byte Controller = 65;
    private readonly uint _data;

    /// <summary>Creates a PortamentoSwitchMessage from its parameters.</summary>
    public PortamentoSwitchMessage(byte channel, bool isOn)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, (byte)(isOn ? 127 : 0)))
    { }

    public PortamentoSwitchMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(PortamentoSwitchMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>63 and below is off, 64 and above is on.</summary>
    public bool IsOn => _data.Byte(2) >= 64;
}

/// <summary>Sostenuto On/Off (CC 66): sostenuto pedal on or off.</summary>
public readonly record struct SostenutoMessage
{
    public const byte Controller = 66;
    private readonly uint _data;

    /// <summary>Creates a SostenutoMessage from its parameters.</summary>
    public SostenutoMessage(byte channel, bool isOn)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, (byte)(isOn ? 127 : 0)))
    { }

    public SostenutoMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(SostenutoMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>63 and below is off, 64 and above is on.</summary>
    public bool IsOn => _data.Byte(2) >= 64;
}

/// <summary>Soft Pedal On/Off (CC 67): soft pedal on or off.</summary>
public readonly record struct SoftPedalMessage
{
    public const byte Controller = 67;
    private readonly uint _data;

    /// <summary>Creates a SoftPedalMessage from its parameters.</summary>
    public SoftPedalMessage(byte channel, bool isOn)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, (byte)(isOn ? 127 : 0)))
    { }

    public SoftPedalMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(SoftPedalMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>63 and below is off, 64 and above is on.</summary>
    public bool IsOn => _data.Byte(2) >= 64;
}

/// <summary>Legato Footswitch (CC 68): normal or legato playing.</summary>
public readonly record struct LegatoFootswitchMessage
{
    public const byte Controller = 68;
    private readonly uint _data;

    /// <summary>Creates a LegatoFootswitchMessage from its parameters.</summary>
    public LegatoFootswitchMessage(byte channel, bool isLegato)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, (byte)(isLegato ? 127 : 0)))
    { }

    public LegatoFootswitchMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(LegatoFootswitchMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>63 and below is normal, 64 and above is legato.</summary>
    public bool IsLegato => _data.Byte(2) >= 64;
}

/// <summary>Hold 2 (CC 69): the second hold pedal on or off.</summary>
public readonly record struct Hold2Message
{
    public const byte Controller = 69;
    private readonly uint _data;

    /// <summary>Creates a Hold2Message from its parameters.</summary>
    public Hold2Message(byte channel, bool isOn)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, (byte)(isOn ? 127 : 0)))
    { }

    public Hold2Message(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(Hold2Message));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>63 and below is off, 64 and above is on.</summary>
    public bool IsOn => _data.Byte(2) >= 64;
}

/// <summary>
/// Sound Controller 1-10 (CC 70-79): sound variation, timbre, release time, attack time, brightness,
/// decay time, vibrato rate, vibrato depth, vibrato delay and an undefined controller (by default).
/// </summary>
public readonly record struct SoundControllerMessage
{
    public const byte FirstController = 70;
    public const byte LastController = 79;
    private readonly uint _data;

    /// <summary>Creates a SoundControllerMessage from its parameters.</summary>
    public SoundControllerMessage(byte channel, int number, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)(FirstController + PackedMessage.CheckRange(number, 1, LastController - FirstController + 1) - 1), value))
    { }

    public SoundControllerMessage(uint data)
        => _data = ControllerMessage.Validate(data, FirstController, LastController, nameof(SoundControllerMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>The sound controller number: 1 to 10.</summary>
    public int Number => _data.Byte(1) - FirstController + 1;

    public byte Value => _data.Byte(2);
}

/// <summary>Portamento Control (CC 84): starts a portamento from the given source note number.</summary>
public readonly record struct PortamentoControlMessage
{
    public const byte Controller = 84;
    private readonly uint _data;

    /// <summary>Creates a PortamentoControlMessage from its parameters.</summary>
    public PortamentoControlMessage(byte channel, byte sourceNote)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, sourceNote))
    { }

    public PortamentoControlMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(PortamentoControlMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte SourceNote => _data.Byte(2);
}

/// <summary>High Resolution Velocity Prefix (CC 88): the LSB of the velocity of the next note on or off.</summary>
public readonly record struct HighResolutionVelocityPrefixMessage
{
    public const byte Controller = 88;
    private readonly uint _data;

    /// <summary>Creates a HighResolutionVelocityPrefixMessage from its parameters.</summary>
    public HighResolutionVelocityPrefixMessage(byte channel, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller, value))
    { }

    public HighResolutionVelocityPrefixMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(HighResolutionVelocityPrefixMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
    public byte Value => _data.Byte(2);
}

/// <summary>
/// Effects 1-5 Depth (CC 91-95): the send level of effects 1 to 5
/// (by default reverb, tremolo, chorus, celeste/detune and phaser).
/// </summary>
public readonly record struct EffectsDepthMessage
{
    public const byte FirstController = 91;
    public const byte LastController = 95;
    private readonly uint _data;

    /// <summary>Creates a EffectsDepthMessage from its parameters.</summary>
    public EffectsDepthMessage(byte channel, int number, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)(FirstController + PackedMessage.CheckRange(number, 1, LastController - FirstController + 1) - 1), value))
    { }

    public EffectsDepthMessage(uint data)
        => _data = ControllerMessage.Validate(data, FirstController, LastController, nameof(EffectsDepthMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>The effect number: 1 to 5.</summary>
    public int Number => _data.Byte(1) - FirstController + 1;

    public byte Value => _data.Byte(2);
}

/// <summary>Data Increment (CC 96): increments the value of the selected RPN or NRPN by one.</summary>
public readonly record struct DataIncrementMessage
{
    public const byte Controller = 96;
    private readonly uint _data;

    /// <summary>Creates a DataIncrementMessage from its parameters.</summary>
    public DataIncrementMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller))
    { }

    public DataIncrementMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(DataIncrementMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}

/// <summary>Data Decrement (CC 97): decrements the value of the selected RPN or NRPN by one.</summary>
public readonly record struct DataDecrementMessage
{
    public const byte Controller = 97;
    private readonly uint _data;

    /// <summary>Creates a DataDecrementMessage from its parameters.</summary>
    public DataDecrementMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, Controller))
    { }

    public DataDecrementMessage(uint data)
        => _data = ControllerMessage.Validate(data, Controller, Controller, nameof(DataDecrementMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}

/// <summary>Non-Registered Parameter Number (CC 98 LSB, CC 99 MSB): selects a manufacturer specific parameter.</summary>
public readonly record struct NrpnMessage
{
    public const byte FirstController = 98;
    public const byte LastController = 99;
    private readonly uint _data;

    /// <summary>Creates a NrpnMessage from its parameters.</summary>
    public NrpnMessage(byte channel, bool isMsb, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, isMsb ? LastController : FirstController, value))
    { }

    public NrpnMessage(uint data)
        => _data = ControllerMessage.Validate(data, FirstController, LastController, nameof(NrpnMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>True for the MSB (CC 99), false for the LSB (CC 98).</summary>
    public bool IsMsb => _data.Byte(1) == LastController;

    public byte Value => _data.Byte(2);
}

/// <summary>Registered Parameter Number (CC 100 LSB, CC 101 MSB): selects a registered (standardized) parameter.</summary>
public readonly record struct RpnMessage
{
    public const byte FirstController = 100;
    public const byte LastController = 101;
    private readonly uint _data;

    /// <summary>Creates a RpnMessage from its parameters.</summary>
    public RpnMessage(byte channel, bool isMsb, byte value)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, isMsb ? LastController : FirstController, value))
    { }

    public RpnMessage(uint data)
        => _data = ControllerMessage.Validate(data, FirstController, LastController, nameof(RpnMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    /// <summary>True for the MSB (CC 101), false for the LSB (CC 100).</summary>
    public bool IsMsb => _data.Byte(1) == LastController;

    public byte Value => _data.Byte(2);
}
