using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Processors;

public class MidiPack7ProcessorTests
{
    private const string Root = "Pack7.Message";

    // 9 data bytes: a group of 7 (8 packed) and a group of 2 (3 packed)
    private static readonly byte[] Data = [0x81, 0x02, 0x83, 0x04, 0x05, 0x06, 0xFF, 0x80, 0x09];

    [Theory]
    // little: the high bit of the first byte of a group in bit 0 of the high-bit byte
    [InlineData("little", new byte[] { 0x45, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x7F, 0x01, 0x00, 0x09 })]
    // big: in bit 6
    [InlineData("big", new byte[] { 0x51, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x7F, 0x40, 0x00, 0x09 })]
    public void Write_PacksEverySevenBytesInEight_AndReadUnpacksThem(string bitOrder, byte[] packed)
    {
        var serializer = CreateSerializer(packed.Length, bitOrder);
        var buffer = new ArrayBufferWriter<byte>();
        var source = new ValueSource();
        source.Values["Message.Data"] = Data;

        serializer.Serialize(Root, source, buffer);
        var sink = new ValueSink();
        serializer.Deserialize(Root, new ReadOnlySequence<byte>(packed), sink);

        Assert.Equal(packed, buffer.WrittenSpan.ToArray());
        Assert.Equal(Data, sink.Values["Message.Data"]);
    }

    [Fact]
    public void Write_DataThatDoesNotFitTheByteLength_Fails()
    {
        var source = new ValueSource();
        source.Values["Message.Data"] = Data;

        Assert.Throws<SerializationException>(() =>
            CreateSerializer(10, "little").Serialize(Root, source, new ArrayBufferWriter<byte>()));
    }

    [Theory]
    // a packed byte with bit 7 set
    [InlineData(new byte[] { 0x00, 0x81 })]
    // a high-bit byte without data
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x00 })]
    // a high bit for a byte the group does not have (2 bytes: bits 0 and 1)
    [InlineData(new byte[] { 0x04, 0x01, 0x02 })]
    public void Read_InvalidPackedData_Fails(byte[] packed)
    {
        Assert.Throws<SerializationException>(() =>
            CreateSerializer(packed.Length, "little").Deserialize(Root, new ReadOnlySequence<byte>(packed), new ValueSink()));
    }

    private static Serializer CreateSerializer(int byteLength, string bitOrder)
    {
        var xml = $"""
            <schema name="Pack7">
              <members>
                <group name="Message">
                  <members>
                    <field name="Data" datatype="sys.bytes" byteLength="{byteLength}">
                      <processor name="midi.pack7" bitorder="{bitOrder}" />
                    </field>
                  </members>
                </group>
              </members>
            </schema>
            """;
        return new SerializerBuilder()
            .AddSchemas(CreateSchemas(Jacobi.BinarySerializer.Schema.Xml.XmlSerializer.Deserialize(xml)))
            .AddProcessors(CreateProcessors())
            .Build();
    }
}
