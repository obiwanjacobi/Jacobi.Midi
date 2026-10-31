using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// An acknowledged exchange with a minimal envelope (manufacturer id 7D, non-commercial):
///   request  F0 7D 01 aa ss F7      data  F0 7D 02 aa dd… F7      ack  F0 7D 03 F7      nak  F0 7D 04 F7
/// The exchange file names the ack and nak messages: sent data waits for them.
/// </summary>
public class AcknowledgedExchangeTests
{
    private const string Envelope = """
        <schema name="Ack">
          <includes>
            <include schema="midi" />
          </includes>
          <nodeDefs>
            <group name="body" />
          </nodeDefs>
          <members>
            <group name="Message">
              <processors>
                <processor name="midi.sysex" />
              </processors>
              <members>
                <group name="Request">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x01" />
                    <field name="Address" datatype="sys.uint8" byteLength="1" midi.role="address" />
                    <field name="Size" datatype="sys.uint8" midi.role="size" />
                  </members>
                </group>
                <group name="Data">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x02" />
                    <field name="Address" datatype="sys.uint8" byteLength="1" midi.role="address" />
                    <choice name="Body" memberNodeDef="body" midi.role="data" />
                  </members>
                </group>
                <group name="Ack">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x03" />
                  </members>
                </group>
                <group name="Nak">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x04" />
                  </members>
                </group>
              </members>
            </group>
            <group name="Map" nodeDef="body" midi.address="0x10">
              <members>
                <field name="A" datatype="sys.uint8" />
                <field name="B" datatype="sys.uint8" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private const string Exchanges = """
        <exchanges for="Ack.Map" responseTimeout="200">
          <exchange name="requestData">
            <state name="request">
              <send message="Ack.Message.Request" />
              <receive message="Ack.Message.Data" />
              <on event="complete" result="completed" />
              <on message="Ack.Message.Nak" result="rejected" />
            </state>
          </exchange>
          <exchange name="sendData">
            <state name="write">
              <send message="Ack.Message.Data" />
              <on message="Ack.Message.Ack" result="completed" />
              <on message="Ack.Message.Nak" result="rejected" />
            </state>
          </exchange>
          <exchange name="sendDataOneWay">
            <state name="write">
              <send message="Ack.Message.Data" />
              <on event="sent" result="completed" />
            </state>
          </exchange>
        </exchanges>
        """;

    private static readonly byte[] Ack = [0xF0, 0x7D, 0x03, 0xF7];
    private static readonly byte[] Nak = [0xF0, 0x7D, 0x04, 0xF7];

    private static Serializer CreateSerializer()
        => TestDevice.CreateSerializer(TestDevice.Parse(Envelope));

    private static PlanRange MapRange(Serializer serializer)
        => serializer.GetPlan(new SchemaName("Ack.Map")).CreateRange("Map");

    private static MessageExchange CreateExchange(Serializer serializer, FakeDevice device)
        => new(new AddressMaps(serializer), TestDevice.CreateExchanges(files: Exchanges), device, new DeviceValues());

    private static Task<TransferResult> Send(MessageExchange exchange, PlanRange range, IValueSource source, string name = ExchangeNames.SendData)
        => exchange.Execute(name, range, new ExchangeData { Source = source }, TestContext.Current.CancellationToken);

    private static FieldValueStore Values(byte a, byte b)
    {
        var values = new FieldValueStore();
        values.Set("Map.A", InstancePath.Empty, new LogicalField("A", typeof(byte), a));
        values.Set("Map.B", InstancePath.Empty, new LogicalField("B", typeof(byte), b));
        return values;
    }

    [Fact]
    public void MessagesWithoutData_AreFoundByName()
    {
        var map = AddressMap.Create(CreateSerializer(), new SchemaName("Ack.Map"));

        var ack = map.Message(new SchemaName("Ack.Message.Ack"));
        Assert.False(ack.IsData);
        Assert.False(ack.IsRequest);
        Assert.Throws<InvalidOperationException>(() => map.Message(new SchemaName("Ack.Message.Unknown")));
    }

    [Fact]
    public void TryRead_RecognisesAckAndNak()
    {
        var map = AddressMap.Create(CreateSerializer(), new SchemaName("Ack.Map"));
        var ack = map.Message(new SchemaName("Ack.Message.Ack"));
        var nak = map.Message(new SchemaName("Ack.Message.Nak"));

        Assert.True(map.Protocol.TryRead(ack, Ack, new DeviceValues()));
        Assert.False(map.Protocol.TryRead(ack, Nak, new DeviceValues()));
        Assert.True(map.Protocol.TryRead(nak, Nak, new DeviceValues()));
        Assert.False(map.Protocol.TryRead(ack, new byte[] { 0xF0, 0x7D, 0x02, 0x10, 0x01, 0x02, 0xF7 }, new DeviceValues()));
    }

    [Fact]
    public async Task Send_Ack_Completes()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { DataReply = Ack };
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer), Values(5, 6));

        Assert.True(result.IsComplete);
        Assert.Equal("F07D02100506F7", Convert.ToHexString(Assert.Single(device.Sent)));
    }

    [Fact]
    public async Task Send_Nak_IsRejected()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { DataReply = Nak };
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer), Values(5, 6));

        var block = Assert.Single(result.Blocks);
        Assert.Equal(BlockStatus.Rejected, block.Status);
        Assert.Equal("'Ack.Message.Nak' in state 'write' of 'sendData'.", block.Reason);
    }

    [Fact]
    public async Task Send_NoAck_TimesOut()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeDevice());

        var result = await Send(exchange, MapRange(serializer), Values(5, 6));

        Assert.Equal(BlockStatus.TimedOut, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task Receive_Data_ReportsProgress()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { RequestReply = [0xF0, 0x7D, 0x02, 0x10, 0x07, 0x08, 0xF7] };
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();
        var progress = new List<BlockResult>();

        var result = await exchange.Execute(ExchangeNames.RequestData, MapRange(serializer), new ExchangeData { Sink = sink }, new Progress(progress), TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal("F07D011002F7", Convert.ToHexString(Assert.Single(device.Sent)));
        Assert.True(sink.TryGet("Map.B", InstancePath.Empty, out var b));
        Assert.Equal((byte)8, b.Value);
        Assert.Equal(BlockStatus.Completed, Assert.Single(progress).Status);
    }

    [Fact]
    public async Task Receive_Nak_IsRejected()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeDevice { RequestReply = Nak });

        var result = await exchange.Execute(ExchangeNames.RequestData, MapRange(serializer), new ExchangeData { Sink = new FieldValueStore() }, TestContext.Current.CancellationToken);

        Assert.Equal(BlockStatus.Rejected, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task AnotherExchangeOfTheFile_RunsByItsName()
    {
        var serializer = CreateSerializer();
        // one way: the data is not acknowledged, so a silent device completes the send
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer), Values(5, 6), "sendDataOneWay");

        Assert.True(result.IsComplete);
        Assert.Single(device.Sent);
    }

    /// <summary>Collects progress reports synchronously (System.Progress posts them asynchronously).</summary>
    private sealed class Progress(List<BlockResult> results) : IProgress<BlockResult>
    {
        public void Report(BlockResult value) => results.Add(value);
    }

    /// <summary>
    /// A device for the tests: answers a request and a data message with the configured reply (none when null),
    /// from a background thread, and records what it receives.
    /// </summary>
    private sealed class FakeDevice : ISysExPort
    {
        public byte[]? RequestReply { get; init; }
        public byte[]? DataReply { get; init; }
        public List<byte[]> Sent { get; } = [];

        public event SysExReceivedHandler? Received;

        public MidiConnectionState State => MidiConnectionState.Open;

        public event EventHandler<MidiConnectionStateChangedArgs>? StateChanged { add { } remove { } }

        public ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct)
        {
            var bytes = sysEx.ToArray();
            Sent.Add(bytes);
            var reply = bytes[2] switch
            {
                0x01 => RequestReply,
                0x02 => DataReply,
                _ => null,
            };
            if (reply is not null)
            {
                _ = Task.Run(() => Received?.Invoke(reply), ct);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
