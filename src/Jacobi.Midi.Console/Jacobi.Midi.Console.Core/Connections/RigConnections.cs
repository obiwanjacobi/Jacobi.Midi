using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Messages;

namespace Jacobi.Midi.Console.Core.Connections;

/// <summary>
/// The MIDI connections of the devices of a rig: one MIDI session, the endpoints it can reach, and a
/// <see cref="DeviceSession"/> per device whose endpoint (by name, see <see cref="EndpointReference"/>) is there.
/// Started explicitly (<see cref="Start"/>); until then nothing is connected. The connections follow the rig
/// (<see cref="Update"/>: a device whose endpoint, device values or schemas changed connects again) and the endpoints:
/// a device whose endpoint appears connects, one whose endpoint disappears or whose connection is lost waits for it to
/// come back. Why a device is not online is in its <see cref="DeviceConnection"/>; nothing is thrown for it.
/// <see cref="Changed"/> may be raised on a background thread.
/// </summary>
public sealed class RigConnections(IMidiApi api, string sessionName) : IAsyncDisposable
{
    /// <summary>How long <see cref="RequestIdentity"/> waits for the replies.</summary>
    public static TimeSpan IdentityWait { get; } = TimeSpan.FromSeconds(1);

    // the changes (start, stop, rig, endpoints, lost connections) are made one at a time
    private readonly SemaphoreSlim _gate = new(1, 1);
    // guards the snapshots the properties return
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Link> _links = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MidiEndpointInfo> _endpoints = [];
    private RigSession? _rig;
    private IMidiSession? _session;
    private IMidiEndpointObserver? _observer;
    private MessageExchangeFactory? _factory;
    private Serializer? _factorySerializer;
    private bool _isStarted;

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsStarted
    {
        get
        {
            lock (_lock)
                return _isStarted;
        }
    }

    /// <summary>The endpoints that can be reached; none until started.</summary>
    public IReadOnlyList<MidiEndpointInfo> Endpoints
    {
        get
        {
            lock (_lock)
                return _endpoints;
        }
    }

    /// <summary>The connections of the devices, in the order of the rig; none until started.</summary>
    public IReadOnlyList<DeviceConnection> Devices
    {
        get
        {
            lock (_lock)
                return _rig is null ? [] : [.. _rig.Rig.Devices.Select(device => _links.GetValueOrDefault(device.Alias)?.Connection).OfType<DeviceConnection>()];
        }
    }

    /// <summary>The connection of a device; null when not started or the device is not in the rig.</summary>
    public DeviceConnection? Find(string alias)
    {
        lock (_lock)
            return _links.GetValueOrDefault(alias)?.Connection;
    }

    /// <summary>The endpoints or the connection of a device changed, or the connections started or stopped.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Opens the MIDI session, watches the endpoints and connects the devices of the rig given to <see cref="Update"/>.
    /// Returns false when the MIDI service is not available.
    /// </summary>
    public async Task<bool> Start(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsStarted)
                return true;
            if (!await api.EnsureIsAvailable())
                return false;
            try
            {
                _session = await api.CreateSession(new MidiSessionOptions { Name = sessionName }, ct);
                // the handler can be called before this returns: it waits for the gate
                _observer = await api.WatchMidiEndpoints(OnEndpointChanged, ct);
            }
            catch
            {
                await Close();
                throw;
            }
            lock (_lock)
            {
                _isStarted = true;
                _endpoints = SortedEndpoints(_observer.Endpoints);
            }
            await Reconcile(ct);
        }
        finally
        {
            _gate.Release();
        }
        return true;
    }

    /// <summary>Closes the connections, the session and the endpoint watch.</summary>
    public async Task Stop()
    {
        await _gate.WaitAsync();
        try
        {
            await Close();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The rig was opened (again): when started, its devices are connected; a device whose endpoint, device values or
    /// schemas changed connects again, a device that left the rig is disconnected.
    /// </summary>
    public async Task Update(RigSession? rig, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            lock (_lock)
                _rig = rig;
            if (IsStarted)
                await Reconcile(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sends a universal Identity Request (to all devices) on the endpoint and groups of the device, over a connection of
    /// its own, and returns the replies that arrive within <see cref="IdentityWait"/>. Needs the endpoint, not the
    /// device's schemas.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not started, or the device's endpoint is not found.</exception>
    public async Task<IReadOnlyList<IdentityReplyMessage>> RequestIdentity(string alias, CancellationToken ct)
    {
        IMidiSession session;
        MidiEndpointInfo endpoint;
        MidiGroupPair groups;
        lock (_lock)
        {
            session = _session ?? throw new InvalidOperationException("MIDI is not started.");
            var connection = _links.GetValueOrDefault(alias)?.Connection;
            endpoint = connection?.EndpointInfo ?? throw new InvalidOperationException($"The endpoint of '{alias}' is not found.");
            groups = connection.Endpoint!.Groups;
        }
        return await DeviceIdentity.Request(session, endpoint.Id, groups, DeviceIdentity.AllDevices, IdentityWait, ct);
    }

    /// <summary>
    /// Checks that the device answers with its schemas and device values: requests the first parameter of its map
    /// (widened to its atomic group, as the exchange does) and discards it (<see cref="DeviceSession.Probe"/>). Nothing
    /// changes: not the device state, not the session's state.
    /// </summary>
    /// <exception cref="InvalidOperationException">The device is not online, has no state, or its map has no request for the range.</exception>
    public async Task<DataProbeResult> ProbeData(string alias, CancellationToken ct)
    {
        DeviceSession session;
        DeviceState state;
        lock (_lock)
        {
            session = _links.GetValueOrDefault(alias)?.Session ?? throw new InvalidOperationException($"'{alias}' is not online.");
            state = _rig?.Devices.GetValueOrDefault(alias) ?? throw new InvalidOperationException($"'{alias}' has no parameters: see the problems of the rig.");
        }
        var (path, instance) = FirstParameter(state.Parameters)
            ?? throw new InvalidOperationException($"The map of '{alias}' has no parameters.");
        var range = state.Plan.CreateRange(path, instance, path, instance);
        return new DataProbeResult(path, instance, await session.Probe(range, ct));
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
        _gate.Dispose();
    }

    // the first field of the parameter tree, in the first item of each repeat around it
    private static (string Path, InstancePath Instance)? FirstParameter(ParameterNode node, int repeats = 0)
    {
        if (node.Kind == ParameterNodeKind.Field)
            return (node.Path, new InstancePath(Enumerable.Repeat(0, repeats)));
        var inner = node.Kind == ParameterNodeKind.Repeat ? repeats + 1 : repeats;
        return node.Children.Select(child => FirstParameter(child, inner)).FirstOrDefault(found => found is not null);
    }

    // under the gate: every device of the rig gets the connection it should have now
    private async Task Reconcile(CancellationToken ct)
    {
        var rig = _rig;
        var devices = rig?.Rig.Devices ?? [];
        foreach (var gone in _links.Values.Where(link => !devices.Any(device => String.Equals(device.Alias, link.Alias, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            await CloseSession(gone);
            lock (_lock)
                _links.Remove(gone.Alias);
        }
        Raise();

        var serializer = rig?.Library.Serializer;
        foreach (var device in devices)
        {
            var link = _links.GetValueOrDefault(device.Alias);
            if (link is not null && !link.IsFor(device, serializer))
            {
                await CloseSession(link);
                link = null;
            }
            if (link is null)
            {
                link = new Link(device, serializer);
                lock (_lock)
                    _links[device.Alias] = link;
            }
            await Connect(link, serializer, ct);
        }
    }

    // under the gate: connects a device that should be and can be connected; one that is online stays when its endpoint is still there
    private async Task Connect(Link link, Serializer? serializer, CancellationToken ct)
    {
        if (link.Endpoint is not { } reference)
        {
            SetConnection(link, DeviceConnectionStatus.NoEndpoint);
            return;
        }
        var endpoint = FindEndpoint(reference.Name);
        if (link.Session is not null)
        {
            // online, or lost: then its Disconnected handler makes it Lost, with the reason
            if (endpoint is not null && String.Equals(endpoint.Id, link.Connection.EndpointInfo?.Id, StringComparison.OrdinalIgnoreCase))
                return;
            // the endpoint went away (or is another one now): the connection is of no use
            await CloseSession(link);
            link.IsWaiting = true;
        }
        if (endpoint is null)
        {
            link.IsWaiting = true;
            SetConnection(link, DeviceConnectionStatus.EndpointNotFound);
            return;
        }
        if (!link.IsWaiting)
            return;
        if (serializer is null || _session is null)
        {
            link.IsWaiting = false;
            SetConnection(link, DeviceConnectionStatus.Failed, endpoint, "the schemas do not compile; see the problems of the rig.");
            return;
        }

        link.IsWaiting = false;
        SetConnection(link, DeviceConnectionStatus.Connecting, endpoint);
        try
        {
            if (_factory is null || _factorySerializer != serializer)
                (_factory, _factorySerializer) = (new MessageExchangeFactory(_session, serializer, _rig?.Library.Exchanges ?? new ExchangeDefinitions()), serializer);
            var options = new MessageExchangeOptions { Groups = reference.Groups };
            var session = await _factory.CreateSession(endpoint.Id, link.DeviceValues, options, null, ct);
            link.Session = session;
            session.Disconnected += (_, args) => _ = OnDisconnected(link, session, args);
            if (session.IsConnected)
                SetConnection(link, DeviceConnectionStatus.Online, endpoint);
            else
                await Lose(link, endpoint, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the MIDI backend fails with its own exception types
            SetConnection(link, DeviceConnectionStatus.Failed, endpoint, exception.Message);
        }
    }

    private async Task OnDisconnected(Link link, DeviceSession session, MidiConnectionStateChangedArgs args)
    {
        await _gate.WaitAsync();
        try
        {
            if (link.Session == session)
                await Lose(link, link.Connection.EndpointInfo, args.Error?.Message);
        }
        catch (Exception exception)
        {
            // not thrown on the backend's thread: the device shows it
            SetConnection(link, DeviceConnectionStatus.Failed, link.Connection.EndpointInfo, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    // under the gate: the connection is lost; the device connects again when its endpoint is added or updated
    private async Task Lose(Link link, MidiEndpointInfo? endpoint, string? reason)
    {
        await CloseSession(link);
        SetConnection(link, DeviceConnectionStatus.Lost, endpoint, reason);
    }

    // called by the backend on a background thread: handled one at a time with the other changes
    private void OnEndpointChanged(MidiEndpointChangedArgs args) => _ = HandleEndpointChange(args);

    private async Task HandleEndpointChange(MidiEndpointChangedArgs args)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsStarted || _observer is null)
                return;
            lock (_lock)
                _endpoints = SortedEndpoints(_observer.Endpoints);
            // a device that lost its endpoint, or failed on it, tries again when the endpoint comes (back)
            if (args.ChangeType != MidiEndpointChangeType.Removed)
            {
                foreach (var link in _links.Values.Where(link => link.Session is null
                    && String.Equals(link.Endpoint?.Name, args.Endpoint.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    link.IsWaiting = true;
                }
            }
            await Reconcile(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // not thrown on the backend's thread: every device shows it
            foreach (var link in _links.Values.Where(link => !link.Connection.IsOnline))
                SetConnection(link, DeviceConnectionStatus.Failed, link.Connection.EndpointInfo, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    // under the gate
    private async Task Close()
    {
        foreach (var link in _links.Values.ToList())
            await CloseSession(link);
        var (session, observer) = (_session, _observer);
        (_session, _observer, _factory, _factorySerializer) = (null, null, null, null);
        lock (_lock)
        {
            _links.Clear();
            _endpoints = [];
            _isStarted = false;
        }
        try
        {
            if (observer is not null)
                await observer.DisposeAsync();
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
        Raise();
    }

    private static async Task CloseSession(Link link)
    {
        var session = link.Session;
        link.Session = null;
        if (session is not null)
            await session.DisposeAsync();
    }

    private MidiEndpointInfo? FindEndpoint(string name)
        => Endpoints.FirstOrDefault(endpoint => String.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<MidiEndpointInfo> SortedEndpoints(IEnumerable<MidiEndpointInfo> endpoints)
        => [.. endpoints.OrderBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)];

    private void SetConnection(Link link, DeviceConnectionStatus status, MidiEndpointInfo? endpoint = null, string? message = null)
    {
        var connection = new DeviceConnection(link.Alias, link.Endpoint, status, endpoint, message);
        lock (_lock)
        {
            if (connection == link.Connection)
                return;
            link.Connection = connection;
        }
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    /// <summary>
    /// The connection of one device: what it was made for (endpoint, device values, schemas), its session and status.
    /// </summary>
    private sealed class Link
    {
        private readonly Serializer? _serializer;

        public Link(DeviceInstance device, Serializer? serializer)
        {
            Alias = device.Alias;
            Endpoint = device.Endpoint;
            DeviceValues = new DeviceValues();
            foreach (var (name, value) in device.DeviceValues)
                DeviceValues[name] = value;
            _serializer = serializer;
            Connection = new DeviceConnection(Alias, Endpoint, DeviceConnectionStatus.EndpointNotFound);
        }

        public string Alias { get; }

        public EndpointReference? Endpoint { get; }

        public DeviceValues DeviceValues { get; }

        public DeviceSession? Session { get; set; }

        public DeviceConnection Connection { get; set; }

        /// <summary>The device connects when its endpoint is found; false after it was lost or failed, until its endpoint changes.</summary>
        public bool IsWaiting { get; set; } = true;

        /// <summary>True when the device is still connected the same way: same endpoint, device values and schemas.</summary>
        public bool IsFor(DeviceInstance device, Serializer? serializer)
            => device.Endpoint == Endpoint
                && serializer == _serializer
                && device.DeviceValues.Count == DeviceValues.Count
                && device.DeviceValues.All(value => DeviceValues.TryGet(value.Key, out var known) && known == value.Value);
    }
}

/// <summary>
/// The outcome of <see cref="RigConnections.ProbeData"/>: the parameter that was requested (the device sends its
/// atomic group) and the result per block.
/// </summary>
public sealed record DataProbeResult(string Path, InstancePath Instance, TransferResult Result);
