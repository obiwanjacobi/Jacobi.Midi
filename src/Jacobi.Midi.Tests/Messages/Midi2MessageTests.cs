using Jacobi.Midi.Core;
using Xunit;
using ControllerNumber = Jacobi.Midi.Messages.ControllerNumber;
using V2 = Jacobi.Midi.Messages.V2;

namespace Jacobi.Midi.Tests.Messages;

public class Midi2MessageTests
{
    [Fact]
    public void ConstructorsMatchSpecificationWords()
    {
        AssertWords(new V2.RegisteredPerNoteControllerMessage(2, 3, 60, 255, 0x89ABCDEF).Data, 0x42033CFF, 0x89ABCDEF);
        AssertWords(new V2.AssignablePerNoteControllerMessage(2, 3, 60, 255, 0x89ABCDEF).Data, 0x42133CFF, 0x89ABCDEF);
        AssertWords(new V2.RegisteredControllerMessage(2, 3, 12, 34, 0x89ABCDEF).Data, 0x42230C22, 0x89ABCDEF);
        AssertWords(new V2.AssignableControllerMessage(2, 3, 12, 34, 0x89ABCDEF).Data, 0x42330C22, 0x89ABCDEF);
        AssertWords(new V2.RelativeRegisteredControllerMessage(2, 3, 12, 34, -2).Data, 0x42430C22, 0xFFFFFFFE);
        AssertWords(new V2.RelativeAssignableControllerMessage(2, 3, 12, 34, Int32.MinValue).Data, 0x42530C22, 0x80000000);
        AssertWords(new V2.PerNotePitchBendMessage(2, 3, 60, 0x80000000).Data, 0x42633C00, 0x80000000);
        AssertWords(new V2.NoteOffMessage(2, 3, 60, 0xABCD, V2.NoteAttributeType.ManufacturerSpecific, 0x1234).Data, 0x42833C01, 0xABCD1234);
        AssertWords(new V2.NoteOnMessage(2, 3, 60, 0xABCD, V2.NoteAttributeType.Pitch, 0x7980).Data, 0x42933C03, 0xABCD7980);
        AssertWords(new V2.PolyphonicKeyPressureMessage(2, 3, 60, 0x89ABCDEF).Data, 0x42A33C00, 0x89ABCDEF);
        AssertWords(new V2.ControlChangeMessage(2, 3, (ControllerNumber)74, 0x89ABCDEF).Data, 0x42B34A00, 0x89ABCDEF);
        AssertWords(new V2.ProgramChangeMessage(2, 3, 42, 0x1234).Data, 0x42C30001, 0x2A002434);
        AssertWords(new V2.ChannelPressureMessage(2, 3, 0x89ABCDEF).Data, 0x42D30000, 0x89ABCDEF);
        AssertWords(new V2.PitchBendChangeMessage(2, 3, 0x80000000).Data, 0x42E30000, 0x80000000);
        AssertWords(new V2.PerNoteManagementMessage(2, 3, 60, V2.PerNoteManagementOptions.Detach | V2.PerNoteManagementOptions.Reset).Data, 0x42F33C03, 0);
    }

    [Fact]
    public void UnionDispatchesEveryDefinedStatus()
    {
        for (int status = 0; status < 16; status++)
        {
            var message = new V2.MidiMessage(0x40000000U | ((uint)status << 20), 0);
            Assert.Equal(status != 7, message.HasValue);
            Assert.Equal(status == 7 ? V2.MidiMessageKind.Unknown : (V2.MidiMessageKind)status, message.Kind);
            Assert.Equal(status == 0, message.TryGetValue(out V2.RegisteredPerNoteControllerMessage? registeredPerNote));
            Assert.Equal(status == 1, message.TryGetValue(out V2.AssignablePerNoteControllerMessage? assignablePerNote));
            Assert.Equal(status == 2, message.TryGetValue(out V2.RegisteredControllerMessage? registered));
            Assert.Equal(status == 3, message.TryGetValue(out V2.AssignableControllerMessage? assignable));
            Assert.Equal(status == 4, message.TryGetValue(out V2.RelativeRegisteredControllerMessage? relativeRegistered));
            Assert.Equal(status == 5, message.TryGetValue(out V2.RelativeAssignableControllerMessage? relativeAssignable));
            Assert.Equal(status == 6, message.TryGetValue(out V2.PerNotePitchBendMessage? perNotePitch));
            Assert.Equal(status == 8, message.TryGetValue(out V2.NoteOffMessage? noteOff));
            Assert.Equal(status == 9, message.TryGetValue(out V2.NoteOnMessage? noteOn));
            Assert.Equal(status == 10, message.TryGetValue(out V2.PolyphonicKeyPressureMessage? polyPressure));
            Assert.Equal(status == 11, message.TryGetValue(out V2.ControlChangeMessage? controlChange));
            Assert.Equal(status == 12, message.TryGetValue(out V2.ProgramChangeMessage? programChange));
            Assert.Equal(status == 13, message.TryGetValue(out V2.ChannelPressureMessage? channelPressure));
            Assert.Equal(status == 14, message.TryGetValue(out V2.PitchBendChangeMessage? pitch));
            Assert.Equal(status == 15, message.TryGetValue(out V2.PerNoteManagementMessage? management));
            Assert.Equal(status == 0 ? message.Data : (ulong?)null, registeredPerNote?.Data);
            Assert.Equal(status == 1 ? message.Data : (ulong?)null, assignablePerNote?.Data);
            Assert.Equal(status == 2 ? message.Data : (ulong?)null, registered?.Data);
            Assert.Equal(status == 3 ? message.Data : (ulong?)null, assignable?.Data);
            Assert.Equal(status == 4 ? message.Data : (ulong?)null, relativeRegistered?.Data);
            Assert.Equal(status == 5 ? message.Data : (ulong?)null, relativeAssignable?.Data);
            Assert.Equal(status == 6 ? message.Data : (ulong?)null, perNotePitch?.Data);
            Assert.Equal(status == 8 ? message.Data : (ulong?)null, noteOff?.Data);
            Assert.Equal(status == 9 ? message.Data : (ulong?)null, noteOn?.Data);
            Assert.Equal(status == 10 ? message.Data : (ulong?)null, polyPressure?.Data);
            Assert.Equal(status == 11 ? message.Data : (ulong?)null, controlChange?.Data);
            Assert.Equal(status == 12 ? message.Data : (ulong?)null, programChange?.Data);
            Assert.Equal(status == 13 ? message.Data : (ulong?)null, channelPressure?.Data);
            Assert.Equal(status == 14 ? message.Data : (ulong?)null, pitch?.Data);
            Assert.Equal(status == 15 ? message.Data : (ulong?)null, management?.Data);
        }
    }

    [Fact]
    public void RawViewsDecodeFieldsWithoutLosingPrecision()
    {
        var note = new V2.NoteOnMessage(new V2.MidiMessage(0x4F9E7F03, 0xFFFF8B80).Data);
        Assert.Equal(15, note.Group);
        Assert.Equal(14, note.Channel);
        Assert.Equal(127, note.Note);
        Assert.Equal(UInt16.MaxValue, note.Velocity);
        Assert.Equal(V2.NoteAttributeType.Pitch, note.AttributeType);
        Assert.Equal(0x8B80, note.AttributeData);

        var registered = new V2.RegisteredControllerMessage(new V2.MidiMessage(0x42230C22, 0x89ABCDEF).Data);
        Assert.Equal(12, registered.Bank);
        Assert.Equal(34, registered.Index);
        Assert.Equal(0x89ABCDEFU, registered.Value);
        var perNote = new V2.AssignablePerNoteControllerMessage(new V2.MidiMessage(0x42133CFF, UInt32.MaxValue).Data);
        Assert.Equal(60, perNote.Note);
        Assert.Equal(255, perNote.Controller);
        Assert.Equal(UInt32.MaxValue, perNote.Value);
        Assert.Equal(-2, new V2.RelativeRegisteredControllerMessage(2, 3, 12, 34, -2).Value);
        Assert.Equal(Int32.MinValue, new V2.RelativeAssignableControllerMessage(2, 3, 12, 34, Int32.MinValue).Value);
    }

    [Fact]
    public void ProgramBankIsOptionalAndPreservesBankZero()
    {
        var withoutBank = new V2.ProgramChangeMessage(2, 3, 42);
        AssertWords(withoutBank.Data, 0x42C30000, 0x2A000000);
        Assert.False(withoutBank.BankValid);
        Assert.Null(withoutBank.Bank);
        var zeroBank = new V2.ProgramChangeMessage(2, 3, 42, 0);
        Assert.True(zeroBank.BankValid);
        Assert.Equal((ushort)0, zeroBank.Bank);
        var bank = new V2.ProgramChangeMessage(2, 3, 42, 0x1234);
        Assert.Equal(42, bank.Program);
        Assert.Equal(0x24, bank.BankMsb);
        Assert.Equal(0x34, bank.BankLsb);
        Assert.Equal((ushort)0x1234, bank.Bank);
    }

    [Fact]
    public void ManagementFlagsAreIndependent()
    {
        var reset = new V2.PerNoteManagementMessage(0, 0, 60, V2.PerNoteManagementOptions.Reset);
        AssertWords(reset.Data, 0x40F03C01, 0);
        Assert.True(reset.Reset);
        Assert.False(reset.Detach);
        var detach = new V2.PerNoteManagementMessage(0, 0, 60, V2.PerNoteManagementOptions.Detach);
        AssertWords(detach.Data, 0x40F03C02, 0);
        Assert.True(detach.Detach);
        Assert.False(detach.Reset);
    }

    [Fact]
    public void DefaultAndOtherMessageTypesDoNotDispatch()
    {
        Assert.False(default(V2.MidiMessage).HasValue);
        Assert.Null(default(V2.MidiMessage).Value);
        var midi1 = new V2.MidiMessage(0x20903C7F, 0);
        Assert.Equal(V2.MidiMessageKind.Unknown, midi1.Kind);
        Assert.False(midi1.TryGetValue(out V2.NoteOnMessage? note));
        Assert.Null(note);
        Assert.Throws<ArgumentException>(() => new V2.NoteOnMessage(midi1.Data));
        Assert.Throws<ArgumentException>(() => new V2.NoteOffMessage(new V2.NoteOnMessage(0, 0, 60, 0).Data));
        var zeroVelocity = new V2.MidiMessage(new V2.NoteOnMessage(0, 0, 60, 0).Data);
        Assert.Equal(V2.MidiMessageKind.NoteOn, zeroVelocity.Kind);
    }

    [Fact]
    public void ParameterConstructorsRejectOutOfRangeFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.NoteOnMessage(16, 0, 60, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.NoteOnMessage(0, 16, 60, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.NoteOnMessage(0, 0, 128, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.NoteOnMessage(0, 0, 60, 1, (V2.NoteAttributeType)4));
        Assert.Throws<ArgumentException>(() => new V2.NoteOnMessage(0, 0, 60, 1, V2.NoteAttributeType.None, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.RegisteredControllerMessage(0, 0, 128, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.AssignableControllerMessage(0, 0, 0, 128, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.ControlChangeMessage(0, 0, (ControllerNumber)128, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.ProgramChangeMessage(0, 0, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.ProgramChangeMessage(0, 0, 0, 16384));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.PerNoteManagementMessage(0, 0, 60, (V2.PerNoteManagementOptions)4));
    }

    [Fact]
    public void UnionInteroperatesWithUniversalPacket64()
    {
        var message = new V2.MidiMessage(new V2.NoteOnMessage(2, 3, 60, 0xABCD).Data);
        Assert.Equal(message, new V2.MidiMessage(message.Packet));
        Assert.Equal(PacketKind.Packet64, message.PacketKind);
        Assert.Equal(MessageKind.Midi2ChannelVoice64, message.MessageKind);
        uint[] words = [0, 0, 0xDEADBEEF];
        message.CopyTo(words);
        Assert.Equal(new uint[] { 0x42933C00, 0xABCD0000, 0xDEADBEEF }, words);
        Assert.Throws<ArgumentException>(() => message.CopyTo(new uint[1]));
        Assert.Throws<IndexOutOfRangeException>(() => message[-1]);
        Assert.Throws<IndexOutOfRangeException>(() => message[2]);
    }

    private static void AssertWords(ulong data, uint word0, uint word1)
    {
        var message = new V2.MidiMessage(data);
        Assert.Equal(word0, message[0]);
        Assert.Equal(word1, message[1]);
        Assert.Equal((byte)((word0 >> 24) & 15), message.Group);
        Assert.Equal((byte)((word0 >> 16) & 15), message.Channel);
    }
}