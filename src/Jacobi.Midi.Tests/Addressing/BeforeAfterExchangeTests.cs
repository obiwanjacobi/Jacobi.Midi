using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The turns once per call of an exchange, around the blocks ('before' and 'after'), with a minimal envelope
/// (manufacturer id 7D, non-commercial):
///   request  F0 7D 01 aa ss F7    data  F0 7D 02 aa dd… F7    ack  F0 7D 03 F7    nak  F0 7D 04 F7
///   header   F0 7D 05 aa ss F7 (built for the whole range)    footer  F0 7D 06 F7 (a fixed message)
/// </summary>
public class BeforeAfterExchangeTests
{
    private const string Envelope = """
        <schema name="Frame">
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
                <group name="Header">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x05" />
                    <field name="Address" datatype="sys.uint8" byteLength="1" midi.role="address" />
                    <field name="Size" datatype="sys.uint8" midi.role="size" />
                  </members>
                </group>
                <group name="Footer">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Command" datatype="sys.uint8" value="0x06" />
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
        <exchanges for="Frame.Map" responseTimeout="100">
          <exchange name="sendData">
            <before>
              <turn name="header">
                <send message="Frame.Message.Header" />
              </turn>
            </before>
            <turn name="write">
              <send message="Frame.Message.Data" />
            </turn>
            <after>
              <turn name="footer">
                <send message="Frame.Message.Footer" />
              </turn>
            </after>
          </exchange>
          <exchange name="sendAcknowledged">
            <before>
              <turn name="header">
                <send message="Frame.Message.Header" />
                <on message="Frame.Message.Ack" result="completed" />
                <on message="Frame.Message.Nak" result="rejected" />
              </turn>
            </before>
            <turn name="write">
              <send message="Frame.Message.Data" />
            </turn>
            <after>
              <turn name="store">
                <send message="Frame.Message.Footer" />
                <on message="Frame.Message.Ack" result="completed" />
              </turn>
            </after>
          </exchange>
          <exchange name="requestData">
            <before>
              <turn name="header">
                <send message="Frame.Message.Header" />
              </turn>
            </before>
            <turn name="request">
              <send message="Frame.Message.Request" />
              <receive message="Frame.Message.Data" />
            </turn>
            <after>
              <turn name="footer">
                <send message="Frame.Message.Footer" />
              </turn>
            </after>
          </exchange>
          <exchange name="dataAfter">
            <turn name="write">
              <send message="Frame.Message.Footer" />
            </turn>
            <after>
              <turn name="again">
                <send message="Frame.Message.Data" />
              </turn>
            </after>
          </exchange>
        </exchanges>
        """;

    private static readonly byte[] Ack = [0xF0, 0x7D, 0x03, 0xF7];
    private static readonly byte[] Nak = [0xF0, 0x7D, 0x04, 0xF7];
    // the header for the whole map: address 0x10, size 2
    private static readonly byte[] Header = [0xF0, 0x7D, 0x05, 0x10, 0x02, 0xF7];
    private static readonly byte[] Footer = [0xF0, 0x7D, 0x06, 0xF7];
    private static readonly byte[] MapData = [0xF0, 0x7D, 0x02, 0x10, 0x05, 0x06, 0xF7];

    private static Serializer CreateSerializer()
        => TestDevice.CreateSerializer(TestDevice.Parse(Envelope));

    private static PlanRange MapRange(Serializer serializer)
        => serializer.GetPlan(new SchemaName("Frame.Map")).CreateRange("Map");

    private static MessageExchange CreateExchange(Serializer serializer, FakeDevice device)
        => new(new MessageMaps(serializer), TestDevice.CreateExchanges(files: Exchanges), device, new DeviceValues());

    private static Task<TransferResult> Send(MessageExchange exchange, PlanRange range, string name = ExchangeNames.SendData)
        => exchange.Execute(name, range, new ExchangeData { Source = Values(5, 6) }, TestContext.Current.CancellationToken);

    private static FieldValueStore Values(byte a, byte b)
    {
        var values = new FieldValueStore();
        values.Set("Map.A", InstancePath.Empty, new LogicalField("A", typeof(byte), a));
        values.Set("Map.B", InstancePath.Empty, new LogicalField("B", typeof(byte), b));
        return values;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    [Fact]
    public async Task Before_IsSentOnce_BeforeTheBlocks_ForTheWholeRange()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer));

        Assert.True(result.IsComplete);
        Assert.Equal(BlockStatus.Completed, result.Before!.Status);
        Assert.Equal(MapRange(serializer), result.Before.Block);
        // the header first, once, with the address and size of the whole range; then the data
        Assert.Equal(Hex(Header), Hex(device.Sent[0]));
        Assert.Single(device.Sent, sent => sent[2] == 0x05);
        Assert.All(device.Sent.Skip(1).SkipLast(1), sent => Assert.Equal(0x02, sent[2]));
    }

    [Fact]
    public async Task Before_WaitsForItsAcknowledgement_ThenTheBlocks()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Replies = { [0x05] = Ack, [0x06] = Ack } };
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer), "sendAcknowledged");

        Assert.True(result.IsComplete);
        Assert.Equal(BlockStatus.Completed, result.Before!.Status);
        Assert.Equal([0x05, 0x02, 0x06], device.Sent.Select(sent => sent[2]));
    }

    [Fact]
    public async Task Before_Rejected_NoBlockIsTransferred()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Replies = { [0x05] = Nak } };
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer), "sendAcknowledged");

        Assert.False(result.IsComplete);
        Assert.Equal(BlockStatus.Rejected, result.Before!.Status);
        Assert.Equal("'Frame.Message.Nak' in turn 'header' (before) of 'sendAcknowledged'.", result.Before.Reason);
        // every block is reported with the status of the 'before', as not transferred; the 'after' does not run
        Assert.NotEmpty(result.Blocks);
        Assert.All(result.Blocks, block => Assert.Equal(BlockStatus.Rejected, block.Status));
        Assert.All(result.Blocks, block => Assert.StartsWith("Not transferred", block.Reason));
        Assert.Null(result.After);
        Assert.Equal([0x05], device.Sent.Select(sent => sent[2]));
    }

    [Fact]
    public async Task Before_TimesOut_NoBlockIsTransferred()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer), "sendAcknowledged");

        Assert.Equal(BlockStatus.TimedOut, result.Before!.Status);
        Assert.Equal("timeout in turn 'header' (before) of 'sendAcknowledged'.", result.Before.Reason);
        Assert.All(result.Blocks, block => Assert.Equal(BlockStatus.TimedOut, block.Status));
        Assert.DoesNotContain(device.Sent, sent => sent[2] == 0x02);
    }

    [Fact]
    public async Task Before_OfAReceive_IsSentBeforeTheRequest()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Replies = { [0x01] = MapData } };
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await exchange.Execute(ExchangeNames.RequestData, MapRange(serializer), new ExchangeData { Sink = sink }, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal([0x05, 0x01, 0x06], device.Sent.Select(sent => sent[2]));
        Assert.True(sink.TryGet("Map.B", InstancePath.Empty, out var b));
        Assert.Equal((byte)6, b.Value);
    }

    [Fact]
    public async Task After_IsSentOnce_AfterTheBlocks()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer));

        Assert.Equal(BlockStatus.Completed, result.After!.Status);
        Assert.Equal(Hex(Footer), Hex(device.Sent[^1]));
        Assert.Single(device.Sent, sent => sent[2] == 0x06);
    }

    [Fact]
    public async Task After_DoesNotRun_WhenABlockFails()
    {
        var serializer = CreateSerializer();
        // the request is not answered
        var device = new FakeDevice();
        await using var exchange = CreateExchange(serializer, device);

        var result = await exchange.Execute(ExchangeNames.RequestData, MapRange(serializer), new ExchangeData { Sink = new FieldValueStore() }, TestContext.Current.CancellationToken);

        Assert.Equal(BlockStatus.Completed, result.Before!.Status);
        Assert.Equal(BlockStatus.TimedOut, result.Blocks[0].Status);
        Assert.Null(result.After);
        Assert.DoesNotContain(device.Sent, sent => sent[2] == 0x06);
    }

    [Fact]
    public async Task After_NotAcknowledged_TheTransferIsNotComplete()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Replies = { [0x05] = Ack } };
        await using var exchange = CreateExchange(serializer, device);

        var result = await Send(exchange, MapRange(serializer), "sendAcknowledged");

        Assert.All(result.Blocks, block => Assert.Equal(BlockStatus.Completed, block.Status));
        Assert.Equal(BlockStatus.TimedOut, result.After!.Status);
        Assert.Equal("timeout in turn 'store' (after) of 'sendAcknowledged'.", result.After.Reason);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task After_ThatFails_KeepsTheSentValuesInTheSession()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice { Replies = { [0x05] = Ack } };
        await using var session = new DeviceSession(CreateExchange(serializer, device), new DeviceSessionOptions());
        var ct = TestContext.Current.CancellationToken;

        var sent = await session.Execute("sendAcknowledged", MapRange(serializer), new ExchangeData { Source = Values(5, 6) }, progress: null, ct);
        var sink = new FieldValueStore();
        var received = await session.RequestData(MapRange(serializer), sink, ct);

        // the data reached the device: the session holds it and answers without a request
        Assert.Equal(BlockStatus.TimedOut, sent.After!.Status);
        Assert.True(received.IsComplete);
        Assert.DoesNotContain(device.Sent, message => message[2] == 0x01);
        Assert.True(sink.TryGet("Map.A", InstancePath.Empty, out var a));
        Assert.Equal((byte)5, a.Value);
    }

    [Fact]
    public async Task DataMessage_InTheTurnsAfter_IsNotPossible()
    {
        var serializer = CreateSerializer();
        await using var exchange = CreateExchange(serializer, new FakeDevice());

        var problems = exchange.Validate("dataAfter", MapRange(serializer));

        Assert.Contains("'Frame.Message.Data' carries data of 'Frame.Map': the turns before and after the blocks send no data.", problems);
    }

    /// <summary>
    /// A device for the tests: answers a message with the reply configured for its command (none when there is none),
    /// from a background thread, and records what it receives.
    /// </summary>
    private sealed class FakeDevice : ISysExPort
    {
        public Dictionary<byte, byte[]> Replies { get; } = [];
        public List<byte[]> Sent { get; } = [];

        public event SysExReceivedHandler? Received;

        public MidiConnectionState State => MidiConnectionState.Open;

        public event EventHandler<MidiConnectionStateChangedArgs>? StateChanged { add { } remove { } }

        public ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct)
        {
            var bytes = sysEx.ToArray();
            lock (Sent)
            {
                Sent.Add(bytes);
            }
            if (Replies.TryGetValue(bytes[2], out var reply))
            {
                _ = Task.Run(() => Received?.Invoke(reply), ct);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
