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

        var port = new MidiConnectionSysExPort(options.Group);
        port.Connection = await session.Connect(endpointId, new MidiConnectionOptions(), port.OnPacketsReceived, ct);
        return new MessageExchange(_maps, port, device, options);
    }
}

/// <summary>
/// Transfers schema node ranges to and from one device over its own connection: sends the requests or data messages
/// for a range, decodes the data that comes back and does the read-modify-write for atomic groups.
/// Every transfer is given by a <see cref="PlanRange"/> (a range of a plan of the serializer); the address map and
/// the messages are deduced from the schemas of its plan. A transfer ends when its range is exhausted.
/// Disposing it closes the connection.
/// </summary>
public sealed class MessageExchange : IAsyncDisposable
{
    private readonly AddressMaps _maps;
    private readonly ISysExPort _port;
    private readonly DeviceValues _device;
    private readonly MessageExchangeOptions _options;
    private readonly Lock _lock = new();
    private readonly List<PendingBlock> _pending = [];
    private readonly List<Listener> _listeners = [];

    internal MessageExchange(AddressMaps maps, ISysExPort port, DeviceValues device, MessageExchangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(device);
        _maps = maps;
        _port = port;
        _device = device;
        _options = options;
        _port.Received += OnReceived;
    }

    /// <summary>
    /// Requests the data of the range (widened to atomic groups) and decodes each completed block into the sink.
    /// Blocks that are not answered within the timeout are reported in the result, not thrown.
    /// </summary>
    public async Task<AddressTransferResult> Receive(PlanRange range, IValueSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(sink);

        var map = _maps.Get(range);
        var device = DeviceFor(map);
        var widened = map.WidenToAtomic(range);
        var blocks = SelectBlocks(map, widened, map.Protocol.Requests, device);
        if (blocks.Count == 0)
        {
            throw new InvalidOperationException($"The messages of '{map.Root}' have no request that can ask for {map.GetAddressRange(widened)}.");
        }

        var results = new List<AddressBlockResult>();
        foreach (var (request, block) in blocks)
        {
            results.Add(await ReceiveBlock(map, device, request, block, sink, ct));
        }
        return new AddressTransferResult(results);
    }

    /// <summary>
    /// Encodes the range from the source and sends it as data messages. An atomic group the range only partly covers
    /// is completed with the device's current data first and written back as a whole. The transfer ends when the
    /// source has supplied the last logical field of the range.
    /// </summary>
    public async Task<AddressTransferResult> Send(PlanRange range, IValueSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);

        var map = _maps.Get(range);
        var device = DeviceFor(map);
        var widened = map.WidenToAtomic(range);
        var values = source;
        if (map.GetAddressRange(widened) != map.GetAddressRange(range))
        {
            // read-modify-write: the device's current data, with the client's values over it
            var store = new FieldValueStore();
            var current = await Receive(widened, store, ct);
            if (!current.IsComplete)
            {
                throw new InvalidOperationException(
                    $"The current data of the atomic groups around the range could not be received ({String.Join(", ", current.Blocks.Where(b => b.Status != AddressBlockStatus.Completed).Select(b => $"{b.Block.Addresses}: {b.Status}"))}).");
            }
            map.CopyValues(range, source, store);
            values = store;
        }

        var results = new List<AddressBlockResult>();
        foreach (var (message, block) in SelectBlocks(map, widened, map.Protocol.DataMessages, device))
        {
            try
            {
                var bytes = map.CreateData(message, block, values, device);
                await _port.Send(bytes, ct);
                results.Add(new AddressBlockResult(block, AddressBlockStatus.Completed));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new AddressBlockResult(block, AddressBlockStatus.Failed, exception));
            }
            await Pause(message, ct);
        }
        return new AddressTransferResult(results);
    }

    /// <summary>
    /// Decodes data messages the device sends on its own into the sink, for the nodes of the range, until cancelled.
    /// Sends nothing. Data outside the range, and replies to a pending <see cref="Receive"/>, are not delivered.
    /// A message is decoded as a whole: the sink can get fields around the range (e.g. the rest of a patch).
    /// </summary>
    public async Task Listen(PlanRange range, IValueSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(sink);

        var map = _maps.Get(range);
        var listener = new Listener(map, DeviceFor(map), map.GetAddressRange(range));
        lock (_lock)
        {
            _listeners.Add(listener);
        }
        try
        {
            // decode on the caller's flow, not on the receive thread
            await foreach (var data in listener.Channel.Reader.ReadAllAsync(ct))
            {
                data.Decode(sink);
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
        await _port.DisposeAsync();
    }

    // the device values of the map root, with the client's values over them; all values the messages need must be known
    private DeviceValues DeviceFor(AddressMap map)
    {
        var device = map.Device.With(_device);
        var missing = map.RequiredDeviceValues.Where(name => !device.TryGet(name, out _)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"The device values {String.Join(", ", missing)} are required by the messages of '{map.Root}'; supply them when the exchange is created.");
        }
        return device;
    }

    private async Task<AddressBlockResult> ReceiveBlock(AddressMap map, DeviceValues device, AddressMessage request, AddressBlock block, IValueSink sink, CancellationToken ct)
    {
        var pending = new PendingBlock(map, device, block);
        lock (_lock)
        {
            _pending.Add(pending);
        }

        try
        {
            await _port.Send(map.Protocol.CreateRequest(request, block.Addresses, device), ct);

            var timeout = Task.Delay(_options.ResponseTimeout, ct);
            if (await Task.WhenAny(pending.Completed, timeout) != pending.Completed)
            {
                ct.ThrowIfCancellationRequested();
                return new AddressBlockResult(block, AddressBlockStatus.TimedOut);
            }

            // decode on the caller's flow, in address order
            foreach (var data in pending.Received.OrderBy(d => d.Address))
            {
                data.Decode(sink);
            }
            await Pause(request, ct);
            return new AddressBlockResult(block, AddressBlockStatus.Completed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AddressBlockResult(block, AddressBlockStatus.Failed, exception);
        }
        finally
        {
            lock (_lock)
            {
                _pending.Remove(pending);
            }
        }
    }

    // Picks the messages for a range: a range message where one exists, else the atomic message for the atomic
    // groups and the field message for the remaining fields. Only messages whose device values are known are used.
    private static List<(AddressMessage Message, AddressBlock Block)> SelectBlocks(AddressMap map, PlanRange range,
        IReadOnlyList<AddressMessage> messages, DeviceValues device)
    {
        var usable = messages.Where(m => m.DeviceValueNames.All(name => device.TryGet(name, out _))).ToList();

        if (usable.FirstOrDefault(m => m.Unit == AddressUnit.Range) is { } rangeMessage)
        {
            return [.. map.Split(range, rangeMessage).Select(b => (rangeMessage, b))];
        }

        var blocks = new List<(AddressMessage, AddressBlock)>();
        if (usable.FirstOrDefault(m => m.Unit == AddressUnit.Atomic) is { } atomicMessage)
        {
            blocks.AddRange(map.Split(range, atomicMessage).Select(b => (atomicMessage, b)));
        }
        if (usable.FirstOrDefault(m => m.Unit == AddressUnit.Field) is { } fieldMessage)
        {
            var covered = blocks.Select(b => b.Item2.Addresses).ToList();
            blocks.AddRange(map.Split(range, fieldMessage)
                .Where(b => !covered.Any(c => c.Contains(b.Addresses)))
                .Select(b => (fieldMessage, b)));
        }
        return [.. blocks.OrderBy(b => b.Item2.Addresses.Start)];
    }

    private static Task Pause(AddressMessage message, CancellationToken ct)
        => message.Interval > TimeSpan.Zero ? Task.Delay(message.Interval, ct) : Task.CompletedTask;

    // raised by the port, possibly on a background thread
    private void OnReceived(ReadOnlyMemory<byte> sysEx)
    {
        var message = sysEx.ToArray();
        List<Listener> listeners;
        lock (_lock)
        {
            foreach (var pending in _pending)
            {
                if (pending.Map.TryReadData(message, pending.Device, out var data) && pending.TryAccept(data))
                {
                    return;
                }
            }
            listeners = [.. _listeners];
        }

        foreach (var listener in listeners)
        {
            if (listener.Map.TryReadData(message, listener.Device, out var data) && data.Block.Addresses.Overlaps(listener.Span))
            {
                listener.Channel.Writer.TryWrite(data);
            }
        }
    }

    /// <summary>
    /// A requested block that waits for its data: collects the data messages that fall inside it until all its
    /// addresses are covered (a device may answer one request with several messages).
    /// </summary>
    private sealed class PendingBlock(AddressMap map, DeviceValues device, AddressBlock block)
    {
        private readonly bool[] _covered = new bool[block.Addresses.Size];
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AddressMap Map { get; } = map;
        public DeviceValues Device { get; } = device;
        public List<ReceivedData> Received { get; } = [];
        public Task Completed => _completed.Task;

        public bool TryAccept(ReceivedData data)
        {
            var overlap = block.Addresses.Intersect(data.Block.Addresses);
            if (overlap.IsEmpty || _completed.Task.IsCompleted)
            {
                return false;
            }

            Received.Add(data);
            for (var address = overlap.Start; address < overlap.End; address++)
            {
                _covered[address - block.Addresses.Start] = true;
            }
            if (_covered.All(c => c))
            {
                _completed.TrySetResult();
            }
            return true;
        }
    }

    /// <summary>
    /// A <see cref="Listen"/> call: the map and addresses it listens to, and the channel to its caller.
    /// </summary>
    private sealed class Listener(AddressMap map, DeviceValues device, AddressRange span)
    {
        public AddressMap Map { get; } = map;
        public DeviceValues Device { get; } = device;
        public AddressRange Span { get; } = span;
        public Channel<ReceivedData> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<ReceivedData>(new UnboundedChannelOptions { SingleReader = true });
    }
}
