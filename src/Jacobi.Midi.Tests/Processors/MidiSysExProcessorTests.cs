using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Xunit;
using static Jacobi.Midi.Tests.Schema.TestHelpers;

namespace Jacobi.Midi.Tests.Processors;

public class MidiSysExProcessorTests
{
    [Fact]
    public void MidiSysExProcessor_WritesSysExMessage()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(CreateSysExSchema())
            .AddProcessors(CreateProcessors())
            .Build();

        var buffer = new ArrayBufferWriter<byte>();
        var valueSource = new ValueSource();
        valueSource.Values[$"Message.Data"] = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        serializer.Serialize(SysExRoot, valueSource, buffer);

        Assert.Equal(6, buffer.WrittenCount);
        Assert.Equal(new byte[] { 0xF0, 0x01, 0x02, 0x03, 0x04, 0xF7 }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void MidiSysExProcessor_ReadsSysExMessage()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(CreateSysExSchema())
            .AddProcessors(CreateProcessors())
            .Build();

        var data = new byte[] { 0xF0, 0x01, 0x02, 0x03, 0x04, 0xF7 };
        var valueSink = new ValueSink();

        serializer.Deserialize(SysExRoot, new ReadOnlySequence<byte>(data), valueSink);

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, valueSink.Values[$"Message.Data"]);
    }

    private const string SysExRoot = "MidiSysEx.Message";

    private static SchemaSet CreateSysExSchema()
    {
        var schema = new SchemaDocumentBuilder("MidiSysEx")
            .Group("Message",
                members => members.BytesField("Data", 4),
                nodeDef: null,
                procs => procs.Add("midi.sysex")
             )
            .Build();

        return CreateSchemas(schema);
    }
}
