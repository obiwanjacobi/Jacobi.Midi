using System.Threading.Channels;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// Creates message exchanges on a MIDI session. Each exchange gets its own connection to the endpoint, with the
/// exchange as the receive handler. The address maps are deduced from the serializer's schemas when a range is used,
/// and shared by the exchanges of the factory; <see cref="Prepare"/> and <see cref="PrepareAll"/> validate them up front.
/// </summary>
public sealed class MessageExchangeFactory(IMidiSession session, Serializer serializer)
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
        return new MessageExchange(_maps, port, device, options);
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
/// <see cref="PlanRange"/> (a range of a plan of the serializer); the address map and the messages are deduced from
/// the schemas of its plan. The range is cut into blocks, and each block is transferred by a state machine of the
/// device's exchange protocol (<see cref="IExchangeProtocol"/>); this class runs those machines: it sends, waits and
/// decodes. Transfers to the device run one at a time. Disposing it closes the connection.
/// </summary>
public sealed class MessageExchange : IAsyncDisposable
{
    private readonly AddressMaps _maps;
    private readonly ISysExPort _port;
    private readonly DeviceValues _device;
    private readonly MessageExchangeOptions _options;
    private readonly SemaphoreSlim _transfers = new(1, 1);
    private readonly Lock _lock = new();
    private readonly List<Listener> _listeners = [];
    private ActiveTransfer? _active;

    internal MessageExchange(AddressMaps maps, ISysExPort port, DeviceValues device, MessageExchangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(device);
        _maps = maps;
        _port = port;
        _device = device;
        _options = options;
        _port.Received += OnReceived;
        _port.StateChanged += OnStateChanged;
    }

    /// <summary>The state of the exchange's connection to the device.</summary>
    public MidiConnectionState ConnectionState => _port.State;

    /// <summary>Raised when the state of the connection changes (e.g. the cable is pulled); may be raised on a background thread.</summary>
    public event EventHandler<MidiConnectionStateChangedArgs>? ConnectionStateChanged;

    /// <inheritdoc cref="Receive(PlanRange, IValueSink, IProgress{BlockResult}?, CancellationToken)"/>
    public Task<TransferResult> Receive(PlanRange range, IValueSink sink, CancellationToken ct)
        => Receive(range, sink, null, ct);

    /// <summary>
    /// Requests the data of the range (widened to atomic groups) and decodes each completed block into the sink.
    /// Blocks that are not answered, or are rejected, are reported in the result (and to the progress), not thrown.
    /// </summary>
    public async Task<TransferResult> Receive(PlanRange range, IValueSink sink, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(sink);

        var map = _maps.Get(range);
        var device = DeviceFor(map);
        var protocol = ProtocolFor(map);
        var results = new List<BlockResult>();
        foreach (var transfer in RequestTransfers(map, map.WidenToAtomic(range), device))
        {
            var result = await Run(map, device, transfer, protocol.Receive(transfer, Settings), null, sink, ct);
            results.Add(result);
            progress?.Report(result);
        }
        return new TransferResult(results);
    }

    /// <inheritdoc cref="Send(PlanRange, IValueSource, IProgress{BlockResult}?, CancellationToken)"/>
    public Task<TransferResult> Send(PlanRange range, IValueSource source, CancellationToken ct)
        => Send(range, source, null, ct);

    /// <summary>
    /// Encodes the range (widened to the atomic groups it overlaps) from the source and sends it as data messages.
    /// The source must supply every field that is sent, also those of the atomic groups outside the range: a block
    /// with a missing value fails. To send only some fields of an atomic group, use a <see cref="DeviceSession"/>,
    /// which completes the groups.
    /// </summary>
    public async Task<TransferResult> Send(PlanRange range, IValueSource source, IProgress<BlockResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);

        var map = _maps.Get(range);
        var device = DeviceFor(map);
        var protocol = ProtocolFor(map);
        var widened = map.WidenToAtomic(range);

        var results = new List<BlockResult>();
        foreach (var transfer in SelectBlocks(map, widened, map.Protocol.DataMessages, device))
        {
            var result = await Run(map, device, transfer, protocol.Send(transfer, Settings), source, null, ct);
            results.Add(result);
            progress?.Report(result);
        }
        return new TransferResult(results);
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
        var listener = new Listener(map, DeviceFor(map), map.GetAddressRange(range));
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

    /// <summary>
    /// The blocks a range (already widened to atomic groups) is requested with, as <see cref="Receive(PlanRange, IValueSink, IProgress{BlockResult}?, CancellationToken)"/> transfers them.
    /// </summary>
    /// <exception cref="InvalidOperationException">No request of the map can ask for the range.</exception>
    internal IReadOnlyList<AddressBlock> RequestBlocks(AddressMap map, PlanRange range)
        => [.. RequestTransfers(map, range, DeviceFor(map)).Select(transfer => transfer.Block)];

    private static List<Transfer> RequestTransfers(AddressMap map, PlanRange range, DeviceValues device)
    {
        var transfers = SelectBlocks(map, range, map.Protocol.Requests, device);
        return transfers.Count > 0
            ? transfers
            : throw new InvalidOperationException($"The messages of '{map.Root}' have no request that can ask for {map.GetAddressRange(range)}.");
    }

    private void OnStateChanged(object? sender, MidiConnectionStateChangedArgs args)
        => ConnectionStateChanged?.Invoke(this, args);

    private TransferSettings Settings => new(_options.ResponseTimeout);

    // Runs the state machine of one transfer: executes its actions and feeds it the events, then decodes the data it
    // accepted (receive). One transfer at a time, so replies without an address (ack, nak) belong to it.
    private async Task<BlockResult> Run(AddressMap map, DeviceValues device, Transfer transfer, ITransferMachine machine,
        IValueSource? values, IValueSink? sink, CancellationToken ct)
    {
        await _transfers.WaitAsync(ct);
        var active = new ActiveTransfer(map, device, transfer.Block);
        lock (_lock)
        {
            _active = active;
        }

        try
        {
            var received = new List<ReceivedData>();
            var action = machine.Start();
            while (true)
            {
                switch (action)
                {
                    case SendMessage send:
                        await _port.Send(Build(map, device, transfer, send.Message, values), ct);
                        action = machine.OnSent();
                        break;

                    case WaitForMessage wait:
                        action = await Wait(active, machine, DateTime.UtcNow + wait.Timeout, received, ct);
                        break;

                    case Pause pause:
                        await Task.Delay(pause.Duration, ct);
                        while (active.Inbox.Reader.TryRead(out _))
                        {
                            // messages during a pause are not delivered to the machine
                        }
                        action = machine.OnElapsed();
                        break;

                    case Complete done:
                        if (done.Status == BlockStatus.Completed && sink is not null)
                        {
                            // decode on the caller's flow, in address order
                            foreach (var data in received.OrderBy(d => d.Address))
                            {
                                data.Decode(sink);
                            }
                        }
                        return new BlockResult(transfer.Block, done.Status) { Reason = done.Reason };

                    default:
                        throw new InvalidOperationException($"The exchange machine {machine.GetType().Name} returned an unexpected action after '{transfer.Message.Name}'.");
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new BlockResult(transfer.Block, BlockStatus.Failed, exception) { Reason = exception.Message };
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
    private static async Task<ExchangeAction> Wait(ActiveTransfer active, ITransferMachine machine, DateTime deadline,
        List<ReceivedData> received, CancellationToken ct)
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

            var next = machine.OnMessage(message);
            if (next is Ignore)
            {
                continue;
            }
            if (message.Data is { } data)
            {
                received.Add(data);
            }
            return next;
        }
    }

    private static byte[] Build(AddressMap map, DeviceValues device, Transfer transfer, AddressMessage message, IValueSource? values)
        => message.Kind switch
        {
            ExchangeMessageKind.Request => map.Protocol.CreateRequest(message, transfer.Block.Addresses, device),
            ExchangeMessageKind.Data => map.CreateData(message, transfer.Block,
                values ?? throw new InvalidOperationException($"'{message.Name}': there are no values to send."), device),
            _ => throw new InvalidOperationException($"'{message.Name}': a {message.Kind} message is not sent by the exchange."),
        };

    private IExchangeProtocol ProtocolFor(AddressMap map)
        => _options.Protocol
            ?? ExchangeProtocols.Find(map.Protocol.Exchange)
            ?? throw new InvalidOperationException($"The exchange protocol '{map.Protocol.Exchange}' of '{map.Root}' is not known.");

    // the device values of the map root, with those the exchange was created with over them
    private DeviceValues DeviceFor(AddressMap map) => map.DeviceFor(_device);

    // Picks the messages for a range (see AddressMap.SelectBlocks).
    private static List<Transfer> SelectBlocks(AddressMap map, PlanRange range, IReadOnlyList<AddressMessage> messages, DeviceValues device)
        => [.. map.SelectBlocks(range, messages, device).Select(planned => new Transfer(planned.Message, planned.Block))];

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

        if (active is not null)
        {
            if (active.Map.TryReadData(message, active.Device, out var data))
            {
                if (data.Block.Addresses.Overlaps(active.Block.Addresses))
                {
                    active.Inbox.Writer.TryWrite(new ExchangeMessage(ExchangeMessageKind.Data, data.Message, data));
                    return;
                }
            }
            else if (active.Map.Protocol.TryReadReply(message, active.Device, out var reply))
            {
                active.Inbox.Writer.TryWrite(new ExchangeMessage(reply.Kind, reply));
                return;
            }
        }

        foreach (var listener in listeners)
        {
            if (listener.Map.TryReadData(message, listener.Device, out var data) && data.Block.Addresses.Overlaps(listener.Span))
            {
                listener.Inbox.Writer.TryWrite(data);
            }
        }
    }

    /// <summary>
    /// The transfer that is running: the map and block it is for, and the messages for its machine.
    /// </summary>
    private sealed class ActiveTransfer(AddressMap map, DeviceValues device, AddressBlock block)
    {
        public AddressMap Map { get; } = map;
        public DeviceValues Device { get; } = device;
        public AddressBlock Block { get; } = block;
        public Channel<ExchangeMessage> Inbox { get; } = Channel.CreateUnbounded<ExchangeMessage>(new UnboundedChannelOptions { SingleReader = true });
    }

    /// <summary>
    /// A <see cref="Listen"/> call: the map and addresses it listens to, and the channel to its caller.
    /// </summary>
    private sealed class Listener(AddressMap map, DeviceValues device, AddressRange span)
    {
        public AddressMap Map { get; } = map;
        public DeviceValues Device { get; } = device;
        public AddressRange Span { get; } = span;
        public Channel<ReceivedData> Inbox { get; } = Channel.CreateUnbounded<ReceivedData>(new UnboundedChannelOptions { SingleReader = true });
    }
}
