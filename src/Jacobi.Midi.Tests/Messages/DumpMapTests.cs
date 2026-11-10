using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Messages;

/// <summary>
/// The default message map: a root that is a message itself (a dump), without an address map. A made-up device
/// (manufacturer id 7D, non-commercial) with a voice dump selected by a function code:
///   request  F0 7D dd 10 F7      voice dump  F0 7D dd 40 ll cc F7      (dd: device id, ll: level, cc: cutoff)
/// </summary>
public class DumpMapTests
{
    private const string Schema = """
        <schema name="Dump">
          <includes>
            <include schema="midi" />
          </includes>
          <members>
            <group name="VoiceRequest">
              <processors>
                <processor name="midi.sysex" />
              </processors>
              <members>
                <field name="Id" datatype="sys.uint8" value="0x7D" />
                <field name="Device" datatype="sys.uint8" midi.role="device.deviceId" />
                <field name="Function" datatype="sys.uint8" value="0x10" />
              </members>
            </group>
            <group name="Voice">
              <processors>
                <processor name="midi.sysex" />
              </processors>
              <members>
                <field name="Id" datatype="sys.uint8" value="0x7D" />
                <field name="Device" datatype="sys.uint8" midi.role="device.deviceId" />
                <field name="Function" datatype="sys.uint8" value="0x40" />
                <field name="Level" datatype="sys.uint8" />
                <field name="Cutoff" datatype="sys.uint8" />
              </members>
            </group>
          </members>
        </schema>
        """;

    private const string Exchanges = """
        <exchanges for="Dump.Voice" responseTimeout="1000">
          <exchange name="requestData">
            <turn name="request">
              <send message="Dump.VoiceRequest" />
              <receive message="Dump.Voice" />
            </turn>
          </exchange>
          <exchange name="sendData">
            <turn name="write">
              <send message="Dump.Voice" />
            </turn>
          </exchange>
        </exchanges>
        """;

    private static readonly SchemaName VoiceName = new("Dump.Voice");

    private static Serializer CreateSerializer(params string[] extra)
        => TestDevice.CreateSerializer([TestDevice.Parse(Schema), .. extra.Select(TestDevice.Parse)]);

    private static PlanRange Range(Serializer serializer, string path)
        => serializer.GetPlan(VoiceName).CreateRange(path, path);

    private static MessageExchange CreateExchange(Serializer serializer, DumpDevice device, byte deviceId = 0)
    {
        var exchanges = new ExchangeDefinitions();
        exchanges.Add(ExchangeDefinitionReader.ReadXml(Exchanges, "test"));
        return new MessageExchange(new MessageMaps(serializer), exchanges, device, new DeviceValues { ["deviceId"] = deviceId });
    }

    private static byte[] VoiceDump(byte level, byte cutoff, byte deviceId = 0) => [0xF0, 0x7D, deviceId, 0x40, level, cutoff, 0xF7];

    private static FieldValueStore Voice(byte level, byte cutoff)
    {
        var values = new FieldValueStore();
        values.Set("Voice.Level", InstancePath.Empty, new LogicalField("Level", typeof(byte), level));
        values.Set("Voice.Cutoff", InstancePath.Empty, new LogicalField("Cutoff", typeof(byte), cutoff));
        return values;
    }

    private static byte Value(FieldValueStore store, string path)
    {
        Assert.True(store.TryGet(path, InstancePath.Empty, out var value));
        return Assert.IsType<byte>(value.Value);
    }

    [Fact]
    public void Providers_ARootWithoutAddressTags_IsADump()
    {
        var serializer = CreateSerializer();
        var maps = new MessageMaps(serializer);

        Assert.IsType<DumpMap>(maps.Get(serializer.GetPlan(VoiceName)));
        Assert.IsType<AddressMap>(maps.Get(serializer.GetPlan(TestDevice.MapName)));
    }

    [Fact]
    public void Info_AnyKindOfMap_GivesTheRequiredDeviceValues()
    {
        var maps = new MessageMaps(CreateSerializer());

        var dump = maps.Info(VoiceName);
        var address = maps.Info(TestDevice.MapName);

        Assert.Equal(VoiceName, dump.Root);
        Assert.Equal(["deviceId"], dump.RequiredDeviceValues);
        Assert.Equal(["deviceId"], address.RequiredDeviceValues);
    }

    [Fact]
    public void Save_WritesTheWholeDump_AndLoadReadsItBack()
    {
        var serializer = CreateSerializer();
        var maps = new MessageMaps(serializer);
        var device = new DeviceValues { ["deviceId"] = 3 };

        // a part of the dump is widened to the whole root
        var saved = maps.Save([Range(serializer, "Voice.Level")], Voice(100, 20), device);

        Assert.Equal([VoiceDump(100, 20, deviceId: 3)], saved);
        var loaded = new FieldValueStore();
        var warnings = new List<string>();
        Assert.Equal(1, maps.Load(VoiceName, [saved[0], VoiceDump(1, 2, deviceId: 4)], device, loaded, warnings));
        Assert.Equal(100, Value(loaded, "Voice.Level"));
        Assert.Equal(20, Value(loaded, "Voice.Cutoff"));
        // the dump of another device is skipped and reported
        Assert.Contains("Message 2", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_PartsOfADump_AreOneMessage_WithAWarningPerPart()
    {
        var serializer = CreateSerializer();
        var warnings = new List<string>();

        var saved = new MessageMaps(serializer).Save([Range(serializer, "Voice.Level"), Range(serializer, "Voice.Cutoff")],
            Voice(100, 20), new DeviceValues { ["deviceId"] = 0 }, warnings: warnings);

        Assert.Equal([VoiceDump(100, 20)], saved);
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning => Assert.Contains("the whole 'Dump.Voice'", warning, StringComparison.Ordinal));
    }

    [Fact]
    public void Save_AMessageThatIsNoDataMessageOfTheRoot_IsRejected()
    {
        var serializer = CreateSerializer();
        var maps = new MessageMaps(serializer);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            maps.Save([Range(serializer, "Voice.Level")], Voice(1, 2), new DeviceValues { ["deviceId"] = 0 }, new SchemaName("Dump.VoiceRequest")));
        Assert.Contains("not a data message", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_WithoutTheDeviceValues_IsRejected()
    {
        var serializer = CreateSerializer();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new MessageMaps(serializer).Save([Range(serializer, "Voice.Level")], Voice(1, 2), new DeviceValues()));
        Assert.Contains("deviceId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_NotASysExMessage_IsNotADump()
    {
        const string plain = """
            <schema name="Plain">
              <members>
                <group name="Settings">
                  <members>
                    <field name="Level" datatype="sys.uint8" />
                  </members>
                </group>
              </members>
            </schema>
            """;
        var maps = new MessageMaps(CreateSerializer(plain));

        var exception = Assert.Throws<MessageMapException>(() => maps.Info(new SchemaName("Plain.Settings")));
        Assert.Contains("not a SysEx message", exception.Message);
        Assert.Equal(MessageMapKind.MessageDump, exception.Kind);
    }

    [Fact]
    public void Info_ItemCount_IsTheMapsItemCount()
    {
        const string bank = """
            <schema name="Bank">
              <includes>
                <include schema="midi" />
              </includes>
              <members>
                <group name="Dump">
                  <processors>
                    <processor name="midi.sysex" />
                  </processors>
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <repeat name="Voice" count="4">
                      <members>
                        <field name="Level" datatype="sys.uint8" />
                      </members>
                    </repeat>
                  </members>
                </group>
              </members>
            </schema>
            """;
        var maps = new MessageMaps(CreateSerializer(bank));

        var dump = maps.Info(new SchemaName("Bank.Dump"));
        var address = maps.Info(TestDevice.MapName);

        Assert.Equal(4, dump.ItemCount(new SchemaPath("Dump.Voice")));
        Assert.Null(dump.ItemCount(new SchemaPath("Dump.Id")));
        // a repeat of an address map has its items in the address space (midi.repeat), not in the schema
        Assert.Equal(8, address.ItemCount(new SchemaPath("AddressMap.Memory")));
    }

    [Fact]
    public void Info_Kind_TellsHowTheRootIsCarried()
    {
        var maps = new MessageMaps(CreateSerializer());

        Assert.Equal(MessageMapKind.MessageDump, maps.Info(VoiceName).Kind);
        Assert.Equal(MessageMapKind.Address, maps.Info(TestDevice.MapName).Kind);
    }

    [Fact]
    public void Split_AnyRange_IsTheWholeRoot()
    {
        var serializer = CreateSerializer();
        var map = new MessageMaps(serializer).Get(serializer.GetPlan(VoiceName));

        var block = Assert.Single(map.Split(Range(serializer, "Voice.Level"), map.Message(new SchemaName("Dump.VoiceRequest"))));

        Assert.Equal(Range(serializer, "Voice"), block);
        Assert.True(map.IsData(map.Message(VoiceName)));
        Assert.False(map.IsData(map.Message(new SchemaName("Dump.VoiceRequest"))));
    }

    [Fact]
    public async Task RequestData_OneField_ReceivesTheWholeDump()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice { Reply = VoiceDump(100, 20) };
        await using var exchange = CreateExchange(serializer, device);
        var sink = new FieldValueStore();

        var result = await exchange.Execute(ExchangeNames.RequestData, Range(serializer, "Voice.Level"), new ExchangeData { Sink = sink }, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(Range(serializer, "Voice"), Assert.Single(result.Blocks).Block);
        Assert.Equal("F07D0010F7", Convert.ToHexString(Assert.Single(device.Sent)));
        Assert.Equal(100, Value(sink, "Voice.Level"));
        Assert.Equal(20, Value(sink, "Voice.Cutoff"));
    }

    [Fact]
    public async Task RequestData_DumpOfAnotherDevice_TimesOut()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice { Reply = VoiceDump(100, 20, deviceId: 1) };
        var exchanges = new ExchangeDefinitions();
        exchanges.Add(ExchangeDefinitionReader.ReadXml(Exchanges.Replace("responseTimeout=\"1000\"", "responseTimeout=\"50\""), "test"));
        await using var exchange = new MessageExchange(new MessageMaps(serializer), exchanges, device, new DeviceValues { ["deviceId"] = 0 });

        var result = await exchange.Execute(ExchangeNames.RequestData, Range(serializer, "Voice"), new ExchangeData { Sink = new FieldValueStore() }, TestContext.Current.CancellationToken);

        Assert.Equal(BlockStatus.TimedOut, Assert.Single(result.Blocks).Status);
    }

    [Fact]
    public async Task SendData_WritesTheWholeDump()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice();
        await using var exchange = CreateExchange(serializer, device, deviceId: 3);

        var result = await exchange.Execute(ExchangeNames.SendData, Range(serializer, "Voice"), new ExchangeData { Source = Voice(5, 6) }, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal("F07D03400506F7", Convert.ToHexString(Assert.Single(device.Sent)));
    }

    [Fact]
    public async Task Session_SecondRequest_IsAnsweredFromTheState()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice { Reply = VoiceDump(100, 20) };
        await using var session = new DeviceSession(CreateExchange(serializer, device), new DeviceSessionOptions());
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await session.RequestData(Range(serializer, "Voice.Level"), new FieldValueStore(), ct)).IsComplete);
        var sink = new FieldValueStore();
        Assert.True((await session.RequestData(Range(serializer, "Voice.Cutoff"), sink, ct)).IsComplete);

        Assert.Single(device.Sent);
        Assert.Equal(20, Value(sink, "Voice.Cutoff"));
    }

    [Fact]
    public async Task Session_SendData_KeepsTheSentDumpAsState()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice();
        await using var session = new DeviceSession(CreateExchange(serializer, device), new DeviceSessionOptions());
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await session.SendData(Range(serializer, "Voice"), Voice(7, 8), ct)).IsComplete);
        var sink = new FieldValueStore();
        Assert.True((await session.RequestData(Range(serializer, "Voice"), sink, ct)).IsComplete);

        // sent once, not requested: the state holds the dump that was sent
        Assert.Single(device.Sent);
        Assert.Equal(7, Value(sink, "Voice.Level"));
    }

    [Fact]
    public async Task Session_SendData_ASourceWithoutAllValues_Throws()
    {
        var serializer = CreateSerializer();
        await using var session = new DeviceSession(CreateExchange(serializer, new DumpDevice()), new DeviceSessionOptions());
        var level = new FieldValueStore();
        level.Set("Voice.Level", InstancePath.Empty, new LogicalField("Level", typeof(byte), (byte)7));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendData(Range(serializer, "Voice.Level"), level, TestContext.Current.CancellationToken));
        Assert.Contains("is sent whole", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listen_ADumpTheDeviceSendsOnItsOwn_IsDecoded()
    {
        var serializer = CreateSerializer();
        var device = new DumpDevice();
        await using var exchange = CreateExchange(serializer, device);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var received = new TaskCompletionSource<ReceivedData>();

        var listening = exchange.Listen(Range(serializer, "Voice"), data => received.TrySetResult(data), cancel.Token);
        device.Transmit(VoiceDump(42, 43));
        var data = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancel.CancelAsync();
        await listening;

        var sink = new FieldValueStore();
        data.Decode(sink);
        Assert.Equal(42, Value(sink, "Voice.Level"));
    }
}
