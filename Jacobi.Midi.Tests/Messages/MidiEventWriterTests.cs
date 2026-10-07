using System.Buffers;
using Jacobi.Midi.Messages;
using Xunit;

namespace Jacobi.Midi.Tests.Messages;

public class MidiEventWriterTests
{
    private static byte[] Write(MidiEventWriter writer, params MidiEvent[] events)
    {
        var output = new ArrayBufferWriter<byte>();
        foreach (var e in events)
            writer.Write(e, output);
        return output.WrittenSpan.ToArray();
    }

    // --- channel voice ---

    [Fact]
    public void NoteOn_IsWritten()
    {
        var bytes = Write(new MidiEventWriter(), Event(MidiMessageKind.NoteOn, 3, 60, 100));

        Assert.Equal(new byte[] { 0x93, 60, 100 }, bytes);
    }

    [Fact]
    public void ProgramChange_WritesTwoBytes()
    {
        var bytes = Write(new MidiEventWriter(), Event(MidiMessageKind.ProgramChange, 5, 12));

        Assert.Equal(new byte[] { 0xC5, 12 }, bytes);
    }

    [Fact]
    public void PitchBend_WritesLsbThenMsb()
    {
        var bytes = Write(new MidiEventWriter(), Event(MidiMessageKind.PitchBendChange, 0, 0x7F, 0x40));

        Assert.Equal(new byte[] { 0xE0, 0x7F, 0x40 }, bytes);
    }

    // --- system ---

    [Fact]
    public void RealTime_WritesOneByte()
    {
        var bytes = Write(new MidiEventWriter(), new MidiMessage(0xF8), new MidiMessage(0xFA));

        Assert.Equal(new byte[] { 0xF8, 0xFA }, bytes);
    }

    [Fact]
    public void SongPositionPointer_WritesThreeBytes()
    {
        var bytes = Write(new MidiEventWriter(), new MidiMessage(0xF2u | (0x01u << 8) | (0x02u << 16)));

        Assert.Equal(new byte[] { 0xF2, 0x01, 0x02 }, bytes);
    }

    // --- sysex ---

    [Fact]
    public void SysEx_WithTerminator_IsWrittenAsIs()
    {
        byte[] sysEx = [0xF0, 0x41, 0x10, 0xF7];

        var bytes = Write(new MidiEventWriter(), new SystemExclusiveMessage(sysEx));

        Assert.Equal(sysEx, bytes);
    }

    [Fact]
    public void SysEx_WithoutTerminator_GetsOne()
    {
        var bytes = Write(new MidiEventWriter(), new SystemExclusiveMessage(new byte[] { 0xF0, 0x41, 0x10 }));

        Assert.Equal(new byte[] { 0xF0, 0x41, 0x10, 0xF7 }, bytes);
    }

    // --- meta ---

    [Fact]
    public void Meta_IsWrittenAsIs()
    {
        byte[] meta = [0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20];

        var bytes = Write(new MidiEventWriter(), new MidiMetaMessage(meta));

        Assert.Equal(meta, bytes);
    }

    // --- running status ---

    [Fact]
    public void RunningStatus_Off_WritesEveryStatus()
    {
        var bytes = Write(new MidiEventWriter(),
            Event(MidiMessageKind.NoteOn, 0, 60, 100), Event(MidiMessageKind.NoteOn, 0, 62, 90));

        Assert.Equal(new byte[] { 0x90, 60, 100, 0x90, 62, 90 }, bytes);
    }

    [Fact]
    public void RunningStatus_On_OmitsRepeatedStatus()
    {
        var bytes = Write(new MidiEventWriter { UseRunningStatus = true },
            Event(MidiMessageKind.NoteOn, 0, 60, 100), Event(MidiMessageKind.NoteOn, 0, 62, 90));

        Assert.Equal(new byte[] { 0x90, 60, 100, 62, 90 }, bytes);
    }

    [Fact]
    public void RunningStatus_WritesStatusWhenChannelOrKindChanges()
    {
        var bytes = Write(new MidiEventWriter { UseRunningStatus = true },
            Event(MidiMessageKind.NoteOn, 0, 60, 100),
            Event(MidiMessageKind.NoteOn, 1, 60, 100),
            Event(MidiMessageKind.NoteOff, 1, 60, 0));

        Assert.Equal(new byte[] { 0x90, 60, 100, 0x91, 60, 100, 0x81, 60, 0 }, bytes);
    }

    [Fact]
    public void RunningStatus_SurvivesRealTime_ButNotSystemCommonOrSysEx()
    {
        var writer = new MidiEventWriter { UseRunningStatus = true };

        var afterRealTime = Write(writer,
            Event(MidiMessageKind.NoteOn, 0, 60, 100), new MidiMessage(0xF8), Event(MidiMessageKind.NoteOn, 0, 62, 90));
        Assert.Equal(new byte[] { 0x90, 60, 100, 0xF8, 62, 90 }, afterRealTime);

        writer.Reset();
        var afterSysEx = Write(writer,
            Event(MidiMessageKind.NoteOn, 0, 60, 100),
            new SystemExclusiveMessage(new byte[] { 0xF0, 0x41, 0xF7 }),
            Event(MidiMessageKind.NoteOn, 0, 62, 90));
        Assert.Equal(new byte[] { 0x90, 60, 100, 0xF0, 0x41, 0xF7, 0x90, 62, 90 }, afterSysEx);
    }

    [Fact]
    public void RunningStatus_IsNeverUsedForSystemMessages()
    {
        var bytes = Write(new MidiEventWriter { UseRunningStatus = true },
            new MidiMessage(0xF8), new MidiMessage(0xF8));

        Assert.Equal(new byte[] { 0xF8, 0xF8 }, bytes);
    }

    // --- GetSize / TryWrite ---

    [Fact]
    public void GetSize_ReflectsRunningStatus()
    {
        var writer = new MidiEventWriter { UseRunningStatus = true };
        var noteOn = Event(MidiMessageKind.NoteOn, 0, 60, 100);

        Assert.Equal(3, writer.GetSize(noteOn));
        Assert.True(writer.TryWrite(noteOn, new byte[3], out _));
        Assert.Equal(2, writer.GetSize(noteOn));
    }

    [Fact]
    public void TryWrite_TooSmallDestination_WritesNothing_AndKeepsRunningStatus()
    {
        var writer = new MidiEventWriter { UseRunningStatus = true };
        var noteOn = Event(MidiMessageKind.NoteOn, 0, 60, 100);

        Assert.False(writer.TryWrite(noteOn, new byte[2], out var written));
        Assert.Equal(0, written);

        // running status was not set by the failed write, so the status byte is still written
        var destination = new byte[3];
        Assert.True(writer.TryWrite(noteOn, destination, out written));
        Assert.Equal(3, written);
        Assert.Equal(new byte[] { 0x90, 60, 100 }, destination);
    }

    [Fact]
    public void DefaultEvent_Throws()
    {
        Assert.Throws<ArgumentException>(() => new MidiEventWriter().GetSize(default));
        Assert.Throws<ArgumentException>(() => Write(new MidiEventWriter(), default(MidiEvent)));
    }

    [Fact]
    public void Write_NullBufferWriter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MidiEventWriter().Write(Event(MidiMessageKind.NoteOn, 0, 60, 100), null!));
    }

    // --- round trip ---

    [Fact]
    public void RoundTrip_WithRunningStatus_ReadsBackTheSameEvents()
    {
        MidiEvent[] events =
        [
            Event(MidiMessageKind.NoteOn, 2, 60, 100),
            Event(MidiMessageKind.NoteOn, 2, 64, 90),
            Event(MidiMessageKind.ControlChange, 2, 7, 127),
            new MidiMessage(0xF8),
            new SystemExclusiveMessage(new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 }),
            Event(MidiMessageKind.ProgramChange, 2, 5),
            Event(MidiMessageKind.PitchBendChange, 2, 0x10, 0x20),
        ];

        var bytes = Write(new MidiEventWriter { UseRunningStatus = true }, events);

        var reader = new MidiEventReader();
        var buffer = new ReadOnlyMemory<byte>(bytes);
        var read = new List<MidiEvent>();
        while (reader.TryRead(ref buffer, out var e))
            read.Add(e);

        Assert.Equal(events.Length, read.Count);
        for (var i = 0; i < events.Length; i++)
        {
            Assert.Equal(events[i].Kind, read[i].Kind);
            if (events[i].TryGetValue(out MidiMessage? expected))
            {
                Assert.True(read[i].TryGetValue(out MidiMessage? actual));
                Assert.Equal(expected, actual);
            }
        }
    }

    // --- helpers ---

    // builds a channel message from kind, channel and up to two data bytes
    private static MidiEvent Event(MidiMessageKind kind, byte channel, byte data1 = 0, byte data2 = 0)
        => new MidiMessage((uint)((byte)kind | channel) | ((uint)data1 << 8) | ((uint)data2 << 16));
}
