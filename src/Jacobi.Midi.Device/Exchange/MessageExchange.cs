using System.Diagnostics;
using System.Threading.Channels;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Creates message exchanges on a MIDI session. Each exchange gets its own connection to the endpoint, with the
/// exchange as the receive handler. The address maps are deduced from the serializer's schemas when a range is used,
/// and shared by the exchanges of the factory; <see cref="Prepare"/> and <see cref="PrepareAll"/> validate them up front.
/// The exchange files (<paramref name="exchanges"/>) say how each pattern goes for a map root.
/// </summary>
public sealed class MessageExchangeFactory(IMidiSession session, Serializer serializer, ExchangeDefinitions exchanges)
{
    private readonly AddressMaps _maps = new(serializer);

    /// <summary>Builds and validates the address map of a schema root; throws an <see cref="AddressMapException"/> when it is not valid.</summary>
    public void Prepare(SchemaName root) => _maps.Prepare(root);

    /// <summary>
    /// Builds and validates the address maps of all roots a data message can carry;
    /// throws an <see cref="AddressMapException"/> for the first one that is not valid.
    /// </summary>
    public void PrepareAll() => _maps.PrepareAll();

    /// <summary>Opens a connection to the device's endpoint and creates an exchange for it.</summary>
    public async Task<MessageExchange> Create(string endpointId, DeviceValues device, MessageExchangeOptions? options, CancellationToken ct)
    {
        options ??= new MessageExchangeOptions();

        var port = new MidiConnectionSysExPort(options.Groups);
        port.Connection = await session.Connect(endpointId, new MidiConnectionOptions(), port.OnPacketsReceived, ct);
        return new MessageExchange(_maps, exchanges, port, device);
    }

    /// <summary>
    /// Opens a connection to the device's endpoint and creates a session for it: an exchange that keeps the state of the
    /// device for as long as the connection lasts.
    /// </summary>
    public async Task<DeviceSession> CreateSession(string endpointId, DeviceValues device, MessageExchangeOptions? options,
        DeviceSessionOptions? sessionOptions, CancellationToken ct)
        => new DeviceSession(await Create(endpointId, device, options, ct), sessionOptions ?? new DeviceSessionOptions());
}

/// <summary>
/// Transfers schema node ranges to and from one device over its own connection. Every transfer is given by a
/// <see cref="PlanRange"/> (a range of a plan of the serializer) and the name of an exchange: the exchange file of the
/// range's map root (<see cref="ExchangeDefinitions"/>) says how that exchange goes for the device. The address map
/// cuts the range into blocks for the exchange's cutting message, builds and recognises the messages; the exchange
/// runs once per block (<see cref="ExchangeMachine"/>). This class runs it: it sends, waits, paces and decodes.
/// Transfers to the device run one at a time. Disposing it closes the connection.
/// </summary>
public sealed class MessageExchange : IAsyncDisposable
{
    private readonly AddressMaps _maps;
    private readonly ExchangeDefinitions _exchanges;
    private readonly ISysExPort _port;
    private readonly DeviceValues _device;
    private readonly SemaphoreSlim _transfers = new(1, 1);
    private readonly Lock _lock = new();
    private readonly List<Listener> _listeners = [];
    private ActiveTransfer? _active;
    // when the last message was sent (a Stopwatch timestamp; 0: none yet); only used under _transfers
    private long _lastSent;

    internal MessageExchange(AddressMaps maps, ExchangeDefinitions exchanges, ISysExPort port, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(exchanges);
        ArgumentNullException.ThrowIfNull(device);
        _maps = maps;
        _exchanges = exchanges;
        _port = port;
        _device = device;
        _port.Received += OnReceived;
        _port.StateChanged += OnStateChanged;
    }

    /// <summary>The state of the exchange's connection to the device.</summary>
    public MidiConnectionState ConnectionState => _port.State;

    /// <summary>Raised when the state of the connection changes (e.g. the cable is pulled); may be raised on a background thread.</summary>
    public event EventHandler<MidiConnectionStateChangedArgs>? ConnectionStateChanged;

    /// <inheritdoc cref="Execute(string, PlanRange, ExchangeData, IProgress{BlockResult}?, CancellationToken)"/>
    public Task<TransferResult> Execute(string name, PlanRange range, ExchangeData data, CancellationToken ct)
        => Execute(name, range, data, null, ct);

    /// <summary>
    /// Runs the exchange <paramref name="name"/> (e.g. <see cref="ExchangeNames.RequestData"/>) for the range: widened
    /// to the atomic groups it overlaps and cut into blocks for the exchange's cutting message, the exchange runs once
    /// per block. Received data of a completed block is decoded into the sink; sent data comes from the source, which
    /// must supply every field that is sent, also those of the atomic groups outside the range (a
    /// <see cref="DeviceSession"/> completes them). The first block that does not complete ends the transfer; the
    /// blocks after it are reported with the same status. Blocks are reported in the result (and to the progress), not thrown.
    /// </summary>
    /// <exception cref="ExchangeException">The exchange cannot run for the range (see <see cref="Validate"/>); nothing was sent.</exception>
    public async Task<TransferResult> Execute(string name, PlanRange range, ExchangeData data, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        var prepared = Prepare(name, range, data);
        var blocks = Split(prepared, range);

        var results = new List<BlockResult>();
        for (var index = 0; index < blocks.Count; index++)
        {
            var result = await Run(prepared, blocks[index], data, ct);
            Report(result);
            if (result.Status != BlockStatus.Completed)
            {
                foreach (var block in blocks.Skip(index + 1))
                {
                    Report(BlockResult.NotTransferred(block, result));
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

    /// <summary>
    /// The problems that prevent the exchange <paramref name="name"/> from running for the range with the data; empty
    /// when it can run. Checked: the exchange file of the range's map root has the exchange; the messages it names
    /// exist and fit the map; it sends a message that asks for or carries data (its cutting message), and only one
    /// such message; the device values its messages need are known; and (with <paramref name="data"/>) a source is
    /// given when it sends data, a sink when it receives data.
    /// </summary>
    /// <exception cref="AddressMapException">The root of the range is not a valid address map.</exception>
    public IReadOnlyList<string> Validate(string name, PlanRange range, ExchangeData? data = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(range);
        return TryPrepare(name, _maps.Get(range), data, out _);
    }

    /// <summary>
    /// Decodes data messages the device sends on its own into the sink, for the nodes of the range, until cancelled.
    /// Sends nothing. Data outside the range, and data that belongs to a running transfer, are not delivered.
    /// A message is decoded as a whole: the sink can get fields around the range (e.g. the rest of a patch).
    /// </summary>
    public Task Listen(PlanRange range, IValueSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sink);
        return Listen(range, data => data.Decode(sink), ct);
    }

    /// <summary>
    /// <see cref="Listen(PlanRange, IValueSink, CancellationToken)"/> with the recognised data messages, on the caller's flow.
    /// </summary>
    internal async Task Listen(PlanRange range, Action<ReceivedData> received, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(received);

        var map = _maps.Get(range);
        var listener = new Listener(map, map.DeviceFor(_device), range);
        lock (_lock)
        {
            _listeners.Add(listener);
        }
        try
        {
            // decode on the caller's flow, not on the receive thread
            await foreach (var data in listener.Inbox.Reader.ReadAllAsync(ct))
            {
                received(data);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // listening ends with the cancellation
        }
        finally
        {
            lock (_lock)
            {
                _listeners.Remove(listener);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _port.Received -= OnReceived;
        _port.StateChanged -= OnStateChanged;
        await _port.DisposeAsync();
        _transfers.Dispose();
    }

    /// <summary>The address map of a range (deduced from its plan and cached).</summary>
    internal AddressMap MapOf(PlanRange range) => _maps.Get(range);

    /// <summary>The blocks the exchange splits a range into (widened to atomic groups), as <see cref="Execute(string, PlanRange, ExchangeData, IProgress{BlockResult}?, CancellationToken)"/> transfers them.</summary>
    /// <exception cref="ExchangeException">The exchange cannot run for the range.</exception>
    internal IReadOnlyList<PlanRange> Blocks(string name, PlanRange range)
        => Split(Prepare(name, range, null), range);

    private static List<PlanRange> Split(PreparedExchange prepared, PlanRange range)
        => [.. prepared.Map.Split(prepared.Map.WidenToAtomic(range), prepared.Cutting).Select(block => block.Nodes)];

    /// <summary>Whether the exchange sends data (from a source) and receives data (into a sink).</summary>
    /// <exception cref="ExchangeException">The exchange cannot run for the range.</exception>
    internal (bool SendsData, bool ReceivesData) DataOf(string name, PlanRange range)
    {
        var prepared = Prepare(name, range, null);
        return (prepared.SendsData, prepared.ReceivesData);
    }

    private PreparedExchange Prepare(string name, PlanRange range, ExchangeData? data)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(range);
        var map = _maps.Get(range);
        var problems = TryPrepare(name, map, data, out var prepared);
        return problems.Count == 0 ? prepared! : throw new ExchangeException(name, map.Root.FullName, problems);
    }

    private List<string> TryPrepare(string name, AddressMap map, ExchangeData? data, out PreparedExchange? prepared)
    {
        prepared = null;
        var problems = new List<string>();
        var set = _exchanges.Find(map.Root);
        var definition = set?.Find(name);
        if (definition is null)
        {
            problems.Add(set is null
                ? $"There is no exchange file for '{map.Root}'."
                : $"The exchange file for '{map.Root}' ({set.Source}) has no exchange '{name}'.");
            return problems;
        }

        var messages = new Dictionary<SchemaName, DeviceMessage>();
        foreach (var messageName in definition.Messages)
        {
            try
            {
                messages[messageName] = map.Message(messageName);
            }
            catch (Exception exception) when (exception is InvalidOperationException or AddressMapException)
            {
                problems.Add(exception.Message);
            }
        }
        if (problems.Count > 0)
        {
            return problems;
        }

        foreach (var received in definition.Turns.SelectMany(turn => turn.Receive).Distinct().Where(received => !messages[received].IsData))
        {
            problems.Add($"'{received}' is received as data, but it carries no data (it has no open choice midi.role=\"data\").");
        }

        var sent = definition.Turns.Select(turn => turn.Send).OfType<SchemaName>().Select(send => messages[send]).Distinct().ToList();
        var cutting = sent.Where(message => message.IsData || AddressProtocol.IsRequest(message)).ToList();
        if (cutting.Count == 0)
        {
            problems.Add($"'{name}' sends no message that asks for data (role '{MidiProperties.RoleAddress}') or carries data: the range cannot be cut into blocks.");
        }
        else if (cutting.Count > 1)
        {
            problems.Add($"'{name}' sends more than one message that asks for or carries data ({String.Join(", ", cutting.Select(message => message.FullName))}): the blocks can be cut for one message only.");
        }

        var missing = map.MissingDeviceValues(_device, messages.Values);
        if (missing.Count > 0)
        {
            problems.Add($"The device values {String.Join(", ", missing)} are required by the messages of '{name}'; supply them with the device.");
        }

        var sendsData = sent.Any(message => message.IsData);
        var receivesData = definition.Turns.Any(turn => turn.Receive.Count > 0);
        if (data is not null && sendsData && data.Source is null)
        {
            problems.Add($"'{name}' sends data: it needs a source.");
        }
        if (data is not null && receivesData && data.Sink is null)
        {
            problems.Add($"'{name}' receives data: it needs a sink.");
        }

        if (problems.Count == 0)
        {
            var receivable = messages.Values.Where(message => message.IsData && !sent.Contains(message)).Distinct().ToList();
            var others = definition.Turns.SelectMany(turn => turn.Transitions)
                .Select(transition => transition.Message).OfType<SchemaName>()
                .Select(message => messages[message]).Where(message => !message.IsData).Distinct().ToList();
            prepared = new PreparedExchange(map, definition, map.Device.With(_device), messages, cutting[0], sendsData, receivesData, receivable, others);
        }
        return problems;
    }

    private void OnStateChanged(object? sender, MidiConnectionStateChangedArgs args)
        => ConnectionStateChanged?.Invoke(this, args);

    // Runs the exchange for one block: executes the actions of its machine and feeds it the events, then decodes the
    // data it accepted. One transfer at a time, so messages without an address (an ack) belong to it.
    private async Task<BlockResult> Run(PreparedExchange prepared, PlanRange block, ExchangeData data, CancellationToken ct)
    {
        await _transfers.WaitAsync(ct);
        var active = new ActiveTransfer(prepared, block);
        lock (_lock)
        {
            _active = active;
        }

        try
        {
            var machine = new ExchangeMachine(prepared.Definition, prepared.Messages);
            var coverage = prepared.Map.Coverage(block);
            var received = new List<ReceivedData>();
            var action = machine.Start();
            while (true)
            {
                switch (action)
                {
                    case SendMessage send:
                        var message = Build(prepared, block, send.Message, data.Source);
                        await Pace(prepared.Definition.MessageInterval, ct);
                        await _port.Send(message, ct);
                        _lastSent = Stopwatch.GetTimestamp();
                        action = machine.OnSent();
                        break;

                    case WaitForMessage wait:
                        action = await Wait(active, machine, coverage, received, DateTime.UtcNow + wait.Timeout, ct);
                        break;

                    case Complete done:
                        if (done.Status == BlockStatus.Completed && data.Sink is { } sink)
                        {
                            // decode on the caller's flow, in address order
                            foreach (var accepted in received.OrderBy(d => prepared.Map.GetAddressRange(d.Block).Start))
                            {
                                accepted.Decode(sink);
                            }
                        }
                        return new BlockResult(block, done.Status) { Reason = done.Reason };

                    default:
                        throw new InvalidOperationException($"'{prepared.Definition.Name}', turn '{machine.Turn.Name}': unexpected action.");
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new BlockResult(block, BlockStatus.Failed, exception) { Reason = exception.Message };
        }
        finally
        {
            lock (_lock)
            {
                _active = null;
            }
            _transfers.Release();
        }
    }

    // waits for a message for the machine until the deadline; ignored messages do not extend it
    private static async Task<ExchangeAction> Wait(ActiveTransfer active, ExchangeMachine machine, BlockCoverage coverage,
        List<ReceivedData> received, DateTime deadline, CancellationToken ct)
    {
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return machine.OnElapsed();
            }

            ExchangeMessage message;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(remaining);
                try
                {
                    message = await active.Inbox.Reader.ReadAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return machine.OnElapsed();
                }
            }

            ExchangeAction next;
            if (message.Data is { } data && machine.Receives(message.Message))
            {
                received.Add(data);
                next = machine.OnReceived(message, coverage.Add(data));
            }
            else
            {
                next = machine.OnMessage(message);
            }

            if (next is not Ignore)
            {
                return next;
            }
        }
    }

    // keeps the least time between two messages to the device (the exchange's messageInterval)
    private async Task Pace(TimeSpan interval, CancellationToken ct)
    {
        if (interval <= TimeSpan.Zero || _lastSent == 0)
        {
            return;
        }
        var wait = interval - Stopwatch.GetElapsedTime(_lastSent);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, ct);
        }
    }

    private static byte[] Build(PreparedExchange prepared, PlanRange block, DeviceMessage message, IValueSource? values)
        => message.IsData
            ? prepared.Map.CreateData(message, block,
                values ?? throw new InvalidOperationException($"'{message.FullName}': there are no values to send."), prepared.Device)
            : prepared.Map.Protocol.CreateMessage(message, prepared.Map.GetAddressRange(block), prepared.Device);

    // raised by the port, possibly on a background thread
    private void OnReceived(ReadOnlyMemory<byte> sysEx)
    {
        var message = sysEx.ToArray();
        ActiveTransfer? active;
        List<Listener> listeners;
        lock (_lock)
        {
            active = _active;
            listeners = [.. _listeners];
        }

        if (active is not null && active.TryRecognise(message) is { } recognised)
        {
            active.Inbox.Writer.TryWrite(recognised);
            return;
        }

        foreach (var listener in listeners)
        {
            if (listener.Map.TryReadData(message, listener.Device, out var data) && data.Block.Overlaps(listener.Range))
            {
                listener.Inbox.Writer.TryWrite(data);
            }
        }
    }

    /// <summary>
    /// An exchange that can run for a map: its definition, the device values, the messages it names (resolved in the
    /// map), its cutting message, and the messages it can get from the device: data it does not send itself, and
    /// messages without data its transitions wait for.
    /// </summary>
    private sealed record PreparedExchange(AddressMap Map, ExchangeDefinition Definition, DeviceValues Device,
        IReadOnlyDictionary<SchemaName, DeviceMessage> Messages, DeviceMessage Cutting, bool SendsData, bool ReceivesData,
        IReadOnlyList<DeviceMessage> Receivable, IReadOnlyList<DeviceMessage> Others);

    /// <summary>
    /// The transfer that is running: the exchange and block it is for, and the messages for its machine.
    /// </summary>
    private sealed class ActiveTransfer(PreparedExchange exchange, PlanRange block)
    {
        public Channel<ExchangeMessage> Inbox { get; } = Channel.CreateUnbounded<ExchangeMessage>(new UnboundedChannelOptions { SingleReader = true });

        // a data message of the exchange inside the block, or another message the exchange waits for; null otherwise
        public ExchangeMessage? TryRecognise(byte[] sysEx)
        {
            foreach (var message in exchange.Receivable)
            {
                if (exchange.Map.TryReadData(message, sysEx, exchange.Device, out var data) && data.Block.Overlaps(block))
                {
                    return new ExchangeMessage(message, data);
                }
            }
            foreach (var message in exchange.Others)
            {
                if (exchange.Map.Protocol.TryRead(message, sysEx, exchange.Device))
                {
                    return new ExchangeMessage(message);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A <see cref="Listen"/> call: the map and the range it listens to, and the channel to its caller.
    /// </summary>
    private sealed class Listener(AddressMap map, DeviceValues device, PlanRange range)
    {
        public AddressMap Map { get; } = map;
        public DeviceValues Device { get; } = device;
        public PlanRange Range { get; } = range;
        public Channel<ReceivedData> Inbox { get; } = Channel.CreateUnbounded<ReceivedData>(new UnboundedChannelOptions { SingleReader = true });
    }
}
