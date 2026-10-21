using Jacobi.Midi.Core;
using Xunit;
using V2 = Jacobi.Midi.Messages.V2;

namespace Jacobi.Midi.Tests.Messages;

public class Midi2SysExMessageTests
{
    [Theory]
    [InlineData(V2.SysExPacketStatus.Complete, 0x32060102U)]
    [InlineData(V2.SysExPacketStatus.Start, 0x32160102U)]
    [InlineData(V2.SysExPacketStatus.Continue, 0x32260102U)]
    [InlineData(V2.SysExPacketStatus.End, 0x32360102U)]
    public void SysEx7MatchesSpecificationWords(V2.SysExPacketStatus status, uint word0)
    {
        var message = new V2.SysEx7Message(2, status, [1, 2, 3, 4, 5, 127]);
        Assert.True(message.HasValue);
        Assert.Equal(word0, message[0]);
        Assert.Equal(0x0304057FU, message[1]);
        Assert.Equal(2, message.Group);
        Assert.Equal(status, message.Status);
        Assert.Equal(6, message.PayloadLength);
        Assert.Equal(message, new V2.SysEx7Message(word0, 0x0304057F));
        Assert.Equal(message, new V2.SysEx7Message(message.Data));
        Assert.Equal(message, new V2.SysEx7Message(message.Packet));
        Assert.Equal(PacketKind.Packet64, message.PacketKind);
        Assert.Equal(MessageKind.DataMessage64, message.MessageKind);
    }

    [Theory]
    [InlineData(V2.SysExPacketStatus.Complete, 0x520EAB80U)]
    [InlineData(V2.SysExPacketStatus.Start, 0x521EAB80U)]
    [InlineData(V2.SysExPacketStatus.Continue, 0x522EAB80U)]
    [InlineData(V2.SysExPacketStatus.End, 0x523EAB80U)]
    public void SysEx8MatchesSpecificationWords(V2.SysExPacketStatus status, uint word0)
    {
        var message = new V2.SysEx8Message(2, status, 0xAB,
            [0x80, 0xFF, 0xF0, 0xF7, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        Assert.True(message.HasValue);
        Assert.Equal(word0, message[0]);
        Assert.Equal(0xFFF0F704U, message[1]);
        Assert.Equal(0x05060708U, message[2]);
        Assert.Equal(0x090A0B0CU, message[3]);
        Assert.Equal(2, message.Group);
        Assert.Equal(status, message.Status);
        Assert.Equal(0xAB, message.StreamId);
        Assert.Equal(14, message.ByteCount);
        Assert.Equal(13, message.PayloadLength);
        Assert.False(message.IsAborted);
        Assert.Equal(message, new V2.SysEx8Message(word0, 0xFFF0F704, 0x05060708, 0x090A0B0C));
        Assert.Equal(message, new V2.SysEx8Message(message.Data));
        Assert.Equal(message, new V2.SysEx8Message(message.Packet));
        Assert.Equal(PacketKind.Packet128, message.PacketKind);
        Assert.Equal(MessageKind.DataMessage128, message.MessageKind);
    }

    [Fact]
    public void AllPayloadLengthsRoundTripWithZeroPadding()
    {
        for (int length = 0; length <= 13; length++)
        {
            byte[] payload = Enumerable.Range(0, length).Select(index => (byte)(index + 1)).ToArray();
            var eightBit = new V2.SysEx8Message(15, V2.SysExPacketStatus.Continue, 255, payload);
            Assert.Equal(length, eightBit.PayloadLength);
            Assert.Equal(length + 1, eightBit.ByteCount);
            byte[] destination = new byte[length + 1];
            destination[^1] = 0xCC;
            eightBit.CopyPayloadTo(destination);
            Assert.Equal(payload, destination[..length]);
            Assert.Equal(0xCC, destination[^1]);
            for (int index = 0; index < length; index++)
                Assert.Equal(payload[index], eightBit.GetPayloadByte(index));
            byte[] padded = ReadWireBytes(eightBit);
            Assert.All(padded[(length + 3)..], value => Assert.Equal(0, value));

            if (length > 6)
                continue;
            var sevenBit = new V2.SysEx7Message(15, V2.SysExPacketStatus.Start, payload);
            Assert.Equal(length, sevenBit.PayloadLength);
            sevenBit.CopyPayloadTo(destination);
            Assert.Equal(payload, destination[..length]);
            Assert.Equal(0xCC, destination[^1]);
            for (int index = 0; index < length; index++)
                Assert.Equal(payload[index], sevenBit.GetPayloadByte(index));
            padded = ReadWireBytes(sevenBit);
            Assert.All(padded[(length + 2)..], value => Assert.Equal(0, value));
        }
    }

    [Fact]
    public void EmptyPacketsHaveDifferentWireCounts()
    {
        var sevenBit = new V2.SysEx7Message(0, V2.SysExPacketStatus.End, []);
        Assert.Equal(0x30300000U, sevenBit[0]);
        Assert.Equal(0, sevenBit.PayloadLength);
        var eightBit = new V2.SysEx8Message(0, V2.SysExPacketStatus.End, 255, []);
        Assert.Equal(0x5031FF00U, eightBit[0]);
        Assert.Equal(1, eightBit.ByteCount);
        Assert.Equal(0, eightBit.PayloadLength);
        Assert.False(eightBit.IsAborted);
        Assert.True(sevenBit.HasValue);
        Assert.True(eightBit.HasValue);
    }

    [Fact]
    public void AbortIsAnEndWithCountFifteenAndNoPayload()
    {
        var message = V2.SysEx8Message.CreateAbort(15, 0xAB);
        Assert.Equal(0x5F3FAB00U, message[0]);
        Assert.Equal(0U, message[1]);
        Assert.Equal(0U, message[2]);
        Assert.Equal(0U, message[3]);
        Assert.True(message.HasValue);
        Assert.True(message.IsAborted);
        Assert.Equal(V2.SysExPacketStatus.End, message.Status);
        Assert.Equal(0xAB, message.StreamId);
        Assert.Equal(15, message.ByteCount);
        Assert.Equal(0, message.PayloadLength);
        message.CopyPayloadTo(Span<byte>.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => message.GetPayloadByte(0));
    }

    [Theory]
    [InlineData(V2.SysExPacketStatus.Complete)]
    [InlineData(V2.SysExPacketStatus.Start)]
    [InlineData(V2.SysExPacketStatus.Continue)]
    [InlineData(V2.SysExPacketStatus.End)]
    public void OnlyCompleteSysEx7PacketsExposeExistingMessageViews(V2.SysExPacketStatus status)
    {
        var packet = new V2.SysEx7Message(0, status, [0x7E, 0x7F, 0x06, 0x01]);
        bool complete = status == V2.SysExPacketStatus.Complete;
        Assert.Equal(complete, packet.TryGetSystemExclusive(out var message));
        if (!complete)
        {
            Assert.Null(message);
            return;
        }

        Assert.NotNull(message);
        Assert.Equal(new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 }, message.Value.Bytes.ToArray());
        Assert.True(message.Value.IsUniversal);
        Assert.True(message.Value.TryGetUniversal(out var universal));
        Assert.True(universal.Value.TryGetValue(out Jacobi.Midi.Messages.IdentityRequestMessage? identity));
        Assert.NotNull(identity);
        Assert.False(new V2.SysEx7Message(0, V2.SysExPacketStatus.Complete, []).TryGetSystemExclusive(out _));
        Assert.False(default(V2.SysEx7Message).TryGetSystemExclusive(out _));
    }

    [Fact]
    public void RawPacketsPreservePaddingButDoNotExposeItAsPayload()
    {
        var sevenBit = new V2.SysEx7Message(0x30017DFF, 0xFFFFFFFF);
        byte[] payload = new byte[1];
        sevenBit.CopyPayloadTo(payload);
        Assert.Equal(new byte[] { 0x7D }, payload);
        Assert.Equal(0xFFFFFFFFU, sevenBit[1]);
        var eightBit = new V2.SysEx8Message(0x500201FF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF);
        eightBit.CopyPayloadTo(payload);
        Assert.Equal(new byte[] { 0xFF }, payload);
        Assert.Equal(0xFFFFFFFFU, eightBit[3]);
    }

    [Fact]
    public void PacketCopiesUseWordOrderAndCheckDestinationLength()
    {
        var sevenBit = new V2.SysEx7Message(0x30060102, 0x03040506);
        var eightBit = new V2.SysEx8Message(0x500EAB01, 0x02030405, 0x06070809, 0x0A0B0C0D);
        uint[] words = [0, 0, 0, 0, 0xDEADBEEF];
        eightBit.CopyTo(words);
        Assert.Equal(new uint[] { 0x500EAB01, 0x02030405, 0x06070809, 0x0A0B0C0D, 0xDEADBEEF }, words);
        sevenBit.CopyTo(words);
        Assert.Equal(new uint[] { 0x30060102, 0x03040506, 0x06070809, 0x0A0B0C0D, 0xDEADBEEF }, words);
        Assert.Throws<ArgumentException>(() => sevenBit.CopyTo(new uint[1]));
        Assert.Throws<ArgumentException>(() => eightBit.CopyTo(new uint[3]));
        Assert.Throws<ArgumentException>(() => sevenBit.CopyPayloadTo(new byte[5]));
        Assert.Throws<ArgumentException>(() => eightBit.CopyPayloadTo(new byte[12]));
        Assert.Throws<IndexOutOfRangeException>(() => sevenBit[-1]);
        Assert.Throws<IndexOutOfRangeException>(() => sevenBit[2]);
        Assert.Throws<IndexOutOfRangeException>(() => eightBit[-1]);
        Assert.Throws<IndexOutOfRangeException>(() => eightBit[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => sevenBit.GetPayloadByte(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sevenBit.GetPayloadByte(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => eightBit.GetPayloadByte(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => eightBit.GetPayloadByte(13));
    }

    [Fact]
    public void InvalidHeadersAndParameterFieldsAreRejected()
    {
        Assert.False(default(V2.SysEx7Message).HasValue);
        Assert.False(default(V2.SysEx8Message).HasValue);
        Assert.Equal(0, default(V2.SysEx8Message).PayloadLength);
        Assert.Throws<ArgumentException>(() => new V2.SysEx7Message(0x40000000, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx7Message(0x30400000, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx7Message(0x30070000, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx7Message(0x30018000, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx8Message(0x30010000, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx8Message(0x50810000, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx8Message(0x50000000, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx8Message(0x500F0000, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx8Message(0x501F0000, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => new V2.SysEx8Message(0x502F0000, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.SysEx7Message(16, V2.SysExPacketStatus.Complete, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.SysEx8Message(16, V2.SysExPacketStatus.Complete, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.SysEx7Message(0, (V2.SysExPacketStatus)4, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.SysEx8Message(0, (V2.SysExPacketStatus)4, 0, []));
        Assert.Throws<ArgumentException>(() => new V2.SysEx7Message(0, V2.SysExPacketStatus.Complete, new byte[7]));
        Assert.Throws<ArgumentException>(() => new V2.SysEx8Message(0, V2.SysExPacketStatus.Complete, 0, new byte[14]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new V2.SysEx7Message(0, V2.SysExPacketStatus.Complete, [0xF0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => V2.SysEx8Message.CreateAbort(16, 0));
    }

    [Theory]
    [InlineData(0, new[] { V2.SysExPacketStatus.Complete })]
    [InlineData(6, new[] { V2.SysExPacketStatus.Complete })]
    [InlineData(7, new[] { V2.SysExPacketStatus.Start, V2.SysExPacketStatus.End })]
    [InlineData(12, new[] { V2.SysExPacketStatus.Start, V2.SysExPacketStatus.End })]
    [InlineData(13, new[] { V2.SysExPacketStatus.Start, V2.SysExPacketStatus.Continue, V2.SysExPacketStatus.End })]
    public void SysEx7FromSystemExclusiveSplitsThePayload(int length, V2.SysExPacketStatus[] statuses)
    {
        byte[] payload = Enumerable.Range(0, length).Select(index => (byte)(index + 1)).ToArray();
        byte[] sysEx = [0xF0, .. payload, 0xF7];

        var packets = V2.SysEx7Message.FromSystemExclusive(3, sysEx);

        Assert.Equal(statuses, packets.Select(packet => packet.Status));
        Assert.All(packets, packet => Assert.Equal(3, packet.Group));
        var transmitted = packets.SelectMany(packet =>
        {
            var bytes = new byte[packet.PayloadLength];
            packet.CopyPayloadTo(bytes);
            return bytes;
        });
        Assert.Equal(payload, transmitted);
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0xF7 })]
    [InlineData(new byte[] { 0xF0, 0x01, 0x02 })]
    [InlineData(new byte[] { 0xF0, 0x80, 0xF7 })]
    [InlineData(new byte[] { 0xF0 })]
    public void SysEx7FromSystemExclusiveRejectsInvalidMessages(byte[] sysEx)
        => Assert.Throws<ArgumentException>(() => V2.SysEx7Message.FromSystemExclusive(0, sysEx));

    private static byte[] ReadWireBytes(IUniversalPacket packet)
    {
        byte[] bytes = new byte[(int)packet.PacketKind * 4];
        for (int index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)(packet[index / 4] >> ((3 - index % 4) * 8));
        return bytes;
    }
}