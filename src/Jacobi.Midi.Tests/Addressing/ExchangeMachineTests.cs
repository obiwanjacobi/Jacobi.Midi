using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Xunit;
using static Jacobi.Midi.Tests.Addressing.TestDevice;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The <see cref="ExchangeMachine"/> on its own: the actions it answers to the events, for the exchanges of the
/// <see cref="TestDevice"/> exchange file and a few made up for the tests, without a device.
/// </summary>
public class ExchangeMachineTests
{
    private static readonly AddressMap Map = CreateMap();
    private static readonly DeviceValues Device0 = Map.Device.With(new DeviceValues { ["deviceId"] = 0 });

    private const string Retries = """
        <exchanges for="TestDevice.AddressMap" responseTimeout="500">
          <exchange name="retry">
            <state name="request">
              <send message="TestEnvelope.Message.Request" />
              <receive message="TestEnvelope.Message.Data" />
              <on event="complete" result="completed" />
              <on event="timeout" goto="again" />
            </state>
            <state name="again">
              <send message="TestEnvelope.Message.Request" />
              <receive message="TestEnvelope.Message.Data" />
              <on event="complete" result="completed" />
              <on message="TestEnvelope.Message.Data" goto="again" />
              <on event="timeout" result="failed" />
            </state>
          </exchange>
        </exchanges>
        """;

    private static ExchangeMachine Machine(string name, string xml = ExchangeXml)
    {
        var definition = ExchangeDefinitionReader.ReadXml(xml, "test").Find(name)!;
        return new ExchangeMachine(definition, definition.Messages.ToDictionary(message => message, Map.Message));
    }

    private static ExchangeMessage Data(long address)
    {
        Assert.True(Map.TryReadData(DataMessage(address, Patch(PatchName(0))), Device0, out var data));
        return new ExchangeMessage(Map.Message(new SchemaName("TestEnvelope.Message.Data")), data);
    }

    [Fact]
    public void RequestData_SendsTheRequest_ThenWaitsUntilComplete()
    {
        var machine = Machine(ExchangeNames.RequestData);

        var send = Assert.IsType<SendMessage>(machine.Start().Value);
        Assert.Equal("TestEnvelope.Message.Request", send.Message.FullName);
        Assert.Equal(new WaitForMessage(TimeSpan.FromSeconds(1)), Assert.IsType<WaitForMessage>(machine.OnSent().Value));

        var data = Data(PatchAddress(0));
        Assert.True(machine.Receives(data.Message));
        // more data to come: the state waits on, with a new timeout
        Assert.IsType<WaitForMessage>(machine.OnReceived(data, complete: false).Value);
        var done = Assert.IsType<Complete>(machine.OnReceived(data, complete: true).Value);
        Assert.Equal(BlockStatus.Completed, done.Status);
        Assert.Null(done.Reason);
    }

    [Fact]
    public void RequestData_Timeout_TheReasonNamesTheStateAndTheExchange()
    {
        var machine = Machine(ExchangeNames.RequestData);
        machine.Start();
        machine.OnSent();

        var done = Assert.IsType<Complete>(machine.OnElapsed().Value);

        Assert.Equal(BlockStatus.TimedOut, done.Status);
        Assert.Equal("timeout in state 'request' of 'requestData'.", done.Reason);
    }

    [Fact]
    public void SendData_CompletesWhenSent()
    {
        var machine = Machine(ExchangeNames.SendData);

        Assert.Equal("TestEnvelope.Message.Data", Assert.IsType<SendMessage>(machine.Start().Value).Message.FullName);
        Assert.Equal(BlockStatus.Completed, Assert.IsType<Complete>(machine.OnSent().Value).Status);
        Assert.False(machine.Receives(Data(0).Message));
    }

    [Fact]
    public void AMessageWithoutATransition_IsIgnored()
    {
        var machine = Machine(ExchangeNames.SendData);
        machine.Start();

        Assert.IsType<Ignore>(machine.OnMessage(Data(0)).Value);
    }

    [Fact]
    public void Goto_EntersTheNextStateAndSendsItsMessage()
    {
        var machine = Machine("retry", Retries);
        machine.Start();
        machine.OnSent();

        // the timeout of the first state goes to 'again', which sends the request again
        Assert.Equal("TestEnvelope.Message.Request", Assert.IsType<SendMessage>(machine.OnElapsed().Value).Message.FullName);
        Assert.Equal("again", machine.State.Name);
        Assert.Equal(new WaitForMessage(TimeSpan.FromMilliseconds(500)), Assert.IsType<WaitForMessage>(machine.OnSent().Value));

        // data that does not complete the block: the message transition is taken
        Assert.IsType<SendMessage>(machine.OnReceived(Data(PatchAddress(0)), complete: false).Value);
        var failed = Assert.IsType<Complete>(machine.OnElapsed().Value);
        Assert.Equal(BlockStatus.Failed, failed.Status);
        Assert.Equal("timeout in state 'again' of 'retry'.", failed.Reason);
    }
}
