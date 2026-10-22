using Jacobi.Midi.Device.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

public class AddressSpaceTests
{
    private static readonly AddressSpace Gp8 = new(2);
    private static readonly AddressSpace Roland4 = new(4);

    [Theory]
    [InlineData("0x40-00", 0x2000)]
    [InlineData("0x40_00", 0x2000)]
    [InlineData("40-00H", 0x2000)]
    [InlineData("4000h", 0x2000)]
    [InlineData("0x4000", 0x2000)]
    [InlineData("0x00-40", 0x40)]
    [InlineData("0x40", 0x40)]
    [InlineData("50", 50)]
    [InlineData("256", 0x80)]   // decimal 256 = 0x01-00 in wire form
    [InlineData("0x00-00", 0)]
    public void Parse_WireForm(string text, long expected)
    {
        Assert.Equal(expected, Gp8.Parse(text));
    }

    [Fact]
    public void Parse_FourBytes()
    {
        Assert.Equal((3L << 21) | (0x10 << 7), Roland4.Parse("0x03-00-10-00"));
    }

    [Theory]
    [InlineData("0x80")]          // a byte must fit in 7 bits
    [InlineData("0x40-80")]
    [InlineData("12AB")]          // hex without prefix/postfix
    [InlineData("")]
    [InlineData("0x")]
    public void Parse_Invalid(string text)
    {
        Assert.Throws<FormatException>(() => Gp8.Parse(text));
    }

    [Fact]
    public void Parse_OutsideTheAddressSpace()
    {
        Assert.Throws<FormatException>(() => Gp8.Parse("0x01-00-00"));
    }

    [Fact]
    public void Format_WireForm()
    {
        Assert.Equal("0x40-00", Gp8.Format(0x2000));
        Assert.Equal("0x03-00-10-00", Roland4.Format(Roland4.Parse("0x03-00-10-00")));
    }

    [Fact]
    public void Wire_RoundTrip()
    {
        Span<byte> wire = stackalloc byte[2];
        Gp8.ToWire(0x2145, wire);
        Assert.Equal([0x42, 0x45], wire.ToArray());
        Assert.Equal(0x2145, Gp8.ToLinear(wire));
    }

    [Fact]
    public void Capacity()
    {
        Assert.Equal(0x4000, Gp8.Capacity);
        Assert.Equal(1L << 28, Roland4.Capacity);
    }

    [Fact]
    public void Range_Operations()
    {
        var range = new AddressRange(0x10, 0x10);
        Assert.Equal(0x20, range.End);
        Assert.True(range.Contains(0x1F));
        Assert.False(range.Contains(0x20));
        Assert.True(range.Overlaps(new AddressRange(0x1F, 4)));
        Assert.False(range.Overlaps(new AddressRange(0x20, 4)));
        Assert.Equal(new AddressRange(0x18, 8), range.Intersect(new AddressRange(0x18, 0x20)));
        Assert.Equal(new AddressRange(0x08, 0x18), range.Union(new AddressRange(0x08, 4)));
    }
}
