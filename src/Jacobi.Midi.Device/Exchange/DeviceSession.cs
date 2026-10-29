using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Where a transfer of a <see cref="DeviceSession"/> takes the values it does not get from the client.
/// </summary>
public enum SessionData
{
    /// <summary>The session's state of the device: only what the session does not hold is requested from the device.</summary>
    Session,
    /// <summary>Always a fresh copy from the device; the session's state is updated with it.</summary>
    Device,
}

/// <summary>
/// Runtime settings of a <see cref="DeviceSession"/>.
/// </summary>
public sealed record DeviceSessionOptions
{
    /// <summary>The default for the transfers that do not give their own <see cref="SessionData"/>.</summary>
    public SessionData Data { get; init; } = SessionData.Session;
}

/// <summary>
/// How the state of a <see cref="DeviceSession"/> changed.
/// </summary>
public enum DeviceStateChange
{
    /// <summary>Data from the device: requested, or sent by the device on its own.</summary>
    Received,
    /// <summary>The client's values, sent to the device.</summary>
    Sent,
    /// <summary>The values are no longer known (invalidated, or a send that did not complete).</summary>
    Invalidated,
}

/// <summary>
/// The nodes of the state of a <see cref="DeviceSession"/> that changed, and how.
/// </summary>
public sealed class DeviceStateChangedEventArgs(PlanRange nodes, DeviceStateChange change) : EventArgs
{
    /// <summary>The nodes that changed (a range of the map plan; it may be wider than the nodes a client asked for).</summary>
    public PlanRange Nodes { get; } = nodes;
    public DeviceStateChange Change { get; } = change;
}

/// <summary>
/// Works with one connected device for as long as its connection lasts, and keeps the state of the device: the values
/// received from it, sent to it, and sent by the device on its own. The client only gives the nodes it is interested
/// in (a <see cref="PlanRange"/> of any address map of the device); the session completes the atomic groups from its
/// state and requests from the device only what it does not hold (see <see cref="SessionData"/>).
/// When the connection is lost the session is disconnected for good: every call throws, and the client disposes it
/// and creates a new session. Disposing it closes the connection.
/// </summary>
public sealed class DeviceSession : IAsyncDisposable
{
    private readonly MessageExchange _exchange;
    private readonly DeviceSessionOptions _options;
    private readonly Lock _lock = new();
    private readonly Dictionary<AddressMap, MapState> _states = [];
    private readonly CancellationTokenSource _listening = new();
    private readonly List<Task> _listeners = [];
    private bool _connected;
    private bool _disposed;

    internal DeviceSession(MessageExchange exchange, DeviceSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(options);
        _exchange = exchange;
        _options = options;
        _exchange.ConnectionStateChanged += OnConnectionStateChanged;
        _connected = exchange.ConnectionState == MidiConnectionState.Open;
    }

    /// <summary>False once the connection to the device is lost (or the session is disposed); the session cannot be used anymore.</summary>
    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _connected && !_disposed;
            }
        }
    }

    /// <summary>Raised once, when the connection to the device is lost; may be raised on a background thread.</summary>
    public event EventHandler<MidiConnectionStateChangedArgs>? Disconnected;

    /// <summary>Raised when the state of the device changed; may be raised on a background thread (data the device sends on its own).</summary>
    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    /// <inheritdoc cref="Receive(PlanRange, IValueSink, SessionData?, IProgress{BlockResult}?, CancellationToken)"/>
    public Task<TransferResult> Receive(PlanRange range, IValueSink sink, CancellationToken ct)
        => Receive(range, sink, null, null, ct);

    /// <summary>
    /// Gives the values of the range (widened to the atomic groups it overlaps) to the sink. Per block, the values come
    /// from the session's state when it holds them all and <paramref name="data"/> is <see cref="SessionData.Session"/>;
    /// otherwise the block is requested from the device and the state is updated. Blocks that are not answered, or are
    /// rejected, are reported in the result (and to the progress), not thrown.
    /// </summary>
    /// <param name="data">Where the values come from; null: the session's default (<see cref="DeviceSessionOptions.Data"/>).</param>
    /// <exception cref="InvalidOperationException">The session is disconnected, or no request of the map can ask for the range.</exception>
    public async Task<TransferResult> Receive(PlanRange range, IValueSink sink, SessionData? data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(sink);
        ThrowIfNotConnected();

        var map = _exchange.MapOf(range);
        var state = StateOf(map);
        var fresh = (data ?? _options.Data) == SessionData.Device;

        var results = new List<BlockResult>();
        foreach (var block in _exchange.RequestBlocks(map, map.WidenToAtomic(range)))
        {
            // the device never transmits write-only fields
            IReadOnlyList<BlockResult> done = fresh || !Holds(map, block.Nodes, access => access != NodeAccess.Write, state.Has)
                ? await Fetch(state, block, ct)
                : [new BlockResult(block, BlockStatus.Completed)];
            foreach (var result in done)
            {
                if (result.Status == BlockStatus.Completed)
                {
                    map.CopyValues(result.Block.Nodes, state, sink);
                }
                results.Add(result);
                progress?.Report(result);
            }
        }
        return new TransferResult(results);
    }

    /// <inheritdoc cref="Send(PlanRange, IValueSource, SessionData?, IProgress{BlockResult}?, CancellationToken)"/>
    public Task<TransferResult> Send(PlanRange range, IValueSource source, CancellationToken ct)
        => Send(range, source, null, null, ct);

    /// <summary>
    /// Sends the values of the range from the source. The source only supplies the range; an atomic group the range
    /// only partly covers is completed from the session's state, and what the state does not hold is requested from the
    /// device first (with <see cref="SessionData.Device"/>: always requested). When that fails, nothing is sent and the
    /// result holds the blocks that could not be received. A sent block updates the state; a block that did not
    /// complete is invalidated (the device's values are no longer known).
    /// </summary>
    /// <param name="data">Where the values of the atomic groups outside the range come from; null: the session's default (<see cref="DeviceSessionOptions.Data"/>).</param>
    /// <exception cref="InvalidOperationException">The session is disconnected, or an atomic group must be completed but no request of the map can ask for it.</exception>
    public async Task<TransferResult> Send(PlanRange range, IValueSource source, SessionData? data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfNotConnected();

        var map = _exchange.MapOf(range);
        var state = StateOf(map);
        var fresh = (data ?? _options.Data) == SessionData.Device;

        // exactly the client's values of the range
        var client = new FieldValueStore();
        map.CopyValues(range, source, client);
        bool FromClient(string path, InstancePath instance) => client.TryGet(path, instance, out _);
        bool Known(string path, InstancePath instance) => FromClient(path, instance) || (!fresh && state.Has(path, instance));

        // complete the atomic groups: the fields that are sent (not read-only) and can be received (not write-only)
        var widened = map.WidenToAtomic(range);
        static bool Completes(NodeAccess access) => access == NodeAccess.ReadWrite;
        if (!Holds(map, widened, Completes, Known))
        {
            var failed = new List<BlockResult>();
            foreach (var block in _exchange.RequestBlocks(map, widened).Where(block => !Holds(map, block.Nodes, Completes, Known)))
            {
                failed.AddRange((await Fetch(state, block, ct)).Where(result => result.Status != BlockStatus.Completed));
            }
            if (failed.Count > 0)
            {
                return new TransferResult(failed);
            }
        }

        // the client's values over the state; a read-only field is not sent, so the device keeps its own value
        var readOnly = map.FieldsOf(range)
            .Where(field => field.Layout.Access == NodeAccess.Read)
            .Select(field => (field.Node.Path.Value, field.Instance))
            .ToHashSet();
        state.Merge(client.Values.Where(value => !readOnly.Contains((value.Path, value.Instance))));

        var sent = await _exchange.Send(widened, state, progress, ct);
        foreach (var result in sent.Blocks)
        {
            if (result.Status == BlockStatus.Completed)
            {
                Raise(result.Block.Nodes, DeviceStateChange.Sent);
            }
            else
            {
                state.Remove(map, result.Block.Nodes);
                Raise(result.Block.Nodes, DeviceStateChange.Invalidated);
            }
        }
        return sent;
    }

    /// <summary>
    /// Forgets the values of the range (widened to whole addresses): the next transfer that needs them requests them
    /// from the device. Use it when the device changed them without telling (e.g. another patch was selected on the device).
    /// </summary>
    public void Invalidate(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        ThrowIfNotConnected();

        var map = _exchange.MapOf(range);
        MapState? state;
        lock (_lock)
        {
            _states.TryGetValue(map, out state);
        }
        if (state is not null)
        {
            state.Remove(map, range);
            Raise(range, DeviceStateChange.Invalidated);
        }
    }

    /// <summary>Forgets all values of the device.</summary>
    public void Clear()
    {
        ThrowIfNotConnected();

        List<MapState> states;
        lock (_lock)
        {
            states = [.. _states.Values];
        }
        foreach (var state in states)
        {
            state.Clear();
            Raise(state.Whole, DeviceStateChange.Invalidated);
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Task> listeners;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            listeners = [.. _listeners];
        }

        _exchange.ConnectionStateChanged -= OnConnectionStateChanged;
        await _listening.CancelAsync();
        try
        {
            await Task.WhenAll(listeners);
        }
        catch (OperationCanceledException)
        {
            // listening ends with the cancellation
        }
        await _exchange.DisposeAsync();
        _listening.Dispose();
    }

    // the state of a map; the first use of a map starts listening to all of it, so the state follows the device
    private MapState StateOf(AddressMap map)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(map, out var state))
            {
                state = new MapState(map.Plan.CreateRange(map.Plan.Root.Path));
                _states[map] = state;
                _listeners.Add(_exchange.Listen(state.Whole, data => OnListened(state, data), _listening.Token));
            }
            return state;
        }
    }

    private async Task<IReadOnlyList<BlockResult>> Fetch(MapState state, AddressBlock block, CancellationToken ct)
    {
        var result = await _exchange.Receive(block.Nodes, state, ct);
        foreach (var received in result.Blocks.Where(r => r.Status == BlockStatus.Completed))
        {
            Raise(received.Block.Nodes, DeviceStateChange.Received);
        }
        return result.Blocks;
    }

    // true when every field of the nodes whose access is needed is known
    private static bool Holds(AddressMap map, PlanRange nodes, Func<NodeAccess, bool> needed, Func<string, InstancePath, bool> known)
        => map.FieldsOf(nodes)
            .Where(field => needed(field.Layout.Access))
            .All(field => known(field.Node.Path.Value, field.Instance));

    private void OnListened(MapState state, ReceivedData data)
    {
        data.Decode(state);
        Raise(data.Block.Nodes, DeviceStateChange.Received);
    }

    private void Raise(PlanRange nodes, DeviceStateChange change)
        => StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(nodes, change));

    private void OnConnectionStateChanged(object? sender, MidiConnectionStateChangedArgs args)
    {
        if (args.NewState == MidiConnectionState.Open)
        {
            return;
        }
        lock (_lock)
        {
            if (!_connected)
            {
                return;
            }
            _connected = false;
        }
        _listening.Cancel();
        Disconnected?.Invoke(this, args);
    }

    private void ThrowIfNotConnected()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connected)
            {
                throw new InvalidOperationException(
                    "The connection to the device is lost: the session can no longer be used. Dispose it and create a new session.");
            }
        }
    }

    /// <summary>
    /// The state of one address map of the device: the field values by path and instance of the map plan, guarded by
    /// a lock (the device's own data arrives on the listener's flow). As a sink and a source it is the flat
    /// <see cref="FieldValueStore"/>.
    /// </summary>
    private sealed class MapState(PlanRange whole) : IValueSink, IValueSource
    {
        private readonly Lock _lock = new();
        private readonly FieldValueStore _store = new();

        /// <summary>The range of the whole map.</summary>
        public PlanRange Whole { get; } = whole;

        public bool Has(string path, InstancePath instance)
        {
            lock (_lock)
            {
                return _store.TryGet(path, instance, out _);
            }
        }

        public void Merge(IEnumerable<(string Path, InstancePath Instance, LogicalField Value)> values)
        {
            lock (_lock)
            {
                foreach (var (path, instance, value) in values)
                {
                    _store.Set(path, instance, value);
                }
            }
        }

        public void Remove(AddressMap map, PlanRange range)
        {
            lock (_lock)
            {
                foreach (var field in map.FieldsOf(range))
                {
                    _store.Remove(field.Node.Path.Value, field.Instance);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _store.Clear();
            }
        }

        public void SetField(FieldContext context, LogicalField value)
        {
            lock (_lock)
            {
                _store.SetField(context, value);
            }
        }

        public SourceResult GetField(FieldContext context)
        {
            lock (_lock)
            {
                return _store.GetField(context);
            }
        }

        // the store is flat: every scope is the state itself
        public IValueSink EnterGroup(GroupContext context) => this;
        public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
        public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
        public void ExitGroup(GroupContext context) { }
        public void ExitItem(RepeatContext context, int index, int count) { }
        public void ExitChoice(ChoiceContext context, int selectedIndex) { }
        IValueSource IValueSource.EnterGroup(GroupContext context) => this;
        public int GetCount(RepeatContext context) => ((IValueSource)_store).GetCount(context);
        public IValueSource EnterItem(RepeatContext context, int index) => this;
        public int GetSelectedIndex(ChoiceContext context) => ((IValueSource)_store).GetSelectedIndex(context);
        IValueSource IValueSource.EnterChoice(ChoiceContext context) => this;
    }
}
