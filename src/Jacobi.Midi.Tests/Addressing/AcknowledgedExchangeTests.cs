using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The 'acknowledged' exchange protocol with a minimal envelope (manufacturer id 7D, non-commercial):
///   request  F0 7D 01 aa ss F7      data  F0 7D 02 aa dd… F7      ack  F0 7D 03 F7      nak  F0 7D 04 F7
/// The protocol is inferred from the ack/nak messages.
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
                <group name="Request" midi.message="request">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x01" />
                    <field name="Address" datatype="sys.uint8" byteLength="1" midi.role="addressStart" />
                    <field name="Size" datatype="sys.uint8" midi.role="addressSize" />
                  </members>
                </group>
                <group name="Data" midi.message="data">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x02" />
                    <field name="Address" datatype="sys.uint8" byteLength="1" midi.role="addressStart" />
                    <choice name="Body" memberNodeDef="body" midi.role="data" />
                  </members>
                </group>
                <group name="Ack" midi.message="ack">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x03" />
                  </members>
                </group>
                <group name="Nak" midi.message="nak">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x04" />
                  </members>
                </group>
              </members>
            </group>
            <group name="Map" nodeDef="body" midi.addressStart="0x10">
              <members>
                <field name="A" datatype="sys.uint8" />
                <field name="B" datatype="sys.uint8" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private static readonly byte[] Ack = [0xF0, 0x7D, 0x03, 0xF7];
    private static readonly byte[] Nak = [0xF0, 0x7D, 0x04, 0xF7];

    private static Serializer CreateSerializer()
        => TestDevice.CreateSerializer(TestDevice.Parse(Envelope));

    private static PlanRange MapRange(Serializer serializer)
        => serializer.GetPlan(new SchemaName("Ack.Map")).CreateRange("Map");

    private static MessageExchange CreateExchange(Serializer serializer, FakeDevice device, MessageExchangeOptions? options = null)
        => new(new AddressMaps(serializer), device, new DeviceValues(),
            options ?? new MessageExchangeOptions { ResponseTimeout = TimeSpan.FromMilliseconds(200) });

    private static FieldValueStore Values(byte a, byte b)
    {
        var values = new FieldValueStore();
        values.Set("Map.A", InstancePath.Empty, new LogicalField("A", typeof(byte), a));
        values.Set("Map.B", InstancePath.Empty, new LogicalField("B", typeof(byte), b));
        return values;
    }

    [Fact]
    public void Protocol_IsInferredFromTheReplies()
    {
        var map = AddressMap.Create(CreateSerializer(), new SchemaName("Ack.Map"));

        Assert.Equal("acknowledged", map.Protocol.Exchange);
        Assert.Equal([AddressMessageKind.Ack, AddressMessageKind.Nak], map.Protocol.Replies.Select(r => r.Kind));
    }

    [Fact]
    public void TryReadReply_RecognisesAckAndNak()
    {
        var map = AddressMap.Create(CreateSerializer(), new SchemaName("Ack.Map"));

        Assert.True(map.Protocol.TryReadReply(Ack, new DeviceValues(), out var ack));
        Assert.Equal(AddressMessageKind.Ack, ack.Kind);
        Assert.True(map.Protocol.TryReadReply(Nak, new DeviceValues(), out var nak));
        Assert.Equal(AddressMessageKind.Nak, nak.Kind);
        Assert.False(map.Protocol.TryReadReply(new byte[] { 0xF0, 0x7D, 0x02, 0x10, 0x01, 0x02, 0xF7 }, new DeviceValues(), out _));
    }

    [Fact]
    public async Task Send_Ack_Completes()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { DataReply = Ack };
        await using var exchange = CreateExchange(serializer, device);

        var result = await exchange.Send(MapRange(serializer), Values(5, 6), TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal("F07D02100506F7", Convert.ToHexString(Assert.Single(device.Sent)));
    }

    [Fact]
    public async Task Send_Nak_IsRejected()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { DataReply = Nak };
        await using var exchange = CreateExchange(serializer, device);

        var result = await exchange.Send(MapRange(serializer), Values(5, 6), TestContext.Current.CancellationToken);

        var block = Assert.Single(result.Blocks);
        Assert.Equal(AddressBlockStatus.Rejected, block.Status);
        Assert.NotNull(block.Reason);
    }

    [Fact]
    public async Task Send_NoAck_TimesOut()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeDevice());

        var result = await exchange.Send(MapRange(serializer), Values(5, 6), TestContext.Current.CancellationToken);

        Assert.Equal(AddressBlockStatus.TimedOut, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task Receive_Data_ReportsProgress()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { RequestReply = [0xF0, 0x7D, 0x02, 0x10, 0x07, 0x08, 0xF7] };
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();
        var progress = new List<AddressBlockResult>();

        var result = await exchange.Receive(MapRange(serializer), sink, new Progress(progress), TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal("F07D011002F7", Convert.ToHexString(Assert.Single(device.Sent)));
        Assert.True(sink.TryGet("Map.B", InstancePath.Empty, out var b));
        Assert.Equal((byte)8, b.Value);
        Assert.Equal(AddressBlockStatus.Completed, Assert.Single(progress).Status);
    }

    [Fact]
    public async Task Receive_Nak_IsRejected()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeDevice { RequestReply = Nak });

        var result = await exchange.Receive(MapRange(serializer), new FieldValueStore(), TestContext.Current.CancellationToken);

        Assert.Equal(AddressBlockStatus.Rejected, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task Options_Protocol_OverridesTheSchema()
    {
        var serializer = CreateSerializer();
        // one way: the data is not acknowledged, so a silent device completes the send
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device, new MessageExchangeOptions
        {
            ResponseTimeout = TimeSpan.FromMilliseconds(200),
            Protocol = ExchangeProtocols.OneWay,
        });

        var result = await exchange.Send(MapRange(serializer), Values(5, 6), TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
    }

    /// <summary>Collects progress reports synchronously (System.Progress posts them asynchronously).</summary>
    private sealed class Progress(List<AddressBlockResult> results) : IProgress<AddressBlockResult>
    {
        public void Report(AddressBlockResult value) => results.Add(value);
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
