using Jacobi.Midi.Core;
using Xunit;
using V2 = Jacobi.Midi.Messages.V2;

namespace Jacobi.Midi.Tests.Messages;

public class SysEx7AssemblerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(20)]
    public void ReassemblesTheMessageThatWasSplit(int length)
    {
        byte[] sysEx = [0xF0, .. Enumerable.Range(0, length).Select(index => (byte)(index + 1)), 0xF7];
        var assembler = new V2.SysEx7Assembler(3);
        byte[]? result = null;
        var completed = 0;

        // a 0-byte payload cannot be split; send it as an empty Complete packet
        V2.SysEx7Message[] packets = length == 0
            ? [new V2.SysEx7Message(3, V2.SysExPacketStatus.Complete, [])]
            : V2.SysEx7Message.FromSystemExclusive(3, sysEx);
        foreach (var packet in packets)
        {
            if (assembler.TryAdd(new UniversalPacket([packet[0], packet[1]]), out var message))
            {
                result = message;
                completed++;
            }
        }

        Assert.Equal(1, completed);
        Assert.Equal(sysEx, result);
        Assert.False(assembler.IsPending);
    }

    [Fact]
    public void IgnoresOtherGroupsOtherPacketsAndFragmentsWithoutAStart()
    {
        var assembler = new V2.SysEx7Assembler(0);

        Assert.False(assembler.TryAdd(new V2.SysEx7Message(1, V2.SysExPacketStatus.Complete, [1]), out _));
        Assert.False(assembler.TryAdd(new V2.SysEx7Message(0, V2.SysExPacketStatus.End, [1]), out _));
        Assert.False(assembler.TryAdd(new UniversalPacket([0x20903C40U]), out _));

        Assert.False(assembler.TryAdd(new V2.SysEx7Message(0, V2.SysExPacketStatus.Start, [1, 2, 3, 4, 5, 6]), out _));
        Assert.True(assembler.IsPending);
        // a new Start discards the unfinished message
        Assert.False(assembler.TryAdd(new V2.SysEx7Message(0, V2.SysExPacketStatus.Start, [7]), out _));
        Assert.True(assembler.TryAdd(new V2.SysEx7Message(0, V2.SysExPacketStatus.End, [8]), out var sysEx));
        Assert.Equal([0xF0, 7, 8, 0xF7], sysEx);
    }
}
