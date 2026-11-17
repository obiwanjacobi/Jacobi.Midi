using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Processors;

public class MidiXorChecksumProcessorTests
{
    private const string Root = "XorChecksum.Message";

    // 01 ^ 02 ^ 03 ^ 7C = 7C, kept to 7 bits
    private static readonly byte[] Data = [0x01, 0x02, 0x03, 0x7C];
    private const byte Checksum = 0x7C;

    [Fact]
    public void Write_AppendsTheXorOfTheGroup()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var source = new ValueSource();
        source.Values["Message.Data"] = Data;

        CreateSerializer(sysex: false).Serialize(Root, source, buffer);

        Assert.Equal([.. Data, Checksum], buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Read_ChecksTheXorOfTheGroup()
    {
        var sink = new ValueSink();

        CreateSerializer(sysex: false).Deserialize(Root, new ReadOnlySequence<byte>([.. Data, Checksum]), sink);

        Assert.Equal(Data, sink.Values["Message.Data"]);
    }

    [Fact]
    public void Read_AWrongChecksum_Fails()
    {
        Assert.Throws<SerializationException>(() =>
            CreateSerializer(sysex: false).Deserialize(Root, new ReadOnlySequence<byte>([.. Data, 0x10]), new ValueSink()));
    }

    [Fact]
    public void InsideSysEx_OnTheSameGroup_CoversTheContentBetweenF0AndTheChecksum()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var source = new ValueSource();
        source.Values["Message.Data"] = Data;
        var serializer = CreateSerializer(sysex: true);

        serializer.Serialize(Root, source, buffer);
        var sink = new ValueSink();
        serializer.Deserialize(Root, new ReadOnlySequence<byte>(buffer.WrittenMemory), sink);

        // midi.sysex is declared first: it wraps the checksum (F0 and F7 are not part of it)
        Assert.Equal([0xF0, .. Data, Checksum, 0xF7], buffer.WrittenSpan.ToArray());
        Assert.Equal(Data, sink.Values["Message.Data"]);
    }

    private static Serializer CreateSerializer(bool sysex)
    {
        var schema = new SchemaDocumentBuilder("XorChecksum")
            .Group("Message",
                members => members.BytesField("Data", 4),
                nodeDef: null,
                processors =>
                {
                    if (sysex)
                    {
                        processors.Add("midi.sysex");
                    }
                    processors.Add("midi.xorchecksum");
                })
            .Build();
        return new SerializerBuilder()
            .AddSchemas(CreateSchemas(schema))
            .AddProcessors(CreateProcessors())
            .Build();
    }
}
