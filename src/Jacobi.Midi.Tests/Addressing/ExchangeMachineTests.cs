using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The built-in exchange machines on their own: events in, actions out (no device, no timing).
/// </summary>
public class ExchangeMachineTests
{
    private static readonly TransferSettings Settings = new(TimeSpan.FromMilliseconds(500));

    private static (AddressMap Map, Transfer Request, Transfer Data, ReceivedData Patch1) Gp8()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();
        var map = AddressMap.Create(serializer, new SchemaName("Gp8.AddressMap"));

        var dump = File.ReadAllBytes(TestHelpers.PathOf("Schema/gp8-sysex.syx")).AsSpan(59, 59).ToArray();   // memory patch 2
        Assert.True(map.TryReadData(dump, new DeviceValues { ["deviceId"] = 0 }, out var patch));

        var request = new Transfer(map.Protocol.Requests[0], patch.Block);
        var data = new Transfer(map.Protocol.DataMessages[0] with { Interval = TimeSpan.Zero }, patch.Block);
        return (map, request, data, patch);
    }

    private static ExchangeMessage Reply(AddressMessageKind kind, Transfer transfer)
        => new(kind, transfer.Message);

    [Fact]
    public void RequestReply_DataCompletesAfterTheInterval()
    {
        var (_, request, _, patch) = Gp8();
        var machine = ExchangeProtocols.OneWay.Receive(request, Settings);

        Assert.Equal(new SendMessage(request.Message), Assert.IsType<SendMessage>(machine.Start().Value));
        Assert.Equal(new WaitForMessage(Settings.ResponseTimeout), Assert.IsType<WaitForMessage>(machine.OnSent().Value));
        // the RQ1 has no interval of its own; the DT1 has 20 ms
        var done = machine.OnMessage(new ExchangeMessage(AddressMessageKind.Data, patch.Message, patch));
        Assert.Equal(AddressBlockStatus.Completed, Assert.IsType<Complete>(done.Value).Status);
    }

    [Fact]
    public void RequestReply_OtherDataIsIgnored()
    {
        var (map, request, _, _) = Gp8();
        var other = File.ReadAllBytes(TestHelpers.PathOf("Schema/gp8-sysex.syx")).AsSpan(0, 59).ToArray();   // memory patch 1
        Assert.True(map.TryReadData(other, new DeviceValues { ["deviceId"] = 0 }, out var patch1));
        var machine = ExchangeProtocols.OneWay.Receive(request, Settings);
        machine.Start();
        machine.OnSent();

        Assert.IsType<Ignore>(machine.OnMessage(new ExchangeMessage(AddressMessageKind.Data, patch1.Message, patch1)).Value);
    }

    [Fact]
    public void RequestReply_NakRejects_TimeoutTimesOut()
    {
        var (_, request, _, _) = Gp8();

        var rejected = ExchangeProtocols.OneWay.Receive(request, Settings);
        rejected.Start();
        rejected.OnSent();
        Assert.Equal(AddressBlockStatus.Rejected, Assert.IsType<Complete>(rejected.OnMessage(Reply(AddressMessageKind.Nak, request)).Value).Status);

        var silent = ExchangeProtocols.OneWay.Receive(request, Settings);
        silent.Start();
        silent.OnSent();
        Assert.Equal(AddressBlockStatus.TimedOut, Assert.IsType<Complete>(silent.OnElapsed().Value).Status);
    }

    [Fact]
    public void SendOnly_PausesForTheInterval()
    {
        var (_, _, data, _) = Gp8();
        var interval = data with { Message = data.Message with { Interval = TimeSpan.FromMilliseconds(20) } };
        var machine = ExchangeProtocols.OneWay.Send(interval, Settings);

        Assert.IsType<SendMessage>(machine.Start().Value);
        Assert.Equal(new Pause(TimeSpan.FromMilliseconds(20)), Assert.IsType<Pause>(machine.OnSent().Value));
        Assert.Equal(AddressBlockStatus.Completed, Assert.IsType<Complete>(machine.OnElapsed().Value).Status);
    }

    [Fact]
    public void AcknowledgedSend_AckCompletes_NakRejects_ErrorFails_SilenceTimesOut()
    {
        var (_, _, data, _) = Gp8();

        ExchangeAction After(AddressMessageKind? reply)
        {
            var machine = ExchangeProtocols.Acknowledged.Send(data, Settings);
            Assert.IsType<SendMessage>(machine.Start().Value);
            Assert.IsType<WaitForMessage>(machine.OnSent().Value);
            return reply is { } kind ? machine.OnMessage(Reply(kind, data)) : machine.OnElapsed();
        }

        Assert.Equal(AddressBlockStatus.Completed, Assert.IsType<Complete>(After(AddressMessageKind.Ack).Value).Status);
        Assert.Equal(AddressBlockStatus.Rejected, Assert.IsType<Complete>(After(AddressMessageKind.Nak).Value).Status);
        Assert.Equal(AddressBlockStatus.Failed, Assert.IsType<Complete>(After(AddressMessageKind.Error).Value).Status);
        Assert.Equal(AddressBlockStatus.TimedOut, Assert.IsType<Complete>(After(null).Value).Status);
    }
}
