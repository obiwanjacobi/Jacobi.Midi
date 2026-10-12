using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Processors;

public class RolandChecksumProcessorTests
{
    [Fact]
    public void ChecksumProcessor_CalcChecksum_ReturnsExpectedValue()
    {
        // Arrange
        var processor = new Jacobi.Midi.Roland.Processors.ChecksumProcessor();
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        processor.Write(new ArrayBufferWriter<byte>(), data, null!);
        var checksum = processor.CalcChecksum();

        // Assert
        Assert.Equal(0x76, checksum);
    }

    [Fact]
    public void ChecksumProcessor_WritesChecksum()
    {
        // Arrange
        var processor = new Jacobi.Midi.Roland.Processors.ChecksumProcessor();
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var buffer = new ArrayBufferWriter<byte>();

        // Act
        processor.BeginWrite(buffer, null!);
        var result = processor.Write(buffer, data, null!);
        processor.EndWrite(buffer, null!);

        // Assert
        Assert.Equal(WriteResult.Success, result);
        Assert.Equal(data.Length + 1, buffer.WrittenCount);
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

        var plan = serializer.GetPlan("RolandChecksum.Checksum");
        serializer.Deserialize(plan, new ReadOnlySequence<byte>(data), valueSink);
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

        var plan = serializer.GetPlan("RolandChecksum.Checksum");

        Assert.Throws<InvalidOperationException>(() =>
        {
            serializer.Deserialize(plan, new ReadOnlySequence<byte>(data), valueSink);
        });
    }

    private static SchemaSet CreateChecksumSchema()
    {
        var schema = new SchemaDocumentBuilder("RolandChecksum")
            .Group("Checksum",
                config => config.BytesField("Data", 4),
                nodeDef: null,
                procs => procs.Add("roland.checksum")
             )
            .Build();

        return CreateSchemas(schema);
    }
}
