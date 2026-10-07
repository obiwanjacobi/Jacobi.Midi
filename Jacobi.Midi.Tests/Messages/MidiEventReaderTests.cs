using System.Buffers;
using Jacobi.Midi.Messages;
using Xunit;

namespace Jacobi.Midi.Tests.Messages;

public class MidiEventReaderTests
{
    private static List<MidiEvent> ReadAll(MidiEventReader reader, byte[] bytes)
    {
        var buffer = new ReadOnlyMemory<byte>(bytes);
        var events = new List<MidiEvent>();
        while (reader.TryRead(ref buffer, out var e))
            events.Add(e);
        return events;
    }

    private static MidiMessage Message(MidiEvent e)
    {
        Assert.Equal(MidiEventKind.Message, e.Kind);
        Assert.True(e.TryGetValue(out MidiMessage? message));
        return message!.Value;
    }

    // --- channel voice ---

    [Fact]
    public void NoteOn_IsRead()
    {
        var events = ReadAll(new MidiEventReader(), [0x93, 60, 100]);

        var e = Assert.Single(events);
        var message = Message(e);
        Assert.Equal(MidiMessageKind.NoteOn, message.Kind);
        Assert.True(message.TryGetValue(out NoteOnMessage? noteOn));
        Assert.Equal(3, noteOn!.Value.Channel);
        Assert.Equal(60, noteOn.Value.Note);
        Assert.Equal(100, noteOn.Value.Velocity);
    }

    [Fact]
    public void NoteOff_IsRead()
    {
        var message = Message(Assert.Single(ReadAll(new MidiEventReader(), [0x80, 64, 0])));

        Assert.True(message.TryGetValue(out NoteOffMessage? noteOff));
        Assert.Equal(0, noteOff!.Value.Channel);
        Assert.Equal(64, noteOff.Value.Note);
    }

    [Fact]
    public void ProgramChange_HasTwoBytes()
    {
        var events = ReadAll(new MidiEventReader(), [0xC5, 12, 0xD5, 33]);

        Assert.Equal(2, events.Count);
        Assert.True(Message(events[0]).TryGetValue(out ProgramChangeMessage? program));
        Assert.Equal(5, program!.Value.Channel);
        Assert.Equal(12, program.Value.Program);
        Assert.True(Message(events[1]).TryGetValue(out ChannelPressureMessage? pressure));
        Assert.Equal(33, pressure!.Value.Pressure);
    }

    [Fact]
    public void PitchBend_Combines14Bits()
    {
        var message = Message(Assert.Single(ReadAll(new MidiEventReader(), [0xE0, 0x7F, 0x7F])));

        Assert.True(message.TryGetValue(out PitchBendChangeMessage? bend));
        Assert.Equal(16383, bend!.Value.Value);
    }

    [Fact]
    public void ControlChange_IsReadAsControllerMessage()
    {
        var message = Message(Assert.Single(ReadAll(new MidiEventReader(), [0xB1, 10, 64])));

        Assert.True(message.TryGetValue(out ControlChangeMessage? cc));
        Assert.Equal(ControllerNumber.Pan, cc!.Value.ControllerNumber);
        Assert.True(message.TryGetValue(out ControllerMessage? _));
    }

    [Fact]
    public void ControlChange_ChannelMode_IsReadAsChannelMode()
    {
        var message = Message(Assert.Single(ReadAll(new MidiEventReader(), [0xB0, 123, 0])));

        Assert.True(message.TryGetValue(out ChannelModeMessage? mode));
        Assert.True(mode!.Value.TryGetValue(out AllNotesOffMessage? _));
        Assert.False(message.TryGetValue(out ControllerMessage? _));
    }

    // --- system common / real-time ---

    [Fact]
    public void SongPositionPointer_IsRead()
    {
        var message = Message(Assert.Single(ReadAll(new MidiEventReader(), [0xF2, 0x01, 0x02])));

        Assert.Equal(MidiMessageKind.SongPositionPointer, message.Kind);
        Assert.True(message.TryGetValue(out SongPositionPointerMessage? spp));
        Assert.Equal((2 << 7) | 1, spp!.Value.Position);
    }

    [Fact]
    public void RealTimeMessages_AreOneByte()
    {
        var events = ReadAll(new MidiEventReader(), [0xF8, 0xFA, 0xFC]);

        Assert.Equal(
            [MidiMessageKind.TimingClock, MidiMessageKind.Start, MidiMessageKind.Stop],
            events.Select(e => Message(e).Kind));
    }

    [Fact]
    public void SystemReset_IsReadByDefault()
    {
        var message = Message(Assert.Single(ReadAll(new MidiEventReader(), [0xFF])));

        Assert.Equal(MidiMessageKind.SystemReset, message.Kind);
    }

    // --- sysex ---

    [Fact]
    public void SysEx_WithTerminator_IsRead()
    {
        var events = ReadAll(new MidiEventReader(), [0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7]);

        var e = Assert.Single(events);
        Assert.Equal(MidiEventKind.SystemExclusive, e.Kind);
        Assert.True(e.TryGetValue(out SystemExclusiveMessage? sysEx));
        Assert.Equal(new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 }, sysEx!.Value.Bytes.ToArray());
    }

    [Fact]
    public void SysEx_EndedByNextStatus_WithoutTerminator()
    {
        var events = ReadAll(new MidiEventReader(), [0xF0, 0x41, 0x10, 0x90, 60, 100]);

        Assert.Equal(2, events.Count);
        Assert.Equal(MidiEventKind.SystemExclusive, events[0].Kind);
        Assert.True(events[0].TryGetValue(out SystemExclusiveMessage? sysEx));
        Assert.Equal(new byte[] { 0xF0, 0x41, 0x10 }, sysEx!.Value.Bytes.ToArray());
        Assert.Equal(MidiMessageKind.NoteOn, Message(events[1]).Kind);
    }

    [Fact]
    public void SysEx_IsCopied_NotBoundToSourceBuffer()
    {
        var bytes = new byte[] { 0xF0, 0x41, 0x10, 0xF7 };
        var e = Assert.Single(ReadAll(new MidiEventReader(), bytes));

        Array.Clear(bytes);

        Assert.True(e.TryGetValue(out SystemExclusiveMessage? sysEx));
        Assert.Equal(0xF0, sysEx!.Value.Bytes.Span[0]);
    }

    [Fact]
    public void SysEx_Empty_IsSkipped()
    {
        var events = ReadAll(new MidiEventReader(), [0xF0, 0xF7, 0x90, 60, 100]);

        Assert.Equal(MidiMessageKind.NoteOn, Message(Assert.Single(events)).Kind);
    }

    // --- meta ---

    [Fact]
    public void Meta_IsRead_WhenEnabled()
    {
        var reader = new MidiEventReader { ReadMetaEvents = true };

        var events = ReadAll(reader, [0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20, 0xFF, 0x2F, 0x00]);

        Assert.Equal(2, events.Count);
        Assert.Equal(MidiEventKind.Meta, events[0].Kind);
        Assert.True(events[0].TryGetValue(out MidiMetaMessage? meta));
        Assert.True(meta!.Value.TryGetValue(out SetTempoMessage? tempo));
        Assert.NotNull(tempo);
        Assert.True(events[1].TryGetValue(out MidiMetaMessage? end));
        Assert.True(end!.Value.TryGetValue(out EndOfTrackMessage? _));
    }

    [Fact]
    public void Meta_WithVariableLengthSize_IsRead()
    {
        var reader = new MidiEventReader { ReadMetaEvents = true };
        var text = new byte[130];
        Array.Fill(text, (byte)'a');
        byte[] bytes = [0xFF, 0x01, 0x81, 0x02, .. text];

        var e = Assert.Single(ReadAll(reader, bytes));

        Assert.True(e.TryGetValue(out MidiMetaMessage? meta));
        Assert.Equal(bytes.Length, meta!.Value.Bytes.Length);
    }

    [Fact]
    public void Meta_Incomplete_ReturnsFalseWithoutAdvancing()
    {
        var reader = new MidiEventReader { ReadMetaEvents = true };
        var buffer = new ReadOnlyMemory<byte>([0xFF, 0x51, 0x03, 0x07]);

        Assert.False(reader.TryRead(ref buffer, out _));
        Assert.Equal(4, buffer.Length);
    }

    // --- running status ---

    [Fact]
    public void RunningStatus_RepeatsChannelStatus()
    {
        var events = ReadAll(new MidiEventReader(), [0x90, 60, 100, 62, 90, 64, 80]);

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(MidiMessageKind.NoteOn, Message(e).Kind));
        Assert.True(Message(events[2]).TryGetValue(out NoteOnMessage? last));
        Assert.Equal(64, last!.Value.Note);
        Assert.Equal(80, last.Value.Velocity);
    }

    [Fact]
    public void RunningStatus_SurvivesRealTimeMessage()
    {
        var events = ReadAll(new MidiEventReader(), [0x90, 60, 100, 0xF8, 62, 90]);

        Assert.Equal(
            [MidiMessageKind.NoteOn, MidiMessageKind.TimingClock, MidiMessageKind.NoteOn],
            events.Select(e => Message(e).Kind));
    }

    [Fact]
    public void RunningStatus_IsCancelledBySystemCommon()
    {
        // after 0xF6 the data bytes 62 90 have no status and are skipped
        var events = ReadAll(new MidiEventReader(), [0x90, 60, 100, 0xF6, 62, 90]);

        Assert.Equal(
            [MidiMessageKind.NoteOn, MidiMessageKind.TuneRequest],
            events.Select(e => Message(e).Kind));
    }

    [Fact]
    public void RunningStatus_IsKeptAcrossCalls_AndClearedByReset()
    {
        var reader = new MidiEventReader();
        var first = new ReadOnlyMemory<byte>([0x90, 60, 100]);
        Assert.True(reader.TryRead(ref first, out _));

        var second = new ReadOnlyMemory<byte>([62, 90]);
        Assert.True(reader.TryRead(ref second, out var e));
        Assert.Equal(MidiMessageKind.NoteOn, Message(e).Kind);

        reader.Reset();
        var third = new ReadOnlyMemory<byte>([62, 90]);
        Assert.False(reader.TryRead(ref third, out _));
    }

    // --- edge cases ---

    [Fact]
    public void EmptyBuffer_ReturnsFalse()
    {
        var buffer = ReadOnlyMemory<byte>.Empty;

        Assert.False(new MidiEventReader().TryRead(ref buffer, out _));
    }

    [Fact]
    public void StrayDataBytes_AreSkipped()
    {
        var events = ReadAll(new MidiEventReader(), [0x10, 0x20, 0x90, 60, 100]);

        Assert.Equal(MidiMessageKind.NoteOn, Message(Assert.Single(events)).Kind);
    }

    [Fact]
    public void UndefinedStatus_AndStraySysExEnd_AreSkipped()
    {
        var events = ReadAll(new MidiEventReader(), [0xF4, 0xF5, 0xF7, 0xF9, 0xFD, 0x90, 60, 100]);

        Assert.Equal(MidiMessageKind.NoteOn, Message(Assert.Single(events)).Kind);
    }

    [Fact]
    public void IncompleteMessage_ReturnsFalse_AndDoesNotAdvance()
    {
        var reader = new MidiEventReader();
        var buffer = new ReadOnlyMemory<byte>([0x90, 60]);

        Assert.False(reader.TryRead(ref buffer, out _));
        Assert.Equal(2, buffer.Length);

        // the rest arrives
        buffer = new ReadOnlyMemory<byte>([0x90, 60, 100]);
        Assert.True(reader.TryRead(ref buffer, out var e));
        Assert.Equal(MidiMessageKind.NoteOn, Message(e).Kind);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void IncompleteSysEx_ReturnsFalse_AndDoesNotAdvance()
    {
        var buffer = new ReadOnlyMemory<byte>([0xF0, 0x41, 0x10]);

        Assert.False(new MidiEventReader().TryRead(ref buffer, out _));
        Assert.Equal(3, buffer.Length);
    }

    [Fact]
    public void MultiSegmentSequence_IsRead()
    {
        var first = new Segment([0x90, 60]);
        var last = first.Append([100, 0xF0, 0x41]).Append([0x10, 0xF7]);
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        var reader = new MidiEventReader();

        Assert.True(reader.TryRead(ref sequence, out var noteOn));
        Assert.Equal(MidiMessageKind.NoteOn, Message(noteOn).Kind);

        Assert.True(reader.TryRead(ref sequence, out var sysEx));
        Assert.True(sysEx.TryGetValue(out SystemExclusiveMessage? message));
        Assert.Equal(new byte[] { 0xF0, 0x41, 0x10, 0xF7 }, message!.Value.Bytes.ToArray());
        Assert.True(sequence.IsEmpty);
    }

    [Fact]
    public void RealTimeInsideMessage_DropsTheInterruptedMessage()
    {
        // known limitation: the interleaved real-time byte is not emitted separately yet
        var events = ReadAll(new MidiEventReader(), [0x90, 60, 0xF8, 100]);

        Assert.DoesNotContain(events, e => e.Kind == MidiEventKind.Message
            && Message(e).Kind == MidiMessageKind.NoteOn);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(byte[] bytes) => Memory = bytes;

        public Segment Append(byte[] bytes)
        {
            var next = new Segment(bytes) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
