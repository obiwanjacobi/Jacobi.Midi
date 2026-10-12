using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Processors;

public class RolandChecksumProcessorTests
{
    [Fact]
    public void ChecksumProcessor_WritesChecksum()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(CreateChecksumSchema())
            .AddProcessors(CreateProcessors())
            .Build();

        var buffer = new ArrayBufferWriter<byte>();
        var valueSource = new ValueSource();
        valueSource.Values[$"Checksum.Data"] = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        serializer.Serialize(ChecksumRoot, valueSource, buffer);

        Assert.Equal(5, buffer.WrittenCount);
        Assert.Equal(0x76, buffer.WrittenSpan[4]);
    }

    [Fact]
    public void ChecksumProcessor_ReadsChecksum()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(CreateChecksumSchema())
            .AddProcessors(CreateProcessors())
            .Build();

        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x76 };
        var valueSink = new ValueSink();

        serializer.Deserialize(ChecksumRoot, new ReadOnlySequence<byte>(data), valueSink);
    }

    [Fact]
    public void ChecksumProcessor_ReadsChecksum_Fails()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(CreateChecksumSchema())
            .AddProcessors(CreateProcessors())
            .Build();

        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x10 };
        var valueSink = new ValueSink();

        Assert.Throws<InvalidOperationException>(() =>
        {
            serializer.Deserialize(ChecksumRoot, new ReadOnlySequence<byte>(data), valueSink);
        });
    }

    private const string ChecksumRoot = "RolandChecksum.Checksum";

    private static SchemaSet CreateChecksumSchema()
    {
        var schema = new SchemaDocumentBuilder("RolandChecksum")
            .Group("Checksum",
                members => members.BytesField("Data", 4),
                nodeDef: null,
                procs => procs.Add("roland.checksum")
             )
            .Build();

        return CreateSchemas(schema);
    }
}
