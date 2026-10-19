using Jacobi.Midi.Core;
using Xunit;

namespace Jacobi.Midi.Tests.Core;

/// <summary>
/// Verifies that <see cref="UniversalPacket"/> and its fixed-size variants keep every word in place.
/// </summary>
public class UniversalPacketTests
{
    private static readonly uint[] Words = [0x3123_4567, 0x89AB_CDEF, 0xFEDC_BA98, 0x7654_3210];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TryGetValue_PreservesWords(int count)
    {
        var data = Words[..count];
        var packet = new UniversalPacket(data);

        IUniversalPacket typed = count switch
        {
            1 => packet.TryGetValue(out UniversalPacket32? p32) ? p32.Value : throw new InvalidOperationException(),
            2 => packet.TryGetValue(out UniversalPacket64? p64) ? p64.Value : throw new InvalidOperationException(),
            3 => packet.TryGetValue(out UniversalPacket96? p96) ? p96.Value : throw new InvalidOperationException(),
            _ => packet.TryGetValue(out UniversalPacket128? p128) ? p128.Value : throw new InvalidOperationException(),
        };

        Assert.Equal((PacketKind)count, typed.PacketKind);
        var copy = new uint[count];
        typed.CopyTo(copy);
        Assert.Equal(data, copy);
        Assert.Equal(MessageKind.DataMessage64, typed.MessageKind);
    }

    [Fact]
    public void Packet96_MasksUpperWord()
    {
        var value = (UInt128)0xFFFF_FFFF << 96 | (UInt128)0x0000_0003 << 64 | (UInt128)0x0000_0002 << 32 | 0x0000_0001;
        var packet = new UniversalPacket96(value);

        Assert.Equal(0x0000_0001u, packet[0]);
        Assert.Equal(0x0000_0002u, packet[1]);
        Assert.Equal(0x0000_0003u, packet[2]);
        Assert.Equal(new UniversalPacket96((UInt128)3 << 64 | (UInt128)2 << 32 | 1), packet);
    }
}
