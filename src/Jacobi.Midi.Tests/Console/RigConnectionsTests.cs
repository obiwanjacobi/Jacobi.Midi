using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Connections;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Exchange;
using Xunit;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The MIDI connections of the devices of a rig on a fake backend: which devices connect, how they follow the endpoints,
/// lost connections and edits of the rig, stopping, and the two tests of a connection, which change no values (Identity Request, request of data
/// from the GP-8, answered with a patch of the GP-8 bulk dump).
/// </summary>
public class RigConnectionsTests
{
    private const string Port = "GP-8 Port";
    private static readonly byte[] Dump = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Schema", "gp8-sysex.syx"));

    private static MidiGroupPair Group(byte group) => MidiGroupPair.Same(new MidiGroup(group));

    private static DeviceInstance Gp8(string alias = "gp8", EndpointReference? endpoint = null)
        => new()
        {
            Alias = alias,
            MapRoot = "Gp8.AddressMap",
            DeviceValues = new Dictionary<string, long> { ["deviceId"] = 0 },
            Endpoint = endpoint,
        };

    private static RigSession Open(params DeviceInstance[] devices)
        => RigSession.Open(new Rig { Devices = devices }, ConsoleLibrary.BuiltInLibrary);

    private static async Task<RigConnections> Start(FakeMidiApi api, RigSession rig)
    {
        var connections = new RigConnections(api, "test");
        await connections.Update(rig, TestContext.Current.CancellationToken);
        Assert.True(await connections.Start(TestContext.Current.CancellationToken));
        return connections;
    }

    // the endpoint changes are handled on their own: wait until the expected status
    private static async Task WaitFor(RigConnections connections, string alias, DeviceConnectionStatus status)
    {
        for (var tries = 0; tries < 200 && connections.Find(alias)?.Status != status; tries++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(status, connections.Find(alias)?.Status);
    }

    [Fact]
    public async Task Start_ConnectsTheDevicesWhoseEndpointIsThere()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        await using var connections = await Start(api, Open(
            Gp8("gp8", new EndpointReference(Port, Group(2))),
            Gp8("spare"),
            Gp8("away", new EndpointReference("Elsewhere"))));

        Assert.Equal(["gp8", "spare", "away"], connections.Devices.Select(device => device.Alias));
        Assert.Equal(DeviceConnectionStatus.Online, connections.Find("gp8")!.Status);
        Assert.Equal(Port, connections.Find("gp8")!.EndpointInfo!.Name);
        Assert.Equal(DeviceConnectionStatus.NoEndpoint, connections.Find("spare")!.Status);
        Assert.Equal(DeviceConnectionStatus.EndpointNotFound, connections.Find("away")!.Status);
        Assert.Single(api.Connections);
    }

    [Fact]
    public async Task Start_WithoutTheMidiService_ReturnsFalse()
    {
        var connections = new RigConnections(new FakeMidiApi { IsAvailable = false }, "test");

        Assert.False(await connections.Start(TestContext.Current.CancellationToken));
        Assert.False(connections.IsStarted);
        Assert.Empty(connections.Devices);
    }

    [Fact]
    public async Task EndpointAdded_ConnectsTheDeviceThatWaitsForIt()
    {
        var api = new FakeMidiApi();
        await using var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port))));
        Assert.Equal(DeviceConnectionStatus.EndpointNotFound, connections.Find("gp8")!.Status);

        api.Add(FakeMidiApi.Endpoint(Port));

        await WaitFor(connections, "gp8", DeviceConnectionStatus.Online);
    }

    [Fact]
    public async Task ConnectionLost_WaitsForTheEndpointToComeBack()
    {
        var api = new FakeMidiApi();
        var endpoint = FakeMidiApi.Endpoint(Port);
        api.Endpoints.Add(endpoint);
        await using var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port))));

        api.Connections[0].Fault("cable pulled");
        await WaitFor(connections, "gp8", DeviceConnectionStatus.Lost);
        Assert.Equal("cable pulled", connections.Find("gp8")!.Message);
        Assert.True(api.Connections[0].IsDisposed);

        api.Remove(endpoint);
        await WaitFor(connections, "gp8", DeviceConnectionStatus.EndpointNotFound);
        api.Add(endpoint);
        await WaitFor(connections, "gp8", DeviceConnectionStatus.Online);
        Assert.Equal(2, api.Connections.Count);
    }

    [Fact]
    public async Task Update_AnotherGroup_ConnectsAgain_TheSameStays()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        var device = Gp8(endpoint: new EndpointReference(Port));
        var rig = Open(device);
        await using var connections = await Start(api, rig);

        await connections.Update(RigSession.Open(rig.Rig, rig.Library, rig), TestContext.Current.CancellationToken);
        Assert.Single(api.Connections);

        device.Endpoint = new EndpointReference(Port, Group(3));
        await connections.Update(RigSession.Open(rig.Rig, rig.Library, rig), TestContext.Current.CancellationToken);

        Assert.Equal(2, api.Connections.Count);
        Assert.True(api.Connections[0].IsDisposed);
        Assert.Equal(DeviceConnectionStatus.Online, connections.Find("gp8")!.Status);
        Assert.Equal(Group(3), connections.Find("gp8")!.Endpoint!.Groups);
    }

    [Fact]
    public async Task Stop_ClosesTheConnectionsAndTheSession()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port))));

        await connections.Stop();

        Assert.False(connections.IsStarted);
        Assert.Empty(connections.Devices);
        Assert.Empty(connections.Endpoints);
        Assert.True(api.Connections[0].IsDisposed);
        Assert.True(api.Sessions[0].IsDisposed);
        Assert.True(api.Observer!.IsDisposed);
    }

    [Fact]
    public async Task RequestIdentity_ReturnsTheReplies_OnTheGroupOfTheDevice()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        // Roland (0x41), family 0x0013, member 0x0002, revision 01 00 00 00, device id 0x10
        api.Responder = (_, group, sysEx) => group == 2 && sysEx is [0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7]
            ? [0xF0, 0x7E, 0x10, 0x06, 0x02, 0x41, 0x13, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0xF7]
            : null;
        await using var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port, Group(2)))));

        var replies = await connections.RequestIdentity("gp8", TestContext.Current.CancellationToken);

        var reply = Assert.Single(replies);
        Assert.Equal(0x41, reply.ManufacturerId);
        Assert.Equal(0x13, reply.DeviceFamily);
        Assert.Equal(0x10, reply.DeviceId);
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(1, 1, 0)]
    public async Task RequestIdentity_DeviceCabledToDifferentGroups_RepliesOnTheReceiveGroup(byte send, byte receive, int expected)
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        api.Responder = (_, group, sysEx) => group == 1 && sysEx is [0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7]
            ? [0xF0, 0x7E, 0x10, 0x06, 0x02, 0x41, 0x13, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0xF7]
            : null;
        api.ReplyGroup = _ => 2;
        var groups = new MidiGroupPair(new MidiGroup(send), new MidiGroup(receive));
        await using var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port, groups))));

        var replies = await connections.RequestIdentity("gp8", TestContext.Current.CancellationToken);

        Assert.Equal(expected, replies.Count);
        Assert.Equal(send, Assert.Single(api.Connections[^1].Sent).Group);
    }

    [Fact]
    public async Task ProbeData_TheDeviceAnswers_NoValueChanges()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        api.Responder = (_, _, sysEx) => sysEx is [0xF0, 0x41, 0x00, 0x13, 0x11, ..] ? PatchAt(sysEx[5], sysEx[6]) : null;
        var rig = Open(Gp8(endpoint: new EndpointReference(Port)));
        await using var connections = await Start(api, rig);

        var probe = await connections.ProbeData("gp8", TestContext.Current.CancellationToken);

        Assert.True(probe.Result.IsComplete);
        Assert.Equal(0, rig.Devices["gp8"].Count);
    }

    [Fact]
    public async Task ProbeData_NotOnline_Throws()
    {
        await using var connections = await Start(new FakeMidiApi(), Open(Gp8(endpoint: new EndpointReference(Port))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.ProbeData("gp8", TestContext.Current.CancellationToken));
    }

    private static ParameterReference PatchParameter(string field, int patch)
        => new("gp8", $"AddressMap.Memory.Patch.{field}", new InstancePath([patch]));

    // the requests (RQ1) sent to the GP-8
    private static int Requests(FakeMidiApi api)
        => api.Connections.SelectMany(connection => connection.Sent).Count(sent => sent.SysEx is [0xF0, 0x41, 0x00, 0x13, 0x11, ..]);

    [Fact]
    public async Task Receive_TheParameters_AreRequestedOnceAndSetWithTheirOrigin()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        api.Responder = (_, _, sysEx) => sysEx is [0xF0, 0x41, 0x00, 0x13, 0x11, ..] ? DataAt(sysEx[5], sysEx[6], sysEx[8]) : null;
        var rig = Open(Gp8(endpoint: new EndpointReference(Port)));
        await using var connections = await Start(api, rig);
        var state = rig.Devices["gp8"];
        var origins = new List<ValueOrigin>();
        state.ValueChanged += (_, change) => origins.Add(change.Origin);

        var result = await connections.Receive("gp8", ExchangeNames.RequestData,
            [PatchParameter("EffectSwiches1.PhaserOn", 2), PatchParameter("EffectSwiches1.DelayOn", 2), PatchParameter("MasterVolume", 2),
                PatchParameter("EffectSwiches1.PhaserOn", 2)],
            data: null, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(3, result.Parameters.Count);
        // the two switches are bits of one address: the second came with the first
        Assert.Equal(2, result.Transfers.Count);
        Assert.Equal(2, Requests(api));
        Assert.True(state.TryGetValue(PatchParameter("EffectSwiches1.DelayOn", 2), out _));
        Assert.True(state.TryGetValue(PatchParameter("MasterVolume", 2), out _));
        Assert.NotEmpty(origins);
        Assert.All(origins, origin => Assert.Equal(ValueOrigin.Device, origin));
    }

    [Fact]
    public async Task Receive_Again_IsAnsweredByTheSession_UnlessFreshFromTheDevice()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        api.Responder = (_, _, sysEx) => sysEx is [0xF0, 0x41, 0x00, 0x13, 0x11, ..] ? DataAt(sysEx[5], sysEx[6], sysEx[8]) : null;
        await using var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port))));
        ParameterReference[] parameters = [PatchParameter("MasterVolume", 1)];
        await connections.Receive("gp8", ExchangeNames.RequestData, parameters, data: null, TestContext.Current.CancellationToken);

        var again = await connections.Receive("gp8", ExchangeNames.RequestData, parameters, data: null, TestContext.Current.CancellationToken);
        Assert.True(again.IsComplete);
        Assert.Equal(1, Requests(api));

        var fresh = await connections.Receive("gp8", ExchangeNames.RequestData, parameters, SessionData.Device, TestContext.Current.CancellationToken);
        Assert.True(fresh.IsComplete);
        Assert.Equal(2, Requests(api));
    }

    [Fact]
    public async Task Receive_TheDeviceDoesNotAnswer_EndsAtTheFirstParameter()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        var rig = Open(Gp8(endpoint: new EndpointReference(Port)));
        await using var connections = await Start(api, rig);

        var result = await connections.Receive("gp8", ExchangeNames.RequestData,
            [PatchParameter("MasterVolume", 0), PatchParameter("MasterVolume", 1), PatchParameter("MasterVolume", 2)],
            data: null, TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(PatchParameter("MasterVolume", 0), result.Failed!.Value.Parameter);
        Assert.Equal(BlockStatus.TimedOut, result.Failed!.Value.Result.Blocks[0].Status);
        Assert.Equal(2, result.NotRequested);
        Assert.Equal(0, rig.Devices["gp8"].Count);
    }

    [Fact]
    public async Task Receive_WithAnExchangeTheFileDoesNotHave_Throws()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        await using var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port))));

        await Assert.ThrowsAsync<ExchangeException>(() => connections.Receive("gp8", "noSuchExchange",
            [PatchParameter("MasterVolume", 0)], data: null, TestContext.Current.CancellationToken));
        Assert.Equal(0, Requests(api));
    }

    [Fact]
    public async Task Receive_NotOnline_Throws()
    {
        await using var connections = await Start(new FakeMidiApi(), Open(Gp8(endpoint: new EndpointReference(Port))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Receive("gp8", ExchangeNames.RequestData,
            [PatchParameter("MasterVolume", 0)], data: null, TestContext.Current.CancellationToken));
    }

    // the data of the DT1s (data sets) sent to the GP-8, in order
    private static List<byte[]> DataSent(FakeMidiApi api)
        => [.. api.Connections.SelectMany(connection => connection.Sent)
            .Where(sent => sent.SysEx is [0xF0, 0x41, 0x00, 0x13, 0x12, ..])
            .Select(sent => sent.SysEx[7..^2])];

    // a GP-8 that answers requests, with MasterVolume of patch 2 received into the device state
    private static async Task<(FakeMidiApi Api, RigSession Rig, RigConnections Connections)> StartReceived()
    {
        var api = new FakeMidiApi();
        api.Endpoints.Add(FakeMidiApi.Endpoint(Port));
        api.Responder = (_, _, sysEx) => sysEx is [0xF0, 0x41, 0x00, 0x13, 0x11, ..] ? DataAt(sysEx[5], sysEx[6], sysEx[8]) : null;
        var rig = Open(Gp8(endpoint: new EndpointReference(Port)));
        var connections = await Start(api, rig);
        await connections.Receive("gp8", ExchangeNames.RequestData, [PatchParameter("MasterVolume", 2)], data: null, TestContext.Current.CancellationToken);
        return (api, rig, connections);
    }

    // the user sets MasterVolume of patch 2
    private static void SetVolume(RigSession rig, int value)
    {
        var state = rig.Devices["gp8"];
        var parameter = PatchParameter("MasterVolume", 2);
        Assert.True(state.TryGetValue(parameter, out var known));
        state.SetValue(parameter.Path, parameter.Instance, known with { Value = Convert.ChangeType(value, known.Value!.GetType()) }, ValueOrigin.User);
    }

    // waits until the condition holds, or fails
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var tries = 0; tries < 300 && !condition(); tries++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [Fact]
    public async Task Send_AChangedParameter_WritesItsValue()
    {
        var (api, rig, connections) = await StartReceived();
        await using var _ = connections;
        var results = new List<SendResult>();
        connections.Sent += (_, result) => { lock (results) results.Add(result); };

        SetVolume(rig, 42);
        connections.Send("gp8", ExchangeNames.SendData, PatchParameter("MasterVolume", 2));

        await WaitUntil(() => { lock (results) return results.Count == 1; });
        Assert.True(results[0].IsComplete);
        Assert.Equal([42], Assert.Single(DataSent(api)));
    }

    [Fact]
    public async Task Send_ChangesWhileSending_SendTheLatestValue_NotEveryStep()
    {
        var (api, rig, connections) = await StartReceived();
        await using var _ = connections;

        for (var value = 1; value <= 10; value++)
        {
            SetVolume(rig, value);
            connections.Send("gp8", ExchangeNames.SendData, PatchParameter("MasterVolume", 2));
        }

        await WaitUntil(() => DataSent(api) is { Count: > 0 } sent && sent[^1] is [10]);
        // the first send may start before the next change; the others wait in the queue as one
        Assert.InRange(DataSent(api).Count, 1, 2);
    }

    [Fact]
    public async Task Send_WithAnExchangeTheFileDoesNotHave_ReportsTheProblems()
    {
        var (api, rig, connections) = await StartReceived();
        await using var _ = connections;
        SendResult? reported = null;
        connections.Sent += (_, result) => reported = result;

        SetVolume(rig, 42);
        connections.Send("gp8", "noSuchExchange", PatchParameter("MasterVolume", 2));

        await WaitUntil(() => reported is not null);
        Assert.False(reported!.IsComplete);
        Assert.IsType<ExchangeException>(reported.Error);
        Assert.Empty(DataSent(api));
    }

    [Fact]
    public async Task Send_NotOnline_Throws()
    {
        await using var connections = await Start(new FakeMidiApi(), Open(Gp8(endpoint: new EndpointReference(Port))));

        Assert.Throws<InvalidOperationException>(() => connections.Send("gp8", ExchangeNames.SendData, PatchParameter("MasterVolume", 0)));
    }

    // a DT1 with the requested addresses of a patch (the first patch of the dump, as every patch), with its checksum
    private static byte[] DataAt(byte msb, byte lsb, byte size)
    {
        // the dump's DT1: F0 41 00 13 12, the address (2), the 0x32 parameters of the patch, the checksum, F7
        var offset = lsb & 0x3F;
        byte[] message = [0xF0, 0x41, 0x00, 0x13, 0x12, msb, lsb, .. Dump.AsSpan(7 + offset, size), 0x00, 0xF7];
        var sum = message[5..^2].Sum(value => value);
        message[^2] = (byte)((128 - sum % 128) % 128);
        return message;
    }

    // the first patch of the dump as a DT1 at the requested address, with its checksum
    private static byte[] PatchAt(byte msb, byte lsb)
    {
        var message = Dump[..59];
        (message[5], message[6]) = (msb, lsb);
        var sum = message[5..^2].Sum(value => value);
        message[^2] = (byte)((128 - sum % 128) % 128);
        return message;
    }
}
