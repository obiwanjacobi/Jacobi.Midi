using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Jacobi.Midi.Messages;

/// <summary>Identifies which kind of message a <see cref="MidiEvent"/> holds.</summary>
public enum MidiEventKind : byte
{
    None = 0,
    Message,
    SystemExclusive,
    Meta,
}

/// <summary>
/// One entry in a list of MIDI data: a short <see cref="MidiMessage"/>, a <see cref="SystemExclusiveMessage"/>
/// or a <see cref="MidiMetaMessage"/>.
/// </summary>
/// <remarks>
/// Short messages are stored inline in a uint (no allocation). System exclusive and meta messages are
/// copied into an array owned by the event, so an event never depends on the buffer it was read from.
/// Layout: 4 bytes of short message + an object reference = 16 bytes on 64-bit (with padding).
/// </remarks>
[Union]
public readonly record struct MidiEvent : IUnion
{
    private readonly uint _short;
    private readonly byte[]? _long;

    public MidiEvent(MidiMessage message)
    {
        if (!message.HasValue)
            throw new ArgumentException("The message has no value.", nameof(message));

        _short = MidiWire.Pack(message);
    }

    /// <summary>Copies the system exclusive bytes into the event.</summary>
    public MidiEvent(SystemExclusiveMessage message)
    {
        if (!message.HasValue)
            throw new ArgumentException("The message has no value.", nameof(message));

        _long = message.Bytes.ToArray();
    }

    /// <summary>Copies the meta event bytes into the event.</summary>
    public MidiEvent(MidiMetaMessage message)
    {
        if (!message.HasValue)
            throw new ArgumentException("The message has no value.", nameof(message));

        _long = message.Bytes.ToArray();
    }

    private MidiEvent(byte[] ownedBytes)
    {
        _long = ownedBytes;
    }

    /// <summary>Wraps already validated sysex or meta bytes without copying; the event takes ownership of the array.</summary>
    internal static MidiEvent FromOwnedBytes(byte[] bytes) => new(bytes);

    public static implicit operator MidiEvent(MidiMessage message) => new(message);
    public static implicit operator MidiEvent(SystemExclusiveMessage message) => new(message);
    public static implicit operator MidiEvent(MidiMetaMessage message) => new(message);

    public MidiEventKind Kind
    {
        get
        {
            if (_long is null)
                return new MidiMessage(_short).HasValue ? MidiEventKind.Message : MidiEventKind.None;

            return _long[0] == SysEx.Start ? MidiEventKind.SystemExclusive : MidiEventKind.Meta;
        }
    }

    // IUnion implementation
    public object? Value => Kind switch
    {
        MidiEventKind.Message => new MidiMessage(_short),
        MidiEventKind.SystemExclusive => new SystemExclusiveMessage(_long!),
        MidiEventKind.Meta => new MidiMetaMessage(_long!),
        _ => null,
    };

    public bool HasValue => Kind != MidiEventKind.None;

    // non-boxing union accessors
    public bool TryGetValue([NotNullWhen(true)] out MidiMessage? value)
    {
        if (Kind == MidiEventKind.Message) { value = new MidiMessage(_short); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SystemExclusiveMessage? value)
    {
        if (Kind == MidiEventKind.SystemExclusive) { value = new SystemExclusiveMessage(_long!); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MidiMetaMessage? value)
    {
        if (Kind == MidiEventKind.Meta) { value = new MidiMetaMessage(_long!); return true; }
        value = null;
        return false;
    }
}
