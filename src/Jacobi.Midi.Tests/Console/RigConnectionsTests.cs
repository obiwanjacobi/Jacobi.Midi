using Jacobi.Midi.Console.Core.Connections;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Device.Exchange;
using Xunit;

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
            Gp8("gp8", new EndpointReference(Port, 2)),
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

        device.Endpoint = new EndpointReference(Port, 3);
        await connections.Update(RigSession.Open(rig.Rig, rig.Library, rig), TestContext.Current.CancellationToken);

        Assert.Equal(2, api.Connections.Count);
        Assert.True(api.Connections[0].IsDisposed);
        Assert.Equal(DeviceConnectionStatus.Online, connections.Find("gp8")!.Status);
        Assert.Equal(3, connections.Find("gp8")!.Endpoint!.Group);
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
        await using var connections = await Start(api, Open(Gp8(endpoint: new EndpointReference(Port, 2))));

        var replies = await connections.RequestIdentity("gp8", TestContext.Current.CancellationToken);

        var reply = Assert.Single(replies);
        Assert.Equal(0x41, reply.ManufacturerId);
        Assert.Equal(0x13, reply.DeviceFamily);
        Assert.Equal(0x10, reply.DeviceId);
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
