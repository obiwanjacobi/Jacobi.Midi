using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Commands;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device;

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
/// received from it, sent to it, and sent by the device on its own. The entry point of the Device API: the client only
/// gives the nodes it is interested in (a <see cref="PlanRange"/> of any root of the device) and the session routes
/// them by the kind of root. A root with a message map goes through the message exchange: the session completes the
/// atomic groups from its state and requests from the device only what it does not hold (see
/// <see cref="SessionData"/>); it runs the standard exchanges (<see cref="ExchangeNames"/>) of the device and, with
/// <see cref="Execute"/>, any other exchange of its exchange files. A command root (<c>midi.role</c> <c>cmd1</c> or
/// <c>cmd2</c>) goes through <see cref="CommandMessages"/>: its values are sent as channel messages and cannot be
/// requested; the session answers from what it holds. The first use of a root starts listening to all of it, so the
/// state follows the device.
/// The session works on a connection the client created and owns; create the session before the connection is
/// opened, so nothing is missed. When the connection is lost the session is disconnected for good: every call throws,
/// and the client disposes it and creates a new session.
/// </summary>
public sealed class DeviceSession : IAsyncDisposable
{
    private readonly MessageExchange? _exchange;
    private readonly CommandMessages? _commands;
    private readonly IMidiConnection? _connection;
    private readonly DeviceSessionOptions _options;
    private readonly Lock _lock = new();
    private readonly Dictionary<IMessageMap, IDeviceState> _states = [];
    private readonly Dictionary<CommandMap, CommandState> _commandStates = [];
    private readonly CancellationTokenSource _listening = new();
    private readonly List<Task> _listeners = [];
    private bool _connected;
    private bool _disposed;

    /// <summary>
    /// A session on the connection to a device: on the device's UMP groups, with the device values of the client (e.g.
    /// <c>deviceId</c>, <c>channel</c>) over those its roots fix.
    /// </summary>
    /// <param name="serializer">The serializer of the device's schemas.</param>
    /// <param name="connection">The connection to the device; the client creates, opens, owns and disposes it.</param>
    /// <param name="device">The device values of the client.</param>
    /// <param name="groups">The UMP groups the device is on: the group to send on and the group to receive on.</param>
    /// <param name="exchanges">The exchange files of the roots with a message map; null when the session only
    /// transfers command roots (a message-map root then has no exchange to run).</param>
    public DeviceSession(Serializer serializer, IMidiConnection connection, DeviceValues device, MidiGroupPair groups,
        ExchangeDefinitions? exchanges = null, DeviceSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(device);
        _options = options ?? new DeviceSessionOptions();
        _connection = connection;
        _commands = new CommandMessages(serializer, connection, device, groups);
        if (exchanges is not null)
        {
            var port = new MidiConnectionSysExPort(groups, ownsConnection: false) { Connection = connection };
            _exchange = new MessageExchange(new MessageMaps(serializer), exchanges, port, device);
        }
        _connection.ConnectionStateChanged += OnConnectionStateChanged;
        // a connection that is not opened yet is not lost
        _connected = connection.ConnectionState != MidiConnectionState.Faulted;
    }

    /// <summary>A session on a message exchange that owns its connection (no command roots), e.g. for the factory and tests.</summary>
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
    /// A command root cannot be requested: each field is a block, given from the state when the session holds it and
    /// reported as <see cref="BlockStatus.Unavailable"/> when it does not.
    /// </summary>
    /// <param name="data">Where the values come from; null: the session's default (<see cref="DeviceSessionOptions.Data"/>).</param>
    /// <exception cref="InvalidOperationException">The session is disconnected.</exception>
    /// <exception cref="ExchangeException">The device has no <see cref="ExchangeNames.RequestData"/> exchange that can run for the range.</exception>
    public Task<TransferResult> RequestData(PlanRange range, IValueSink sink, SessionData? data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(sink);
        ThrowIfNotConnected();
        return IsCommand(range)
            ? Task.FromResult(ReceiveCommands(range, sink, progress))
            : Receive(ExchangeNames.RequestData, range, sink, (data ?? _options.Data) == SessionData.Device, progress, ct);
    }

    /// <summary>
    /// Requests the range (widened to the atomic groups it overlaps) from the device and discards the values: a check
    /// that the device answers (endpoint, group, device values, schemas and exchange file). Nothing changes: not the
    /// session's state, and no listener gets the data (it belongs to the transfer).
    /// </summary>
    /// <exception cref="InvalidOperationException">The session is disconnected.</exception>
    /// <exception cref="ExchangeException">The device has no <see cref="ExchangeNames.RequestData"/> exchange that can
    /// run for the range (e.g. a command root, which cannot be requested).</exception>
    public Task<TransferResult> Probe(PlanRange range, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ThrowIfNotConnected();
        if (IsCommand(range))
        {
            throw Missing(ExchangeNames.RequestData, range, "does not apply to a command root: its values cannot be requested");
        }
        return ExchangeOf(ExchangeNames.RequestData, range).Execute(ExchangeNames.RequestData, range, new ExchangeData { Sink = NullSink.Instance }, ct);
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
    /// A command root is sent as channel messages, one block per field; the source supplies every field of the range.
    /// </summary>
    /// <param name="data">Where the values of the atomic groups outside the range come from; null: the session's default (<see cref="DeviceSessionOptions.Data"/>).</param>
    /// <exception cref="InvalidOperationException">The session is disconnected, or (a command root) a field of the range has no value.</exception>
    /// <exception cref="ExchangeException">The device has no exchange that can run for the range (to send, or to request what completes an atomic group).</exception>
    public Task<TransferResult> SendData(PlanRange range, IValueSource source, SessionData? data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfNotConnected();
        return IsCommand(range)
            ? SendCommands(range, source, progress, ct)
            : Send(ExchangeNames.SendData, range, source, (data ?? _options.Data) == SessionData.Device, progress, ct);
    }

    /// <summary>
    /// Runs the exchange <paramref name="name"/> of the device for the range, with the state of the session: an exchange
    /// that sends data runs as <see cref="SendData(PlanRange, IValueSource, SessionData?, IProgress{BlockResult}?, CancellationToken)"/>
    /// (with the source of <paramref name="data"/>), one that receives data always requests it from the device and
    /// updates the state (as with <see cref="SessionData.Device"/>), and one that does neither runs as it is.
    /// A command root has only the standard exchanges: <see cref="ExchangeNames.SendData"/> and
    /// <see cref="ExchangeNames.RequestData"/> run as <see cref="SendData(PlanRange, IValueSource, CancellationToken)"/>
    /// and <see cref="RequestData(PlanRange, IValueSink, CancellationToken)"/>.
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

        if (IsCommand(range))
        {
            return name switch
            {
                ExchangeNames.SendData => SendCommands(range, data.Source ?? throw Missing(name, range, "sends data: it needs a source"), progress, ct),
                ExchangeNames.RequestData => Task.FromResult(ReceiveCommands(range, data.Sink ?? throw Missing(name, range, "receives data: it needs a sink"), progress)),
                _ => throw Missing(name, range, $"does not apply to a command root: it only has {ExchangeNames.SendData} and {ExchangeNames.RequestData}"),
            };
        }

        var exchange = ExchangeOf(name, range);
        return exchange.DataOf(name, range) switch
        {
            (true, true) => throw new NotSupportedException($"'{name}' both sends and receives data: the session cannot run it with its state yet."),
            (true, false) => Send(name, range, data.Source ?? throw Missing(name, range, "sends data: it needs a source"),
                _options.Data == SessionData.Device, progress, ct),
            (false, true) => Receive(name, range, data.Sink ?? throw Missing(name, range, "receives data: it needs a sink"),
                fresh: true, progress, ct),
            _ => exchange.Execute(name, range, data, progress, ct),
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

        if (IsCommand(range))
        {
            var map = Commands.Get(range);
            CommandState? commandState;
            lock (_lock)
            {
                _commandStates.TryGetValue(map, out commandState);
            }
            if (commandState is not null)
            {
                commandState.Clear(map.FieldsOf(range));
                Raise(range, DeviceStateChange.Invalidated);
            }
            return;
        }

        if (_exchange is null)
        {
            return;
        }
        IDeviceState? state;
        lock (_lock)
        {
            _states.TryGetValue(_exchange.MapOf(range), out state);
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
        List<CommandState> commandStates;
        lock (_lock)
        {
            states = [.. _states.Values];
            commandStates = [.. _commandStates.Values];
        }
        foreach (var state in states)
        {
            state.Clear();
            Raise(state.Whole, DeviceStateChange.Invalidated);
        }
        foreach (var state in commandStates)
        {
            state.Clear(state.Map.Fields);
            Raise(state.Map.Whole, DeviceStateChange.Invalidated);
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

        if (_connection is not null)
        {
            _connection.ConnectionStateChanged -= OnConnectionStateChanged;
        }
        else if (_exchange is not null)
        {
            _exchange.ConnectionStateChanged -= OnConnectionStateChanged;
        }
        await _listening.CancelAsync();
        try
        {
            await Task.WhenAll(listeners);
        }
        catch (OperationCanceledException)
        {
            // listening ends with the cancellation
        }
        if (_commands is not null)
        {
            await _commands.DisposeAsync();
        }
        if (_exchange is not null)
        {
            await _exchange.DisposeAsync();
        }
        _listening.Dispose();
    }

    // the routing: a command root goes through the command messages, any other root through the message exchange
    private static bool IsCommand(PlanRange range) => MidiProperties.IsCommandRoot(range.ExecutionPlan.Root.Group);

    private CommandMessages Commands
        => _commands ?? throw new InvalidOperationException("This session has no command messages: create it on a connection.");

    private MessageExchange ExchangeOf(string name, PlanRange range)
        => _exchange ?? throw Missing(name, range, "cannot run: the session has no exchange files");

    // per block: from the state, or requested with the exchange (with its turns before and after, around that block);
    // the first block that does not complete, or turns before or after it that do not, end it
    private async Task<TransferResult> Receive(string name, PlanRange range, IValueSink sink, bool fresh, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        var exchange = ExchangeOf(name, range);
        var state = StateOf(exchange.MapOf(range));

        var results = new List<BlockResult>();
        BlockResult? before = null;
        BlockResult? after = null;
        var blocks = exchange.Blocks(name, range);
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var done = fresh || !state.Contains(block)
                ? await Fetch(name, state, block, ct)
                : new TransferResult([new BlockResult(block, BlockStatus.Completed)]);
            foreach (var result in done.Blocks)
            {
                if (result.Status == BlockStatus.Completed)
                {
                    state.CopyTo(result.Block, sink);
                }
                Report(result);
            }
            // the first that did not complete is reported, else the last that ran
            before = Pick(before, done.Before);
            after = Pick(after, done.After);
            var failed = done.Blocks.FirstOrDefault(result => result.Status != BlockStatus.Completed)
                ?? (done.After is { Status: not BlockStatus.Completed } failedAfter ? failedAfter : null);
            if (failed is not null)
            {
                foreach (var skipped in blocks.Skip(index + 1))
                {
                    Report(BlockResult.NotTransferred(skipped, failed));
                }
                break;
            }
        }
        return new TransferResult(results) { Before = before, After = after };

        static BlockResult? Pick(BlockResult? kept, BlockResult? next)
            => kept is { Status: not BlockStatus.Completed } ? kept : next ?? kept;

        void Report(BlockResult result)
        {
            results.Add(result);
            progress?.Report(result);
        }
    }

    // completes what the send needs from the state (requested with requestData when it does not hold it), then sends
    private async Task<TransferResult> Send(string name, PlanRange range, IValueSource source, bool fresh, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        var exchange = ExchangeOf(name, range);
        var state = StateOf(exchange.MapOf(range));

        // exactly the client's values of the range
        var client = state.ClientValues(range, source);
        if (!state.Completes(range, client, fresh))
        {
            foreach (var block in exchange.Blocks(ExchangeNames.RequestData, range).Where(block => !state.Completes(block, client, fresh)))
            {
                // the data is what completes the send; a failed 'before' leaves its blocks not transferred
                var failed = (await Fetch(ExchangeNames.RequestData, state, block, ct)).Blocks.Where(result => result.Status != BlockStatus.Completed).ToList();
                if (failed.Count > 0)
                {
                    return new TransferResult(failed);
                }
            }
        }

        // the client's values over the state
        state.Merge(range, client);

        var sent = await exchange.Execute(name, range, new ExchangeData { Source = state }, progress, ct);
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

    // a command root: every field of the range is sent; the state takes the values as they are sent
    private async Task<TransferResult> SendCommands(PlanRange range, IValueSource source, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        var commands = Commands;
        var map = commands.Get(range);
        var state = CommandStateOf(map);
        await commands.Send(range, source, state.Sink, ct);

        var results = map.FieldsOf(range).Select(field => new BlockResult(field.Range, BlockStatus.Completed)).ToList();
        foreach (var result in results)
        {
            progress?.Report(result);
        }
        Raise(range, DeviceStateChange.Sent);
        return new TransferResult(results);
    }

    // a command root: every field of the range from the state, or not available (it cannot be requested)
    private TransferResult ReceiveCommands(PlanRange range, IValueSink sink, IProgress<BlockResult>? progress)
    {
        var map = Commands.Get(range);
        var state = CommandStateOf(map);
        var results = new List<BlockResult>();
        foreach (var field in map.FieldsOf(range))
        {
            var result = state.TryCopyTo(field, sink)
                ? new BlockResult(field.Range, BlockStatus.Completed)
                : new BlockResult(field.Range, BlockStatus.Unavailable)
                {
                    Reason = $"{field.Command} cannot be requested from the device: the value is known once it is sent, or the device sends it.",
                };
            results.Add(result);
            progress?.Report(result);
        }
        return new TransferResult(results);
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
                _listeners.Add(_exchange!.Listen(state.Whole, data => OnListened(listened, data), _listening.Token));
            }
            return state;
        }
    }

    // the state of a command root; the first use starts listening to all of it
    private CommandState CommandStateOf(CommandMap map)
    {
        lock (_lock)
        {
            if (!_commandStates.TryGetValue(map, out var state))
            {
                state = new CommandState(map, field => Raise(field.Range, DeviceStateChange.Received));
                _commandStates[map] = state;
                _listeners.Add(Commands.Listen(map.Whole, state.Listener, _listening.Token));
            }
            return state;
        }
    }

    private async Task<TransferResult> Fetch(string name, IDeviceState state, PlanRange block, CancellationToken ct)
    {
        var result = await _exchange!.Execute(name, block, new ExchangeData { Sink = state }, ct);
        foreach (var received in result.Blocks.Where(r => r.Status == BlockStatus.Completed))
        {
            Raise(received.Block, DeviceStateChange.Received);
        }
        return result;
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

    /// <summary>
    /// The state of a command root: the values the session sent, and those the device sent on its own (reported with
    /// <paramref name="received"/>). Thread-safe.
    /// </summary>
    private sealed class CommandState(CommandMap map, Action<CommandField> received)
    {
        private readonly FieldValueStore _values = new();
        private readonly Lock _lock = new();

        public CommandMap Map { get; } = map;

        private Action<CommandField> Received { get; } = received;

        /// <summary>Takes the values as they are sent.</summary>
        public IValueSink Sink => new StateSink(this, report: false);

        /// <summary>Takes the values the device sends, and reports each.</summary>
        public IValueSink Listener => new StateSink(this, report: true);

        /// <summary>Gives the value of the field to the sink; false when the state does not hold it.</summary>
        public bool TryCopyTo(CommandField field, IValueSink sink)
        {
            uint physical;
            lock (_lock)
            {
                if (!_values.TryGet(field.Field.Path.Value, InstancePath.Empty, out _) || !Map.TryGetPhysical(field, _values, out physical))
                {
                    return false;
                }
            }
            Map.SetLogical(field, physical, sink);
            return true;
        }

        public void Clear(IEnumerable<CommandField> fields)
        {
            lock (_lock)
            {
                foreach (var field in fields)
                {
                    _values.Remove(field.Field.Path.Value, InstancePath.Empty);
                }
            }
        }

        // the state is flat: every scope is the sink itself
        private sealed class StateSink(CommandState state, bool report) : IValueSink
        {
            public void SetField(FieldContext context, LogicalField value)
            {
                lock (state._lock)
                {
                    state._values.SetField(context, value);
                }
                if (report && state.Map.FieldOf(context.Field) is { } field)
                {
                    state.Received(field);
                }
            }

            public IValueSink EnterGroup(GroupContext context) => this;
            public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
            public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
            public void ExitGroup(GroupContext context) { }
            public void ExitItem(RepeatContext context, int index, int count) { }
            public void ExitChoice(ChoiceContext context, int selectedIndex) { }
        }
    }
}
