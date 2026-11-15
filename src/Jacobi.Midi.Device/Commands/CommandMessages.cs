using System.Collections.Concurrent;
using System.Threading.Channels;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Commands;

/// <summary>
/// Sends and receives the values of command roots (<c>midi.role</c> <c>cmd1</c> or <c>cmd2</c>) as channel messages
/// over a MIDI connection, with the value model: a <see cref="PlanRange"/> of a command root and an
/// <see cref="IValueSource"/> to send, an <see cref="IValueSink"/> to receive into. Each field of the root is one
/// control change, RPN, NRPN or program change (<c>midi.cmd</c>); the root's role decides the form on the wire
/// (<c>cmd1</c>: MIDI 1.0, <c>cmd2</c>: MIDI 2.0). The values cannot be read back from the device: there is no request.
/// The MIDI channel is a device value (<c>channel</c>, 0-15): the root can fix it (<c>midi.device.channel</c>), the
/// client's value overrides it. The UMP groups are those of the device: the send group to send, the receive group to
/// listen. Works on a connection the client opened and owns; disposing it only stops receiving. Thread-safe.
/// </summary>
public sealed class CommandMessages : IAsyncDisposable
{
    private readonly Serializer _serializer;
    private readonly IMidiConnection _connection;
    private readonly DeviceValues _device;
    private readonly MidiGroupPair _groups;
    private readonly ConcurrentDictionary<ExecutionPlan, Lazy<CommandMap>> _maps = new(ReferenceEqualityComparer.Instance);
    private readonly Lock _lock = new();
    private readonly List<Listener> _listeners = [];
    private bool _disposed;

    /// <param name="serializer">The serializer of the schemas that hold the command roots.</param>
    /// <param name="connection">The connection to the device; the caller opens, owns and disposes it.</param>
    /// <param name="device">The device values of the client (e.g. <c>channel</c>); they override those the root fixes.</param>
    /// <param name="groups">The UMP groups the device is on: the group to send on and the group to receive on.</param>
    public CommandMessages(Serializer serializer, IMidiConnection connection, DeviceValues device, MidiGroupPair groups)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(device);
        _serializer = serializer;
        _connection = connection;
        _device = device;
        _groups = groups;
        _connection.PacketsReceived += OnPacketsReceived;
    }

    /// <summary>The problems of a command root (its fields, <c>midi.cmd</c> tags, data types and device values); empty when it is valid.</summary>
    public IReadOnlyList<MessageMapProblem> Validate(SchemaName root)
    {
        var problems = new MessageMapProblems();
        CommandMap.TryCreate(_serializer, _serializer.GetPlan(root), problems);
        return problems;
    }

    /// <summary>
    /// Sends the values of the fields of the range, each as the channel message of its field, in the order of the
    /// root. Every field must have a value in the source; otherwise nothing is sent.
    /// </summary>
    /// <exception cref="MessageMapException">The root of the range is not a valid command root.</exception>
    /// <exception cref="InvalidOperationException">A field has no value in the source, a value does not fit its
    /// field, or the channel is not known.</exception>
    public Task Send(PlanRange range, IValueSource source, CancellationToken ct)
        => Send(range, source, sent: null, ct);

    /// <summary>
    /// <see cref="Send(PlanRange, IValueSource, CancellationToken)"/>, and the values that are sent, exactly as they
    /// are sent (after their data type), to <paramref name="sent"/> (e.g. the state of a session).
    /// </summary>
    internal async Task Send(PlanRange range, IValueSource source, IValueSink? sent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var map = Get(range);
        var channel = ChannelOf(map);
        var group = _groups.Send.Value;
        var missing = new List<string>();
        var packets = new List<UniversalPacket>();
        var values = new List<(CommandField Field, uint Value)>();
        foreach (var field in map.FieldsOf(range))
        {
            if (!map.TryGetPhysical(field, source, out var value))
            {
                missing.Add(field.Field.Path.Value);
                continue;
            }
            if (value > CommandWire.MaxValue(field.Bits))
            {
                throw new InvalidOperationException(
                    $"The value {value} of '{field.Field.Path}' does not fit the {field.Bits} bits of {field.Command}.");
            }
            packets.AddRange(CommandWire.Write(field, value, map.Protocol, group, channel));
            values.Add((field, value));
        }
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Nothing is sent: the source has no value for {String.Join(", ", missing)}.");
        }

        await _connection.Send(packets.ToArray(), ct);
        if (sent is not null)
        {
            foreach (var (field, value) in values)
            {
                map.SetLogical(field, value, sent);
            }
        }
    }

    /// <summary>
    /// Receives the values of the fields of the range that the device sends, into the sink, until cancelled. Sends
    /// nothing. Channel messages of other channels or groups, of other kinds, or of fields outside the range are
    /// ignored. The values are given to the sink on the caller's flow, not on the receive thread. A value the field
    /// cannot take (e.g. not one of its options) ends the listening with the exception.
    /// </summary>
    /// <exception cref="MessageMapException">The root of the range is not a valid command root.</exception>
    /// <exception cref="InvalidOperationException">The channel is not known.</exception>
    public async Task Listen(PlanRange range, IValueSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var map = Get(range);
        var listener = new Listener(new CommandReceiver(map, map.FieldsOf(range).ToHashSet(), _groups.Receive.Value, ChannelOf(map)));
        lock (_lock)
        {
            _listeners.Add(listener);
        }
        try
        {
            await foreach (var (field, value) in listener.Inbox.Reader.ReadAllAsync(ct))
            {
                map.SetLogical(field, value, sink);
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

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }
            _disposed = true;
            foreach (var listener in _listeners)
            {
                listener.Inbox.Writer.TryComplete();
            }
        }
        _connection.PacketsReceived -= OnPacketsReceived;
        return ValueTask.CompletedTask;
    }

    // the command map of the range's root; created and validated on first use
    internal CommandMap Get(PlanRange range)
        => _maps.GetOrAdd(range.ExecutionPlan, plan => new Lazy<CommandMap>(() => CommandMap.Create(_serializer, plan))).Value;

    // the channel of the device: the client's value over the root's
    private byte ChannelOf(CommandMap map)
    {
        if (!map.Device.With(_device).TryGet(CommandMap.ChannelValue, out var channel))
        {
            throw new InvalidOperationException(
                $"The device value '{CommandMap.ChannelValue}' is needed for the command root '{map.Root}'; supply it with the device or fix it on the root ({MidiProperties.DevicePrefix}{CommandMap.ChannelValue}).");
        }
        return channel is >= 0 and <= 15
            ? (byte)channel
            : throw new InvalidOperationException($"The channel ({channel}) must be from 0 to 15.");
    }

    // called by the connection, serialized per connection, on a background thread
    private void OnPacketsReceived(ReadOnlySpan<StampedPacket> packets)
    {
        Listener[] listeners;
        lock (_lock)
        {
            if (_listeners.Count == 0)
            {
                return;
            }
            listeners = [.. _listeners];
        }
        foreach (var packet in packets)
        {
            foreach (var listener in listeners)
            {
                foreach (var received in listener.Receiver.Receive(packet.Packet))
                {
                    listener.Inbox.Writer.TryWrite(received);
                }
            }
        }
    }

    // one call of Listen: its receiver (with the state of the sequences) and the values it has to give to the sink
    private sealed class Listener(CommandReceiver receiver)
    {
        public CommandReceiver Receiver { get; } = receiver;

        public Channel<(CommandField Field, uint Value)> Inbox { get; } = Channel.CreateUnbounded<(CommandField, uint)>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    }
}
