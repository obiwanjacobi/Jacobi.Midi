using System.Text.Json.Nodes;
using Jacobi.Midi.Core;
using Xunit;

namespace Jacobi.Midi.Tests.Core;

/// <summary>
/// Tests the platform-neutral MIDI-CI types of Core: the MUID and the Property Exchange reply.
/// </summary>
public class CapabilityInquiryTests
{
    [Fact]
    public void Muid_Has28Bits()
    {
        Assert.Equal("0ABCDEF", new MidiMuid(0x0ABCDEF).ToString());
        Assert.True(MidiMuid.Broadcast.IsBroadcast);
        Assert.False(new MidiMuid(1).IsBroadcast);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiMuid(0x10000000));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(202, true)]
    [InlineData(341, false)]
    [InlineData(404, false)]
    public void PropertyResponse_IsSuccess_For2xx(int status, bool success)
        => Assert.Equal(success, new MidiPropertyResponse(status, new JsonObject { ["status"] = status }, ReadOnlyMemory<byte>.Empty).IsSuccess);
}
