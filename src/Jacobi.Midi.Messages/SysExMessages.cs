using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Jacobi.Midi.Messages;

/// <summary>
/// Helpers for the system exclusive layout: 0xF0, data bytes, optional 0xF7 terminator.
/// The 'body' is everything between the 0xF0 and the (optional) 0xF7.
/// </summary>
internal static class SysEx
{
    public const byte Start = 0xF0;
    public const byte End = 0xF7;
    public const byte NonRealTimeId = 0x7E;
    public const byte RealTimeId = 0x7F;

    /// <summary>Used as sub id 2 to indicate a message does not have one.</summary>
    public const int None = -1;

    public static bool TryGetBody(ReadOnlyMemory<byte> data, out ReadOnlyMemory<byte> body)
    {
        body = default;
        if (data.Length < 2 || data.Span[0] != Start)
            return false;

        var end = data.Span[^1] == End ? data.Length - 1 : data.Length;
        body = data.Slice(1, end - 1);
        return true;
    }

    // a manufacturer id is 1 byte, or 3 bytes when the first byte is 0
    public static bool TryGetManufacturerId(ReadOnlySpan<byte> bytes, out int id, out int idLength)
    {
        id = 0;
        idLength = 0;
        if (bytes.IsEmpty)
            return false;

        if (bytes[0] == 0)
        {
            if (bytes.Length < 3)
                return false;

            id = (bytes[1] << 8) | bytes[2];
            idLength = 3;
            return true;
        }

        id = bytes[0];
        idLength = 1;
        return true;
    }

    public static int Read14(ReadOnlySpan<byte> bytes, int index)
        => bytes[index] | (bytes[index + 1] << 7);

    public static int Read21(ReadOnlySpan<byte> bytes, int index)
        => bytes[index] | (bytes[index + 1] << 7) | (bytes[index + 2] << 14);
}

/// <summary>
/// A system exclusive message (0xF0, data, optional 0xF7), backed by the raw message bytes (no copy).
/// Use <see cref="TryGetUniversal"/> to get at the universal (real-time and non-real-time) messages.
/// </summary>
public readonly record struct SystemExclusiveMessage
{
    private readonly ReadOnlyMemory<byte> _data;

    public SystemExclusiveMessage(ReadOnlyMemory<byte> data)
    {
        if (!Handles(data))
            throw new ArgumentException($"{nameof(SystemExclusiveMessage)} requires a system exclusive message (0xF0, data).", nameof(data));
        _data = data;
    }

    internal static bool Handles(ReadOnlyMemory<byte> data)
        => SysEx.TryGetBody(data, out var body) && !body.IsEmpty;

    public bool HasValue => Handles(_data);

    /// <summary>The raw message bytes, including the 0xF0 and the 0xF7 when present.</summary>
    public ReadOnlyMemory<byte> Bytes => _data;

    private ReadOnlySpan<byte> Body => SysEx.TryGetBody(_data, out var body) ? body.Span : default;

    /// <summary>True for the universal real-time (0x7F) and non-real-time (0x7E) messages.</summary>
    public bool IsUniversal => UniversalSysExMessage.Handles(_data);

    /// <summary>True when the manufacturer id is the 3 byte (0x00 xx yy) form.</summary>
    public bool IsExtendedManufacturerId => Body[0] == 0;

    /// <summary>
    /// The manufacturer id: the single byte, or (xx &lt;&lt; 8 | yy) for the extended form.
    /// For universal messages this is 0x7E or 0x7F.
    /// </summary>
    public int ManufacturerId
    {
        get
        {
            SysEx.TryGetManufacturerId(Body, out var id, out _);
            return id;
        }
    }

    /// <summary>The data following the manufacturer id.</summary>
    public ReadOnlySpan<byte> Data => Body[(IsExtendedManufacturerId ? 3 : 1)..];

    public bool TryGetUniversal([NotNullWhen(true)] out UniversalSysExMessage? value)
    {
        if (UniversalSysExMessage.Handles(_data)) { value = new UniversalSysExMessage(_data); return true; }
        value = null;
        return false;
    }
}

/// <summary>The sub id 1 values of the universal non-real-time system exclusive messages (0x7E).</summary>
public enum UniversalNonRealTimeId : byte
{
    SampleDumpHeader = 0x01,
    SampleDataPacket = 0x02,
    SampleDumpRequest = 0x03,
    MidiTimeCode = 0x04,
    SampleDumpExtensions = 0x05,
    GeneralInformation = 0x06,
    FileDump = 0x07,
    MidiTuningStandard = 0x08,
    GeneralMidi = 0x09,
    DownloadableSounds = 0x0A,
    FileReference = 0x0B,
    MidiVisualControl = 0x0C,
    MidiCapabilityInquiry = 0x0D,
    EndOfFile = 0x7B,
    Wait = 0x7C,
    Cancel = 0x7D,
    Nak = 0x7E,
    Ack = 0x7F,
}

/// <summary>The sub id 1 values of the universal real-time system exclusive messages (0x7F).</summary>
public enum UniversalRealTimeId : byte
{
    MidiTimeCode = 0x01,
    MidiShowControl = 0x02,
    NotationInformation = 0x03,
    DeviceControl = 0x04,
    MidiTimeCodeCueing = 0x05,
    MidiMachineControlCommands = 0x06,
    MidiMachineControlResponses = 0x07,
    MidiTuningStandard = 0x08,
    ControllerDestinationSetting = 0x09,
    KeyBasedInstrumentControl = 0x0A,
    ScalablePolyphonyMip = 0x0B,
    MobilePhoneControl = 0x0C,
}

/// <summary>
/// A union over the universal system exclusive messages (0xF0, 0x7E or 0x7F, device id, sub id 1, ...),
/// backed by the raw message bytes (no copy). Messages that have no specific type are available through
/// <see cref="SubId1"/>, <see cref="SubId2"/> and <see cref="Data"/>.
/// </summary>
[Union]
public readonly record struct UniversalSysExMessage : IUnion
{
    private readonly ReadOnlyMemory<byte> _data;

    public UniversalSysExMessage(ReadOnlyMemory<byte> data)
    {
        if (!Handles(data))
            throw new ArgumentException($"{nameof(UniversalSysExMessage)} requires a universal system exclusive message (0xF0, 0x7E or 0x7F, device id, sub id).", nameof(data));
        _data = data;
    }

    internal static bool Handles(ReadOnlyMemory<byte> data)
        => SysEx.TryGetBody(data, out var body)
            && body.Length >= 3
            && (body.Span[0] == SysEx.NonRealTimeId || body.Span[0] == SysEx.RealTimeId);

    internal static bool TryValidate(ReadOnlyMemory<byte> data, bool realTime, byte subId1, int subId2, int minData, int maxData,
        out ReadOnlyMemory<byte> body, out string? error)
        => TryValidate(data, realTime, subId1, subId2, subId2, minData, maxData, out body, out error);

    // sub id 2 must be in [subId2Min, subId2Max]; use SysEx.None for both when the message has no sub id 2
    internal static bool TryValidate(ReadOnlyMemory<byte> data, bool realTime, byte subId1, int subId2Min, int subId2Max,
        int minData, int maxData, out ReadOnlyMemory<byte> body, out string? error)
    {
        body = default;
        error = null;

        if (!Handles(data))
        {
            error = "requires a universal system exclusive message (0xF0, 0x7E or 0x7F, device id, sub id)";
            return false;
        }

        SysEx.TryGetBody(data, out var candidate);
        var span = candidate.Span;

        if ((span[0] == SysEx.RealTimeId) != realTime)
        {
            error = realTime ? "requires a real-time message (0x7F)" : "requires a non-real-time message (0x7E)";
            return false;
        }

        if (span[2] != subId1)
        {
            error = $"requires sub id 1 0x{subId1:X2}, but got 0x{span[2]:X2}";
            return false;
        }

        var headerLength = 3;
        if (subId2Max >= 0)
        {
            headerLength = 4;
            if (candidate.Length < headerLength)
            {
                error = "requires a sub id 2";
                return false;
            }

            if (span[3] < subId2Min || span[3] > subId2Max)
            {
                error = subId2Min == subId2Max
                    ? $"requires sub id 2 0x{subId2Min:X2}, but got 0x{span[3]:X2}"
                    : $"requires a sub id 2 from 0x{subId2Min:X2} to 0x{subId2Max:X2}, but got 0x{span[3]:X2}";
                return false;
            }
        }

        var dataLength = candidate.Length - headerLength;
        if (dataLength < minData || dataLength > maxData)
        {
            error = minData == maxData
                ? $"requires {minData} data bytes, but got {dataLength}"
                : $"requires {minData} to {maxData} data bytes, but got {dataLength}";
            return false;
        }

        body = candidate;
        return true;
    }

    internal static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> data, bool realTime, byte subId1, int subId2, int minData, int maxData, string messageName)
        => TryValidate(data, realTime, subId1, subId2, minData, maxData, out var body, out var error)
            ? body
            : throw new ArgumentException($"{messageName} {error}.", nameof(data));

    internal static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> data, bool realTime, byte subId1, int subId2Min, int subId2Max, int minData, int maxData, string messageName)
        => TryValidate(data, realTime, subId1, subId2Min, subId2Max, minData, maxData, out var body, out var error)
            ? body
            : throw new ArgumentException($"{messageName} {error}.", nameof(data));

    private ReadOnlySpan<byte> Body => SysEx.TryGetBody(_data, out var body) ? body.Span : default;

    /// <summary>True for real-time messages (0x7F), false for non-real-time messages (0x7E).</summary>
    public bool IsRealTime => Body[0] == SysEx.RealTimeId;

    /// <summary>The device id (0x7F addresses all devices).</summary>
    public byte DeviceId => Body[1];

    public byte SubId1 => Body[2];

    /// <summary>The sub id 1 as a non-real-time id; null for real-time messages.</summary>
    public UniversalNonRealTimeId? NonRealTimeId => IsRealTime ? null : (UniversalNonRealTimeId)SubId1;

    /// <summary>The sub id 1 as a real-time id; null for non-real-time messages.</summary>
    public UniversalRealTimeId? RealTimeId => IsRealTime ? (UniversalRealTimeId)SubId1 : null;

    /// <summary>The sub id 2, or null when the message has none (not every message has one).</summary>
    public byte? SubId2 => Body.Length > 3 ? Body[3] : null;

    /// <summary>The bytes following sub id 1 (including sub id 2 when present).</summary>
    public ReadOnlySpan<byte> Data => Body[3..];

    // IUnion implementation
    public object? Value => HasValue ? _data : null;

    public bool HasValue => Handles(_data);

    // non-boxing union accessors (typed views over the same memory, no copy)
    public bool TryGetValue([NotNullWhen(true)] out SampleDumpHeaderMessage? value)
    {
        if (SampleDumpHeaderMessage.Matches(_data)) { value = new SampleDumpHeaderMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SampleDataPacketMessage? value)
    {
        if (SampleDataPacketMessage.Matches(_data)) { value = new SampleDataPacketMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out SampleDumpRequestMessage? value)
    {
        if (SampleDumpRequestMessage.Matches(_data)) { value = new SampleDumpRequestMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TimeCodeCueingMessage? value)
    {
        if (TimeCodeCueingMessage.Matches(_data)) { value = new TimeCodeCueingMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out IdentityRequestMessage? value)
    {
        if (IdentityRequestMessage.Matches(_data)) { value = new IdentityRequestMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out IdentityReplyMessage? value)
    {
        if (IdentityReplyMessage.Matches(_data)) { value = new IdentityReplyMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out GeneralMidi1SystemOnMessage? value)
    {
        if (GeneralMidi1SystemOnMessage.Matches(_data)) { value = new GeneralMidi1SystemOnMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out GeneralMidiSystemOffMessage? value)
    {
        if (GeneralMidiSystemOffMessage.Matches(_data)) { value = new GeneralMidiSystemOffMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out GeneralMidi2SystemOnMessage? value)
    {
        if (GeneralMidi2SystemOnMessage.Matches(_data)) { value = new GeneralMidi2SystemOnMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out DownloadableSoundsMessage? value)
    {
        if (DownloadableSoundsMessage.Matches(_data)) { value = new DownloadableSoundsMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out EndOfFileMessage? value)
    {
        if (EndOfFileMessage.Matches(_data)) { value = new EndOfFileMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out WaitMessage? value)
    {
        if (WaitMessage.Matches(_data)) { value = new WaitMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out CancelMessage? value)
    {
        if (CancelMessage.Matches(_data)) { value = new CancelMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out NakMessage? value)
    {
        if (NakMessage.Matches(_data)) { value = new NakMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out AckMessage? value)
    {
        if (AckMessage.Matches(_data)) { value = new AckMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out FullTimeCodeMessage? value)
    {
        if (FullTimeCodeMessage.Matches(_data)) { value = new FullTimeCodeMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TimeCodeUserBitsMessage? value)
    {
        if (TimeCodeUserBitsMessage.Matches(_data)) { value = new TimeCodeUserBitsMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out BarNumberMessage? value)
    {
        if (BarNumberMessage.Matches(_data)) { value = new BarNumberMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out TimeSignatureNotationMessage? value)
    {
        if (TimeSignatureNotationMessage.Matches(_data)) { value = new TimeSignatureNotationMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MasterVolumeMessage? value)
    {
        if (MasterVolumeMessage.Matches(_data)) { value = new MasterVolumeMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MasterBalanceMessage? value)
    {
        if (MasterBalanceMessage.Matches(_data)) { value = new MasterBalanceMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MasterFineTuningMessage? value)
    {
        if (MasterFineTuningMessage.Matches(_data)) { value = new MasterFineTuningMessage(_data); return true; }
        value = null;
        return false;
    }

    public bool TryGetValue([NotNullWhen(true)] out MasterCoarseTuningMessage? value)
    {
        if (MasterCoarseTuningMessage.Matches(_data)) { value = new MasterCoarseTuningMessage(_data); return true; }
        value = null;
        return false;
    }
}

/// <summary>The loop types of a sample dump header.</summary>
public enum SampleLoopType : byte
{
    Forward = 0x00,
    BackwardForward = 0x01,
    Off = 0x7F,
}

/// <summary>Sample Dump Header (7E, 01): announces a sample dump and describes the sample.</summary>
public readonly record struct SampleDumpHeaderMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x01;
    private const int HeaderLength = 3;
    private const int DataLength = 16;
    private readonly ReadOnlyMemory<byte> _body;

    public SampleDumpHeaderMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, DataLength, DataLength, nameof(SampleDumpHeaderMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, DataLength, DataLength, out _, out _);

    private ReadOnlySpan<byte> Payload => _body.Span[HeaderLength..];

    public byte DeviceId => _body.Span[1];
    public int SampleNumber => SysEx.Read14(Payload, 0);
    public byte SampleFormatBits => Payload[2];
    public int SamplePeriodNanoseconds => SysEx.Read21(Payload, 3);
    public int LengthInWords => SysEx.Read21(Payload, 6);
    public int LoopStartPoint => SysEx.Read21(Payload, 9);
    public int LoopEndPoint => SysEx.Read21(Payload, 12);
    public SampleLoopType LoopType => (SampleLoopType)Payload[15];
}

/// <summary>Sample Data Packet (7E, 02): a packet of 120 sample data bytes with a checksum.</summary>
public readonly record struct SampleDataPacketMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x02;
    private const int HeaderLength = 3;
    private const int DataLength = 122;
    private readonly ReadOnlyMemory<byte> _body;

    public SampleDataPacketMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, DataLength, DataLength, nameof(SampleDataPacketMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, DataLength, DataLength, out _, out _);

    private ReadOnlySpan<byte> Payload => _body.Span[HeaderLength..];

    public byte DeviceId => _body.Span[1];
    public byte PacketNumber => Payload[0];
    public ReadOnlySpan<byte> SampleData => Payload.Slice(1, 120);
    public byte Checksum => Payload[121];

    /// <summary>The checksum is the XOR of all bytes from the 0x7E up to (excluding) the checksum.</summary>
    public bool IsChecksumValid
    {
        get
        {
            var span = _body.Span[..^1];
            var xor = 0;
            foreach (var b in span)
                xor ^= b;
            return (xor & 0x7F) == Checksum;
        }
    }
}

/// <summary>Sample Dump Request (7E, 03): requests a sample to be dumped.</summary>
public readonly record struct SampleDumpRequestMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x03;
    private const int HeaderLength = 3;
    private const int DataLength = 2;
    private readonly ReadOnlyMemory<byte> _body;

    public SampleDumpRequestMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, DataLength, DataLength, nameof(SampleDumpRequestMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, DataLength, DataLength, out _, out _);

    public byte DeviceId => _body.Span[1];
    public int SampleNumber => SysEx.Read14(_body.Span[HeaderLength..], 0);
}

/// <summary>The types of MIDI time code cueing messages.</summary>
public enum TimeCodeCueingType : byte
{
    Special = 0x00,
    PunchIn = 0x01,
    PunchOut = 0x02,
    DeletePunchIn = 0x03,
    DeletePunchOut = 0x04,
    EventStart = 0x05,
    EventStop = 0x06,
    EventStartWithInfo = 0x07,
    EventStopWithInfo = 0x08,
    DeleteEventStart = 0x09,
    DeleteEventStop = 0x0A,
    CuePoint = 0x0B,
    CuePointWithInfo = 0x0C,
    DeleteCuePoint = 0x0D,
    EventName = 0x0E,
}

/// <summary>
/// MIDI Time Code cueing (non-real-time 7E, 04 and real-time 7F, 05): cue points, punch points
/// and event points at a time code position.
/// </summary>
public readonly record struct TimeCodeCueingMessage
{
    internal const byte NonRealTimeSubId1 = 0x04;
    internal const byte RealTimeSubId1 = 0x05;
    private const int HeaderLength = 4;
    private const int MinDataLength = 7;
    private readonly ReadOnlyMemory<byte> _body;

    public TimeCodeCueingMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, IsRealTime(data), SubId1For(data),
            (int)TimeCodeCueingType.Special, (int)TimeCodeCueingType.EventName, MinDataLength, int.MaxValue, nameof(TimeCodeCueingMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, IsRealTime(data), SubId1For(data),
            (int)TimeCodeCueingType.Special, (int)TimeCodeCueingType.EventName, MinDataLength, int.MaxValue, out _, out _);

    private static bool IsRealTime(ReadOnlyMemory<byte> data)
        => SysEx.TryGetBody(data, out var body) && !body.IsEmpty && body.Span[0] == SysEx.RealTimeId;

    private static byte SubId1For(ReadOnlyMemory<byte> data)
        => IsRealTime(data) ? RealTimeSubId1 : NonRealTimeSubId1;

    private ReadOnlySpan<byte> Payload => _body.Span[HeaderLength..];

    public bool IsRealTimeMessage => _body.Span[0] == SysEx.RealTimeId;
    public byte DeviceId => _body.Span[1];
    public TimeCodeCueingType Type => (TimeCodeCueingType)_body.Span[3];
    public SmpteFrameRate FrameRate => (SmpteFrameRate)((Payload[0] >> 5) & 0x03);
    public byte Hours => (byte)(Payload[0] & 0x1F);
    public byte Minutes => Payload[1];
    public byte Seconds => Payload[2];
    public byte Frames => Payload[3];
    public byte FractionalFrames => Payload[4];

    /// <summary>The event number; for the Special type this is the setup type.</summary>
    public int EventNumber => SysEx.Read14(Payload, 5);

    /// <summary>The additional information (for example the event name); can be empty.</summary>
    public ReadOnlySpan<byte> AdditionalInfo => Payload[7..];
}

/// <summary>Identity Request (7E, 06 01): asks a device to identify itself.</summary>
public readonly record struct IdentityRequestMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x06;
    internal const int SubId2 = 0x01;
    private readonly ReadOnlyMemory<byte> _body;

    public IdentityRequestMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 0, 0, nameof(IdentityRequestMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 0, 0, out _, out _);

    public byte DeviceId => _body.Span[1];
}

/// <summary>Identity Reply (7E, 06 02): the manufacturer, device family, member and software revision of a device.</summary>
public readonly record struct IdentityReplyMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x06;
    internal const int SubId2 = 0x02;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public IdentityReplyMessage(ReadOnlyMemory<byte> data)
    {
        if (!TryGetBody(data, out var body, out var error))
            throw new ArgumentException($"{nameof(IdentityReplyMessage)} {error}.", nameof(data));
        _body = body;
    }

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => TryGetBody(data, out _, out _);

    // 1 or 3 byte manufacturer id + family (2) + member (2) + revision (4)
    private static bool TryGetBody(ReadOnlyMemory<byte> data, out ReadOnlyMemory<byte> body, out string? error)
    {
        if (!UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 9, 11, out body, out error))
            return false;

        var payload = body.Span[HeaderLength..];
        SysEx.TryGetManufacturerId(payload, out _, out var idLength);
        if (payload.Length != idLength + 8)
        {
            error = $"requires {idLength + 8} data bytes for this manufacturer id, but got {payload.Length}";
            return false;
        }
        return true;
    }

    private ReadOnlySpan<byte> Payload => _body.Span[HeaderLength..];

    private int ManufacturerIdLength => Payload[0] == 0 ? 3 : 1;

    public byte DeviceId => _body.Span[1];

    /// <summary>True when the manufacturer id is the 3 byte (0x00 xx yy) form.</summary>
    public bool IsExtendedManufacturerId => Payload[0] == 0;

    /// <summary>The manufacturer id: the single byte, or (xx &lt;&lt; 8 | yy) for the extended form.</summary>
    public int ManufacturerId
    {
        get
        {
            SysEx.TryGetManufacturerId(Payload, out var id, out _);
            return id;
        }
    }

    public int DeviceFamily => SysEx.Read14(Payload, ManufacturerIdLength);
    public int DeviceFamilyMember => SysEx.Read14(Payload, ManufacturerIdLength + 2);

    /// <summary>The 4 bytes of the software revision level.</summary>
    public ReadOnlySpan<byte> SoftwareRevision => Payload.Slice(ManufacturerIdLength + 4, 4);
}

/// <summary>General MIDI 1 System On (7E, 09 01): switches a device to General MIDI 1 mode.</summary>
public readonly record struct GeneralMidi1SystemOnMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x09;
    internal const int SubId2 = 0x01;
    private readonly ReadOnlyMemory<byte> _body;

    public GeneralMidi1SystemOnMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 0, 0, nameof(GeneralMidi1SystemOnMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 0, 0, out _, out _);

    public byte DeviceId => _body.Span[1];
}

/// <summary>General MIDI System Off (7E, 09 02): switches a device out of General MIDI mode.</summary>
public readonly record struct GeneralMidiSystemOffMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x09;
    internal const int SubId2 = 0x02;
    private readonly ReadOnlyMemory<byte> _body;

    public GeneralMidiSystemOffMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 0, 0, nameof(GeneralMidiSystemOffMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 0, 0, out _, out _);

    public byte DeviceId => _body.Span[1];
}

/// <summary>General MIDI 2 System On (7E, 09 03): switches a device to General MIDI 2 mode.</summary>
public readonly record struct GeneralMidi2SystemOnMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x09;
    internal const int SubId2 = 0x03;
    private readonly ReadOnlyMemory<byte> _body;

    public GeneralMidi2SystemOnMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 0, 0, nameof(GeneralMidi2SystemOnMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 0, 0, out _, out _);

    public byte DeviceId => _body.Span[1];
}

/// <summary>The Downloadable Sounds commands.</summary>
public enum DownloadableSoundsCommand : byte
{
    TurnOn = 0x01,
    TurnOff = 0x02,
    VoiceAllocationOff = 0x03,
    VoiceAllocationOn = 0x04,
}

/// <summary>Downloadable Sounds (7E, 0A 01-04): turns DLS or DLS voice allocation on or off.</summary>
public readonly record struct DownloadableSoundsMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x0A;
    private const int FirstSubId2 = (int)DownloadableSoundsCommand.TurnOn;
    private const int LastSubId2 = (int)DownloadableSoundsCommand.VoiceAllocationOn;
    private readonly ReadOnlyMemory<byte> _body;

    public DownloadableSoundsMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, FirstSubId2, LastSubId2, 0, 0, nameof(DownloadableSoundsMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, FirstSubId2, LastSubId2, 0, 0, out _, out _);

    public byte DeviceId => _body.Span[1];
    public DownloadableSoundsCommand Command => (DownloadableSoundsCommand)_body.Span[3];
}

/// <summary>End of File (7E, 7B): the sender has no more data (file or sample dump handshake).</summary>
public readonly record struct EndOfFileMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x7B;
    private const int HeaderLength = 3;
    private readonly ReadOnlyMemory<byte> _body;

    public EndOfFileMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, 1, 1, nameof(EndOfFileMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, 1, 1, out _, out _);

    public byte DeviceId => _body.Span[1];
    public byte PacketNumber => _body.Span[HeaderLength];
}

/// <summary>Wait (7E, 7C): the receiver is busy and the sender should wait.</summary>
public readonly record struct WaitMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x7C;
    private const int HeaderLength = 3;
    private readonly ReadOnlyMemory<byte> _body;

    public WaitMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, 1, 1, nameof(WaitMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, 1, 1, out _, out _);

    public byte DeviceId => _body.Span[1];
    public byte PacketNumber => _body.Span[HeaderLength];
}

/// <summary>Cancel (7E, 7D): aborts the current transfer.</summary>
public readonly record struct CancelMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x7D;
    private const int HeaderLength = 3;
    private readonly ReadOnlyMemory<byte> _body;

    public CancelMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, 1, 1, nameof(CancelMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, 1, 1, out _, out _);

    public byte DeviceId => _body.Span[1];
    public byte PacketNumber => _body.Span[HeaderLength];
}

/// <summary>NAK (7E, 7E): the packet was not received correctly.</summary>
public readonly record struct NakMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x7E;
    private const int HeaderLength = 3;
    private readonly ReadOnlyMemory<byte> _body;

    public NakMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, 1, 1, nameof(NakMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, 1, 1, out _, out _);

    public byte DeviceId => _body.Span[1];
    public byte PacketNumber => _body.Span[HeaderLength];
}

/// <summary>ACK (7E, 7F): the packet was received correctly.</summary>
public readonly record struct AckMessage
{
    internal const bool RealTime = false;
    internal const byte SubId1 = 0x7F;
    private const int HeaderLength = 3;
    private readonly ReadOnlyMemory<byte> _body;

    public AckMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SysEx.None, 1, 1, nameof(AckMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SysEx.None, 1, 1, out _, out _);

    public byte DeviceId => _body.Span[1];
    public byte PacketNumber => _body.Span[HeaderLength];
}

/// <summary>Full Time Code (7F, 01 01): a complete MIDI time code position, for example when locating.</summary>
public readonly record struct FullTimeCodeMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x01;
    internal const int SubId2 = 0x01;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public FullTimeCodeMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 4, 4, nameof(FullTimeCodeMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 4, 4, out _, out _);

    private ReadOnlySpan<byte> Payload => _body.Span[HeaderLength..];

    public byte DeviceId => _body.Span[1];
    public SmpteFrameRate FrameRate => (SmpteFrameRate)((Payload[0] >> 5) & 0x03);
    public byte Hours => (byte)(Payload[0] & 0x1F);
    public byte Minutes => Payload[1];
    public byte Seconds => Payload[2];
    public byte Frames => Payload[3];
}

/// <summary>Time Code User Bits (7F, 01 02): the SMPTE user bits.</summary>
public readonly record struct TimeCodeUserBitsMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x01;
    internal const int SubId2 = 0x02;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public TimeCodeUserBitsMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 9, 9, nameof(TimeCodeUserBitsMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 9, 9, out _, out _);

    public byte DeviceId => _body.Span[1];

    /// <summary>The 9 user bits bytes (u1-u9) as sent.</summary>
    public ReadOnlySpan<byte> UserBits => _body.Span[HeaderLength..];
}

/// <summary>Bar Number (7F, 03 01): the current bar number.</summary>
public readonly record struct BarNumberMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x03;
    internal const int SubId2 = 0x01;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public BarNumberMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 2, 2, nameof(BarNumberMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 2, 2, out _, out _);

    public byte DeviceId => _body.Span[1];

    /// <summary>The raw 14-bit value (LSB first). See the specification for the special values.</summary>
    public int Value => SysEx.Read14(_body.Span[HeaderLength..], 0);
}

/// <summary>Time Signature (7F, 03 02 immediate / 03 42 delayed): a time signature change in the notation.</summary>
public readonly record struct TimeSignatureNotationMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x03;
    internal const int ImmediateSubId2 = 0x02;
    internal const int DelayedSubId2 = 0x42;
    private const int HeaderLength = 4;
    private const int MinDataLength = 5;
    private readonly ReadOnlyMemory<byte> _body;

    public TimeSignatureNotationMessage(ReadOnlyMemory<byte> data)
    {
        if (TryValidate(data, ImmediateSubId2, out var body, out var error)
            || TryValidate(data, DelayedSubId2, out body, out error))
        {
            _body = body;
            return;
        }
        throw new ArgumentException($"{nameof(TimeSignatureNotationMessage)} {error}.", nameof(data));
    }

    private static bool TryValidate(ReadOnlyMemory<byte> data, int subId2, out ReadOnlyMemory<byte> body, out string? error)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, subId2, MinDataLength, int.MaxValue, out body, out error);

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => TryValidate(data, ImmediateSubId2, out _, out _) || TryValidate(data, DelayedSubId2, out _, out _);

    private ReadOnlySpan<byte> Payload => _body.Span[HeaderLength..];

    public byte DeviceId => _body.Span[1];

    /// <summary>True when the change applies at the next bar (0x42), false when it applies immediately (0x02).</summary>
    public bool IsDelayed => _body.Span[3] == DelayedSubId2;

    public byte Numerator => Payload[1];

    /// <summary>The denominator as a negative power of two (2 = quarter note).</summary>
    public byte DenominatorPower => Payload[2];

    public int Denominator => 1 << DenominatorPower;
    public byte ClocksPerClick => Payload[3];
    public byte ThirtySecondNotesPerQuarterNote => Payload[4];
}

/// <summary>Master Volume (7F, 04 01): the volume of the whole device (14-bit).</summary>
public readonly record struct MasterVolumeMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x04;
    internal const int SubId2 = 0x01;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public MasterVolumeMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 2, 2, nameof(MasterVolumeMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 2, 2, out _, out _);

    public byte DeviceId => _body.Span[1];

    /// <summary>The 14-bit volume, 0 to 16383.</summary>
    public int Value => SysEx.Read14(_body.Span[HeaderLength..], 0);
}

/// <summary>Master Balance (7F, 04 02): the left/right balance of the whole device (14-bit).</summary>
public readonly record struct MasterBalanceMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x04;
    internal const int SubId2 = 0x02;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public MasterBalanceMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 2, 2, nameof(MasterBalanceMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 2, 2, out _, out _);

    public byte DeviceId => _body.Span[1];

    /// <summary>The 14-bit balance: 0 is hard left, 8192 is center and 16383 is hard right.</summary>
    public int Value => SysEx.Read14(_body.Span[HeaderLength..], 0);
}

/// <summary>Master Fine Tuning (7F, 04 03): the tuning of the whole device in cents (14-bit).</summary>
public readonly record struct MasterFineTuningMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x04;
    internal const int SubId2 = 0x03;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public MasterFineTuningMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 2, 2, nameof(MasterFineTuningMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 2, 2, out _, out _);

    public byte DeviceId => _body.Span[1];

    /// <summary>The raw 14-bit value; 8192 is A440.</summary>
    public int Value => SysEx.Read14(_body.Span[HeaderLength..], 0);

    /// <summary>The tuning offset from A440 in cents (-100 up to nearly +100).</summary>
    public double Cents => (Value - 8192) * 100.0 / 8192;
}

/// <summary>Master Coarse Tuning (7F, 04 04): the tuning of the whole device in semitones.</summary>
public readonly record struct MasterCoarseTuningMessage
{
    internal const bool RealTime = true;
    internal const byte SubId1 = 0x04;
    internal const int SubId2 = 0x04;
    private const int HeaderLength = 4;
    private readonly ReadOnlyMemory<byte> _body;

    public MasterCoarseTuningMessage(ReadOnlyMemory<byte> data)
        => _body = UniversalSysExMessage.Validate(data, RealTime, SubId1, SubId2, 2, 2, nameof(MasterCoarseTuningMessage));

    internal static bool Matches(ReadOnlyMemory<byte> data)
        => UniversalSysExMessage.TryValidate(data, RealTime, SubId1, SubId2, 2, 2, out _, out _);

    public byte DeviceId => _body.Span[1];

    /// <summary>The raw value (only the MSB is used); 0x40 is A440.</summary>
    public byte Value => _body.Span[HeaderLength + 1];

    /// <summary>The tuning offset from A440 in semitones (-64 to +63).</summary>
    public int Semitones => Value - 0x40;
}
