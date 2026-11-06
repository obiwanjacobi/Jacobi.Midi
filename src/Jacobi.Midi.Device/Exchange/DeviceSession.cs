using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Messages;

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
/// Runtime settings of a <see cref="DeviceSession"/>. The exchanges the session runs for its own transfers are the
/// standard ones (<see cref="ExchangeNames"/>) of the exchange files of the device's map roots.
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
/// state and requests from the device only what it does not hold (see <see cref="SessionData"/>). The session gets
/// all it needs when it is created; it runs the standard exchanges (<see cref="ExchangeNames"/>) of the device and,
/// with <see cref="Execute"/>, any other exchange of its exchange files.
/// When the connection is lost the session is disconnected for good: every call throws, and the client disposes it
/// and creates a new session. Disposing it closes the connection.
/// </summary>
public sealed class DeviceSession : IAsyncDisposable
{
    private readonly MessageExchange _exchange;
    private readonly DeviceSessionOptions _options;
    private readonly Lock _lock = new();
    private readonly Dictionary<IMessageMap, IDeviceState> _states = [];
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

    /// <inheritdoc cref="RequestData(PlanRange, IValueSink, SessionData?, IProgress{BlockResult}?, CancellationToken)"/>
    public Task<TransferResult> RequestData(PlanRange range, IValueSink sink, CancellationToken ct)
        => RequestData(range, sink, null, null, ct);

    /// <summary>
    /// Gives the values of the range (widened to the atomic groups it overlaps) to the sink. Per block, the values come
    /// from the session's state when it holds them all and <paramref name="data"/> is <see cref="SessionData.Session"/>;
    /// otherwise the block is requested from the device (<see cref="ExchangeNames.RequestData"/>) and the state is
    /// updated. The first block that does not complete ends the transfer: the blocks after it are reported with the
    /// same status. Blocks are reported in the result (and to the progress), not thrown.
    /// </summary>
    /// <param name="data">Where the values come from; null: the session's default (<see cref="DeviceSessionOptions.Data"/>).</param>
    /// <exception cref="InvalidOperationException">The session is disconnected.</exception>
    /// <exception cref="ExchangeException">The device has no <see cref="ExchangeNames.RequestData"/> exchange that can run for the range.</exception>
    public Task<TransferResult> RequestData(PlanRange range, IValueSink sink, SessionData? data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(sink);
        ThrowIfNotConnected();
        return Receive(ExchangeNames.RequestData, range, sink, (data ?? _options.Data) == SessionData.Device, progress, ct);
    }

    /// <summary>
    /// Requests the range (widened to the atomic groups it overlaps) from the device and discards the values: a check
    /// that the device answers (endpoint, group, device values, schemas and exchange file). Nothing changes: not the
    /// session's state, and no listener gets the data (it belongs to the transfer).
    /// </summary>
    /// <exception cref="InvalidOperationException">The session is disconnected.</exception>
    /// <exception cref="ExchangeException">The device has no <see cref="ExchangeNames.RequestData"/> exchange that can run for the range.</exception>
    public Task<TransferResult> Probe(PlanRange range, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ThrowIfNotConnected();
        return _exchange.Execute(ExchangeNames.RequestData, range, new ExchangeData { Sink = NullSink.Instance }, ct);
    }

    /// <inheritdoc cref="SendData(PlanRange, IValueSource, SessionData?, IProgress{BlockResult}?, CancellationToken)"/>
    public Task<TransferResult> SendData(PlanRange range, IValueSource source, CancellationToken ct)
        => SendData(range, source, null, null, ct);

    /// <summary>
    /// Sends the values of the range from the source (<see cref="ExchangeNames.SendData"/>). The source only supplies
    /// the range; an atomic group the range only partly covers is completed from the session's state, and what the
    /// state does not hold is requested from the device first (with <see cref="SessionData.Device"/>: always requested).
    /// When that fails, nothing is sent and the result holds the blocks that could not be received. A sent block
    /// updates the state; a block that did not complete is invalidated (the device's values are no longer known).
    /// </summary>
    /// <param name="data">Where the values of the atomic groups outside the range come from; null: the session's default (<see cref="DeviceSessionOptions.Data"/>).</param>
    /// <exception cref="InvalidOperationException">The session is disconnected.</exception>
    /// <exception cref="ExchangeException">The device has no exchange that can run for the range (to send, or to request what completes an atomic group).</exception>
    public Task<TransferResult> SendData(PlanRange range, IValueSource source, SessionData? data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfNotConnected();
        return Send(ExchangeNames.SendData, range, source, (data ?? _options.Data) == SessionData.Device, progress, ct);
    }

    /// <summary>
    /// Runs the exchange <paramref name="name"/> of the device for the range, with the state of the session: an exchange
    /// that sends data runs as <see cref="SendData(PlanRange, IValueSource, SessionData?, IProgress{BlockResult}?, CancellationToken)"/>
    /// (with the source of <paramref name="data"/>), one that receives data always requests it from the device and
    /// updates the state (as with <see cref="SessionData.Device"/>), and one that does neither runs as it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session is disconnected.</exception>
    /// <exception cref="ExchangeException">The exchange cannot run for the range, or the source or sink it needs is not given.</exception>
    /// <exception cref="NotSupportedException">The exchange both sends and receives data.</exception>
    public Task<TransferResult> Execute(string name, PlanRange range, ExchangeData data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(data);
        ThrowIfNotConnected();

        return _exchange.DataOf(name, range) switch
        {
            (true, true) => throw new NotSupportedException($"'{name}' both sends and receives data: the session cannot run it with its state yet."),
            (true, false) => Send(name, range, data.Source ?? throw Missing(name, range, "sends data: it needs a source"),
                _options.Data == SessionData.Device, progress, ct),
            (false, true) => Receive(name, range, data.Sink ?? throw Missing(name, range, "receives data: it needs a sink"),
                fresh: true, progress, ct),
            _ => _exchange.Execute(name, range, data, progress, ct),
        };
    }

    /// <summary>
    /// Forgets the values of the range (the state may widen it, e.g. an address map to whole addresses): the next
    /// transfer that needs them requests them from the device. Use it when the device changed them without telling (e.g.
    /// another patch was selected on the device).
    /// </summary>
    public void Invalidate(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        ThrowIfNotConnected();

        var map = _exchange.MapOf(range);
        IDeviceState? state;
        lock (_lock)
        {
            _states.TryGetValue(map, out state);
        }
        if (state is not null)
        {
            state.Clear(range);
            Raise(range, DeviceStateChange.Invalidated);
        }
    }

    /// <summary>Forgets all values of the device.</summary>
    public void Clear()
    {
        ThrowIfNotConnected();

        List<IDeviceState> states;
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

    // per block: from the state, or requested with the exchange; the first block that does not complete ends it
    private async Task<TransferResult> Receive(string name, PlanRange range, IValueSink sink, bool fresh, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        var state = StateOf(_exchange.MapOf(range));

        var results = new List<BlockResult>();
        var blocks = _exchange.Blocks(name, range);
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            IReadOnlyList<BlockResult> done = fresh || !state.Contains(block)
                ? await Fetch(name, state, block, ct)
                : [new BlockResult(block, BlockStatus.Completed)];
            foreach (var result in done)
            {
                if (result.Status == BlockStatus.Completed)
                {
                    state.CopyTo(result.Block, sink);
                }
                Report(result);
            }
            if (done.FirstOrDefault(result => result.Status != BlockStatus.Completed) is { } failed)
            {
                foreach (var skipped in blocks.Skip(index + 1))
                {
                    Report(BlockResult.NotTransferred(skipped, failed));
                }
                break;
            }
        }
        return new TransferResult(results);

        void Report(BlockResult result)
        {
            results.Add(result);
            progress?.Report(result);
        }
    }

    // completes what the send needs from the state (requested with requestData when it does not hold it), then sends
    private async Task<TransferResult> Send(string name, PlanRange range, IValueSource source, bool fresh, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        var state = StateOf(_exchange.MapOf(range));

        // exactly the client's values of the range
        var client = state.ClientValues(range, source);
        if (!state.Completes(range, client, fresh))
        {
            foreach (var block in _exchange.Blocks(ExchangeNames.RequestData, range).Where(block => !state.Completes(block, client, fresh)))
            {
                var failed = (await Fetch(ExchangeNames.RequestData, state, block, ct)).Where(result => result.Status != BlockStatus.Completed).ToList();
                if (failed.Count > 0)
                {
                    return new TransferResult(failed);
                }
            }
        }

        // the client's values over the state
        state.Merge(range, client);

        var sent = await _exchange.Execute(name, range, new ExchangeData { Source = state }, progress, ct);
        foreach (var result in sent.Blocks)
        {
            if (result.Status == BlockStatus.Completed)
            {
                Raise(result.Block, DeviceStateChange.Sent);
            }
            else
            {
                state.Clear(result.Block);
                Raise(result.Block, DeviceStateChange.Invalidated);
            }
        }
        return sent;
    }

    // the state of a map; the first use of a map starts listening to all of it, so the state follows the device
    private IDeviceState StateOf(IMessageMap map)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(map, out var state))
            {
                state = map.CreateState();
                _states[map] = state;
                var listened = state;
                _listeners.Add(_exchange.Listen(state.Whole, data => OnListened(listened, data), _listening.Token));
            }
            return state;
        }
    }

    private async Task<IReadOnlyList<BlockResult>> Fetch(string name, IDeviceState state, PlanRange block, CancellationToken ct)
    {
        var result = await _exchange.Execute(name, block, new ExchangeData { Sink = state }, ct);
        foreach (var received in result.Blocks.Where(r => r.Status == BlockStatus.Completed))
        {
            Raise(received.Block, DeviceStateChange.Received);
        }
        return result.Blocks;
    }

    private static ExchangeException Missing(string name, PlanRange range, string problem)
        => new(name, range.ExecutionPlan.Name?.FullName ?? String.Empty, [$"'{name}' {problem}."]);

    private void OnListened(IDeviceState state, ReceivedData data)
    {
        data.Decode(state);
        Raise(data.Block, DeviceStateChange.Received);
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
}
