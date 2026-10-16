using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Jacobi.Midi.Core;
using Native = Windows.Devices.Midi2;

namespace Jacobi.Midi.Windows;

internal sealed class WindowsMidiConnection : IMidiConnection
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource<bool> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<MidiConnectionStateChangedArgs> _stateChanges = Channel.CreateUnbounded<MidiConnectionStateChangedArgs>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Native.MidiEndpointConnection _native;
    private readonly Guid _id;
    private readonly MidiPackedReceivedHandler? _receiver;
    private readonly Action<WindowsMidiConnection, Guid> _remove;
    private readonly Channel<StampedPacket> _received = Channel.CreateBounded<StampedPacket>(
        new BoundedChannelOptions(4096) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _dispatch;
    private readonly Task _stateDispatch;
    private Task? _disposeTask;
    private MidiConnectionState _state = MidiConnectionState.Closed;
    private Exception? _error;

    internal WindowsMidiConnection(Native.MidiEndpointConnection native, MidiEndpointInfo endpoint,
        MidiDirections directions, MidiPackedReceivedHandler? receiver, Action<WindowsMidiConnection, Guid> remove)
    {
        _native = native;
        _id = native.ConnectionId;
        EndpointInfo = endpoint;
        Directions = directions;
        _receiver = receiver;
        _remove = remove;
        if ((directions & MidiDirections.Receive) != 0)
            _native.MessageReceived += MessageReceived;
        _native.EndpointDeviceDisconnected += Disconnected;
        _dispatch = Task.Run(Dispatch);
        _stateDispatch = Task.Run(DispatchStateChanges);
    }

    public MidiEndpointInfo EndpointInfo { get; }
    public MidiDirections Directions { get; }

    public MidiConnectionState ConnectionState
    {
        get { lock (_gate) return _state; }
    }

    public Exception? Error
    {
        get { lock (_gate) return _error; }
    }

    public event EventHandler<MidiConnectionStateChangedArgs>? ConnectionStateChanged;

    internal void Open(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            _state = MidiConnectionState.Open;
        if (!_native.Open())
            throw new MidiException($"Could not open MIDI endpoint '{EndpointInfo.Name}'.");
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_state != MidiConnectionState.Open)
                throw new MidiException($"MIDI endpoint '{EndpointInfo.Name}' failed while opening.", _error!);
            _opened.TrySetResult(true);
        }
    }

    public async ValueTask Send(ReadOnlyMemory<UniversalPacket> packets, CancellationToken ct)
    {
        await _sending.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RequireSend();
            var messages = new Native.MidiMessageStruct[packets.Length];
            for (var index = 0; index < messages.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                messages[index] = WindowsMidiPacketConverter.ToNative(packets.Span[index]);
            }
            SubmitBatches(messages, null, ct);
        }
        finally
        {
            _sending.Release();
        }
    }

    public async ValueTask SendScheduled(ReadOnlyMemory<StampedPacket> packets, CancellationToken ct)
    {
        await _sending.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RequireSend();
            var messages = new Native.MidiMessageStruct[packets.Length];
            var timestamps = new ulong[packets.Length];
            for (var index = 0; index < messages.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var packet = packets.Span[index];
                timestamps[index] = packet.Timestamp;
                if (index > 0 && timestamps[index] < timestamps[index - 1])
                    throw new ArgumentException("Scheduled packets must have nondecreasing timestamps.", nameof(packets));
                messages[index] = WindowsMidiPacketConverter.ToNative(packet.Packet);
            }
            SubmitBatches(messages, timestamps, ct);
        }
        finally
        {
            _sending.Release();
        }
    }

    private void RequireSend()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_state != MidiConnectionState.Open)
                throw new MidiException("The MIDI connection is not open.", _error!);
            if ((Directions & MidiDirections.Send) == 0)
                throw new InvalidOperationException("This MIDI connection is receive-only.");
        }
    }

    private void SubmitBatches(Native.MidiMessageStruct[] messages, ulong[]? timestamps, CancellationToken ct)
    {
        var index = 0;
        while (index < messages.Length)
        {
            ct.ThrowIfCancellationRequested();
            RequireSend();
            Native.MidiSendMessageResults result;
            var count = 0;
            var timestamp = timestamps?[index] ?? Native.MidiClock.TimestampConstantSendImmediately;
            try
            {
                var maxWords = _native.GetSupportedMaxMidiWordsPerTransmission();
                uint words = 0;
                while (index + count < messages.Length
                    && (timestamps is null || timestamps[index + count] == timestamp))
                {
                    var packetWords = WindowsMidiPacketConverter.WordCount(messages[index + count].Word0);
                    if (packetWords > maxWords - words)
                        break;
                    words += packetWords;
                    count++;
                }
                if (count == 0)
                    throw new MidiException($"The native transmission limit of {maxWords} words cannot hold packet {index}. Earlier packets may have been accepted.");
                ct.ThrowIfCancellationRequested();
                result = count == 1
                    ? _native.SendSingleMessageStruct(timestamp, WindowsMidiPacketConverter.WordCount(messages[index].Word0), in messages[index])
                    : _native.SendMultipleMessagesStructArray(timestamp, (uint)index, (uint)count, messages);
            }
            catch (COMException error)
            {
                Fault(error);
                throw new MidiException($"Windows MIDI send failed at packet {index}; earlier packets may have been accepted.", error);
            }
            if ((result & Native.MidiSendMessageResults.Succeeded) == 0
                || (result & Native.MidiSendMessageResults.Failed) != 0)
            {
                var failure = new MidiException($"Windows MIDI send failed for packets {index} through {index + count - 1}: {result}. Earlier transmissions may have been accepted.");
                if ((result & Native.MidiSendMessageResults.EndpointConnectionClosedOrInvalid) != 0)
                    Fault(failure);
                throw failure;
            }
            index += count;
        }
    }

    private void MessageReceived(Native.IMidiMessageReceivedEventSource sender, Native.MidiMessageReceivedEventArgs args)
    {
        // Copy native event data before the callback returns; application callbacks run separately.
        try
        {
            lock (_gate)
            {
                if (_disposeTask is not null || _state != MidiConnectionState.Open)
                    return;
                var count = args.FillMessageStruct(out var message);
                var packet = new StampedPacket(WindowsMidiPacketConverter.FromNative(count, message), args.Timestamp);
                if (!_received.Writer.TryWrite(packet))
                    throw new MidiException("The MIDI receive buffer overflowed; the connection is no longer reliable.");
            }
        }
        catch (Exception error)
        {
            Fault(error);
        }
    }

    private async Task Dispatch()
    {
        var batch = new StampedPacket[64];
        try
        {
            if (!await _opened.Task.ConfigureAwait(false))
                return;
            while (await _received.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var count = 0;
                while (count < batch.Length && _received.Reader.TryRead(out var packet))
                    batch[count++] = packet;
                lock (_gate)
                {
                    if (_disposeTask is not null || _state != MidiConnectionState.Open)
                        return;
                }
                _receiver!(batch.AsSpan(0, count));
                Array.Clear(batch, 0, count);
            }
        }
        catch (Exception error)
        {
            Fault(error);
        }
    }

    private void Disconnected(Native.IMidiEndpointConnectionSource sender, object args)
        => Fault(new MidiException($"MIDI endpoint '{EndpointInfo.Name}' was disconnected."));

    private void Fault(Exception error)
    {
        lock (_gate)
        {
            if (_disposeTask is not null || _state != MidiConnectionState.Open)
                return;
            var old = _state;
            _error = error;
            _state = MidiConnectionState.Faulted;
            _opened.TrySetResult(false);
            _received.Writer.TryComplete(error);
            _stateChanges.Writer.TryWrite(new(old, MidiConnectionState.Faulted, error));
        }
        Trace.TraceError("MIDI connection '{0}' failed: {1}", EndpointInfo.Id, error);
    }

    private async Task DispatchStateChanges()
    {
        await foreach (var change in _stateChanges.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var handlers = ConnectionStateChanged;
            if (handlers is null)
                continue;
            foreach (EventHandler<MidiConnectionStateChangedArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, change);
                }
                catch (Exception callbackError)
                {
                    Trace.TraceError("MIDI connection state callback failed: {0}", callbackError);
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _opened.TrySetResult(false);
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCore));
        }
    }

    private async Task DisposeCore()
    {
        await _sending.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                if ((Directions & MidiDirections.Receive) != 0)
                    _native.MessageReceived -= MessageReceived;
                _native.EndpointDeviceDisconnected -= Disconnected;
            }
            finally
            {
                try
                {
                    _remove(this, _id);
                }
                finally
                {
                    _received.Writer.TryComplete();
                }
            }
        }
        finally
        {
            _sending.Release();
            await _dispatch.ConfigureAwait(false);
            lock (_gate)
            {
                var old = _state;
                _state = MidiConnectionState.Closed;
                _stateChanges.Writer.TryWrite(new(old, MidiConnectionState.Closed, _error));
                _stateChanges.Writer.TryComplete();
            }
            await _stateDispatch.ConfigureAwait(false);
        }
    }
}
