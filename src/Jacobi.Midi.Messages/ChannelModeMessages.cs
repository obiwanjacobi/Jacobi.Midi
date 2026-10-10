using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Jacobi.Midi.Messages;

/// <summary>
/// A union over the channel mode messages: control change messages with controller numbers 120-127.
/// </summary>
[Union]
public readonly record struct ChannelModeMessage : IUnion
{
    internal const byte FirstController = (byte)ControllerNumber.AllSoundOff;

    private readonly uint _data;

    public ChannelModeMessage(uint data)
    {
        MidiMessage.Validate(data, MidiMessageKind.ControlChange, nameof(ChannelModeMessage));
        if (data.Byte(1) < FirstController)
            throw new ArgumentException($"{nameof(ChannelModeMessage)} requires a controller number of {FirstController} or higher, but got {data.Byte(1)}.", nameof(data));
        _data = data;
    }

    public ChannelModeMessage(ControlChangeMessage message)
        : this(message.Data)
    { }

    internal static uint Validate(uint data, ControllerNumber controller, string messageName)
    {
        MidiMessage.Validate(data, MidiMessageKind.ControlChange, messageName);
        if (data.Byte(1) != (byte)controller)
            throw new ArgumentException($"{messageName} requires controller number {controller}, but got {data.Byte(1)}.", nameof(data));
        return data;
    }

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    // IUnion implementation
    public object? Value => HasValue ? _data : null;

    public bool HasValue => (_data.Byte(0) & 0xF0) == (byte)MidiMessageKind.ControlChange
        && _data.Byte(1) >= FirstController;

    private bool Is(ControllerNumber controller) => HasValue && _data.Byte(1) == (byte)controller;

    // non-boxing union accessors (typed views over the same memory, no copy)
    public bool TryGetValue([NotNullWhen(true)] out AllSoundOffMessage? value)
    {
        if (Is(ControllerNumber.AllSoundOff)) { value = new AllSoundOffMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out ResetAllControllersMessage? value)
    {
        if (Is(ControllerNumber.ResetAllControllers)) { value = new ResetAllControllersMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out LocalControlMessage? value)
    {
        if (Is(ControllerNumber.LocalControl)) { value = new LocalControlMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out AllNotesOffMessage? value)
    {
        if (Is(ControllerNumber.AllNotesOff)) { value = new AllNotesOffMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out OmniModeOffMessage? value)
    {
        if (Is(ControllerNumber.OmniModeOff)) { value = new OmniModeOffMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out OmniModeOnMessage? value)
    {
        if (Is(ControllerNumber.OmniModeOn)) { value = new OmniModeOnMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MonoModeOnMessage? value)
    {
        if (Is(ControllerNumber.MonoModeOn)) { value = new MonoModeOnMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out PolyModeOnMessage? value)
    {
        if (Is(ControllerNumber.PolyModeOn)) { value = new PolyModeOnMessage(_data); return true; }
        value = null;
        return false;
    }
}

/// <summary>All Sound Off (controller 120): silences all sounding notes immediately.</summary>
public readonly record struct AllSoundOffMessage
{
    private readonly uint _data;

    /// <summary>Creates a AllSoundOffMessage from its parameters.</summary>
    public AllSoundOffMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.AllSoundOff))
    { }

    public AllSoundOffMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.AllSoundOff, nameof(AllSoundOffMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}

/// <summary>Reset All Controllers (controller 121): resets controllers to their default values.</summary>
public readonly record struct ResetAllControllersMessage
{
    private readonly uint _data;

    /// <summary>Creates a ResetAllControllersMessage from its parameters.</summary>
    public ResetAllControllersMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.ResetAllControllers))
    { }

    public ResetAllControllersMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.ResetAllControllers, nameof(ResetAllControllersMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}

/// <summary>Local Control (controller 122): connects or disconnects the keyboard from the local sound generator.</summary>
public readonly record struct LocalControlMessage
{
    private readonly uint _data;

    /// <summary>Creates a LocalControlMessage from its parameters.</summary>
    public LocalControlMessage(byte channel, bool isOn)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.LocalControl, (byte)(isOn ? 127 : 0)))
    { }

    public LocalControlMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.LocalControl, nameof(LocalControlMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    // 0 = off, 127 = on
    public bool IsOn => _data.Byte(2) >= 64;
}

/// <summary>All Notes Off (controller 123): releases all notes that are currently on.</summary>
public readonly record struct AllNotesOffMessage
{
    private readonly uint _data;

    /// <summary>Creates a AllNotesOffMessage from its parameters.</summary>
    public AllNotesOffMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.AllNotesOff))
    { }

    public AllNotesOffMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.AllNotesOff, nameof(AllNotesOffMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}

/// <summary>Omni Mode Off (controller 124): the receiver responds only to its own channel.</summary>
public readonly record struct OmniModeOffMessage
{
    private readonly uint _data;

    /// <summary>Creates a OmniModeOffMessage from its parameters.</summary>
    public OmniModeOffMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.OmniModeOff))
    { }

    public OmniModeOffMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.OmniModeOff, nameof(OmniModeOffMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}

/// <summary>Omni Mode On (controller 125): the receiver responds to messages on all channels.</summary>
public readonly record struct OmniModeOnMessage
{
    private readonly uint _data;

    /// <summary>Creates a OmniModeOnMessage from its parameters.</summary>
    public OmniModeOnMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.OmniModeOn))
    { }

    public OmniModeOnMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.OmniModeOn, nameof(OmniModeOnMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}

/// <summary>Mono Mode On (controller 126): voices play monophonically, one per channel.</summary>
public readonly record struct MonoModeOnMessage
{
    private readonly uint _data;

    /// <summary>Creates a MonoModeOnMessage from its parameters.</summary>
    public MonoModeOnMessage(byte channel, byte channelCount)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.MonoModeOn, channelCount))
    { }

    public MonoModeOnMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.MonoModeOn, nameof(MonoModeOnMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);

    // number of mono channels; 0 = as many as there are voices
    public byte ChannelCount => _data.Byte(2);
}

/// <summary>Poly Mode On (controller 127): voices play polyphonically.</summary>
public readonly record struct PolyModeOnMessage
{
    private readonly uint _data;

    /// <summary>Creates a PolyModeOnMessage from its parameters.</summary>
    public PolyModeOnMessage(byte channel)
        : this(PackedMessage.Pack(MidiMessageKind.ControlChange, channel, (byte)ControllerNumber.PolyModeOn))
    { }

    public PolyModeOnMessage(uint data)
        => _data = ChannelModeMessage.Validate(data, ControllerNumber.PolyModeOn, nameof(PolyModeOnMessage));

    public byte Channel => (byte)(_data.Byte(0) & 0x0F);
}
