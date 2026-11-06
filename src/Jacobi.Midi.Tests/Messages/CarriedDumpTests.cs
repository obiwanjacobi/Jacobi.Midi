using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Messages;

/// <summary>
/// The default message map for a root carried as the body of a message's open choice, without an address map. A
/// made-up envelope (manufacturer id 7D, non-commercial) with a program dump selected by a function code; two device
/// models share the contract and the model id in the dump selects the body:
///   request  F0 7D dd 10 F7      program dump  F0 7D dd mm 40 ll cc F7      (dd: device id, mm: model, ll: level, cc: cutoff)
/// </summary>
public class CarriedDumpTests
{
    private const string Envelope = """
        <schema name="Env">
          <includes>
            <include schema="midi" />
          </includes>
          <nodeDefs>
            <group name="program" />
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
                    <field name="Device" datatype="sys.uint8" midi.role="device.deviceId" />
                    <field name="Function" datatype="sys.uint8" value="0x10" />
                  </members>
                </group>
                <group name="Dump">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Device" datatype="sys.uint8" midi.role="device.deviceId" />
                    <field name="Model" datatype="sys.uint8" midi.role="device.model" />
                    <field name="Function" datatype="sys.uint8" value="0x40" />
                    <choice name="Body" memberNodeDef="program" midi.role="data" />
                  </members>
                </group>
              </members>
            </group>
          </members>
        </schema>
        """;

    private const string Devices = """
        <schema name="Synth">
          <includes>
            <include schema="Env" />
          </includes>
          <members>
            <group name="Program" nodeDef="Env.program" midi.device.model="1">
              <members>
                <field name="Level" datatype="sys.uint8" />
                <field name="Cutoff" datatype="sys.uint8" />
              </members>
            </group>
            <group name="OtherProgram" nodeDef="Env.program" midi.device.model="2">
              <members>
                <field name="Volume" datatype="sys.uint8" />
                <field name="Pan" datatype="sys.uint8" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private const string Exchanges = """
        <exchanges for="Synth.Program" responseTimeout="1000">
          <exchange name="requestData">
            <turn name="request">
              <send message="Env.Message.Request" />
              <receive message="Env.Message.Dump" />
            </turn>
          </exchange>
          <exchange name="sendData">
            <turn name="write">
              <send message="Env.Message.Dump" />
            </turn>
          </exchange>
        </exchanges>
        """;

    private static readonly SchemaName ProgramName = new("Synth.Program");

    private static Serializer CreateSerializer(params string[] extra)
        => TestDevice.CreateSerializer([TestDevice.Parse(Envelope), TestDevice.Parse(Devices), .. extra.Select(TestDevice.Parse)]);

    private static PlanRange Range(Serializer serializer, string path)
        => serializer.GetPlan(ProgramName).CreateRange(path, path);

    private static MessageExchange CreateExchange(Serializer serializer, DumpDevice device, int responseTimeoutMs = 1000)
    {
        var exchanges = new ExchangeDefinitions();
        exchanges.Add(ExchangeDefinitionReader.ReadXml(Exchanges.Replace("responseTimeout=\"1000\"", $"responseTimeout=\"{responseTimeoutMs}\""), "test"));
        return new MessageExchange(new MessageMaps(serializer), exchanges, device, new DeviceValues { ["deviceId"] = 0 });
    }

    private static byte[] ProgramDump(byte level, byte cutoff, byte model = 1) => [0xF0, 0x7D, 0x00, model, 0x40, level, cutoff, 0xF7];

    private static byte Value(FieldValueStore store, string path)
    {
        Assert.True(store.TryGet(path, InstancePath.Empty, out var value));
        return Assert.IsType<byte>(value.Value);
    }

    [Fact]
    public void DataMessages_AreTheCarriers()
    {
        var serializer = CreateSerializer();
        var map = Assert.IsType<DumpMap>(new MessageMaps(serializer).Get(serializer.GetPlan(ProgramName)));

        Assert.Equal("Env.Message.Dump", Assert.Single(map.DataMessages).FullName);
        Assert.Equal(1, map.Device["model"]);
        Assert.True(map.IsData(map.Message(new SchemaName("Env.Message.Dump"))));
        Assert.False(map.IsData(map.Message(new SchemaName("Env.Message.Request"))));
    }

    [Fact]
    public async Task RequestData_ReceivesTheBody()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice { Reply = ProgramDump(100, 20) };
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await exchange.Execute(ExchangeNames.RequestData, Range(serializer, "Program.Level"), new ExchangeData { Sink = sink }, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(serializer.GetPlan(ProgramName).CreateRange("Program"), Assert.Single(result.Blocks).Block);
        Assert.Equal("F07D0010F7", Convert.ToHexString(Assert.Single(device.Sent)));
        Assert.Equal(100, Value(sink, "Program.Level"));
        Assert.Equal(20, Value(sink, "Program.Cutoff"));
    }

    [Fact]
    public async Task RequestData_TheBodyOfAnotherModel_IsNotRecognised()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice { Reply = ProgramDump(100, 20, model: 2) };
        await using var exchange = CreateExchange(serializer, device, responseTimeoutMs: 50);

        var result = await exchange.Execute(ExchangeNames.RequestData, Range(serializer, "Program.Level"), new ExchangeData { Sink = new FieldValueStore() }, TestContext.Current.CancellationToken);

        Assert.Equal(BlockStatus.TimedOut, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task SendData_WritesTheBodyInTheCarrier()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice();
        await using var exchange = CreateExchange(serializer, device);
        var values = new FieldValueStore();
        values.Set("Program.Level", InstancePath.Empty, new LogicalField("Level", typeof(byte), (byte)5));
        values.Set("Program.Cutoff", InstancePath.Empty, new LogicalField("Cutoff", typeof(byte), (byte)6));

        var result = await exchange.Execute(ExchangeNames.SendData, Range(serializer, "Program.Level"), new ExchangeData { Source = values }, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        // the model comes from the root (midi.device.model), the device id from the device values
        Assert.Equal("F07D0001400506F7", Convert.ToHexString(Assert.Single(device.Sent)));
    }

    [Fact]
    public async Task Session_SecondRequest_IsAnsweredFromTheState()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice { Reply = ProgramDump(100, 20) };
        await using var session = new DeviceSession(CreateExchange(serializer, device), new DeviceSessionOptions());
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await session.RequestData(Range(serializer, "Program.Level"), new FieldValueStore(), ct)).IsComplete);
        var sink = new FieldValueStore();
        Assert.True((await session.RequestData(Range(serializer, "Program.Cutoff"), sink, ct)).IsComplete);

        Assert.Single(device.Sent);
        Assert.Equal(100, Value(sink, "Program.Level"));
        Assert.Equal(20, Value(sink, "Program.Cutoff"));
    }

    [Fact]
    public void Validator_ACarrierWithoutAddress_IsNoError()
    {
        var problems = AddressMapValidator.Validate(CreateSerializer());

        Assert.DoesNotContain(problems, problem => problem.Severity == MessageMapProblemSeverity.Error);
    }

    [Fact]
    public void ARootCarriedOnlyInsideAnotherBody_IsNotSupported()
    {
        // 'Inner' is carried by the open choice of 'Outer', which is itself a body (of 'Env.Message.Dump')
        var serializer = CreateSerializer("""
            <schema name="Nested">
              <includes>
                <include schema="Env" />
              </includes>
              <nodeDefs>
                <group name="inner" />
              </nodeDefs>
              <members>
                <group name="Outer" nodeDef="Env.program" midi.device.model="3">
                  <members>
                    <field name="Kind" datatype="sys.uint8" />
                    <choice name="Part" memberNodeDef="inner" midi.role="data" />
                  </members>
                </group>
                <group name="Inner" nodeDef="inner">
                  <members>
                    <field name="Level" datatype="sys.uint8" />
                  </members>
                </group>
              </members>
            </schema>
            """);

        var exception = Assert.Throws<MessageMapException>(() => new MessageMaps(serializer).Get(serializer.GetPlan(new SchemaName("Nested.Inner"))));
        Assert.Contains(exception.Errors, error => error.Contains("only carried inside another body (Nested.Outer)", StringComparison.Ordinal));
    }
}
