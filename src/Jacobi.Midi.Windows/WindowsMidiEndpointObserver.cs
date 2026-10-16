using System.Diagnostics;
using System.Threading.Channels;
using Jacobi.Midi.Core;
using Windows.Devices.Enumeration;
using Native = Windows.Devices.Midi2.Enumeration;

namespace Jacobi.Midi.Windows;

internal sealed class WindowsMidiEndpointObserver : IMidiEndpointObserver
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _reconciling = new(1, 1);
    private readonly EndpointChangedHandler _handler;
    private readonly Native.MidiEndpointDeviceWatcher _watcher;
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly TaskCompletionSource _enumerated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _dispatch;
    private IReadOnlyList<MidiEndpointInfo> _snapshot = Array.Empty<MidiEndpointInfo>();
    private Task? _disposeTask;
    private Exception? _failure;
    private bool _initialEnumerationCompleted;

    private WindowsMidiEndpointObserver(EndpointChangedHandler handler)
    {
        _handler = handler;
        _watcher = Native.MidiEndpointDeviceWatcher.Create(Native.MidiEndpointDeviceInformationFilters.AllStandardEndpoints)
            ?? throw new MidiException("Windows MIDI Services could not create an endpoint watcher.");
        _watcher.Added += Added;
        _watcher.Updated += Updated;
        _watcher.Removed += Removed;
        _watcher.EnumerationCompleted += EnumerationCompleted;
        _watcher.Stopped += Stopped;
        _dispatch = Task.Run(Dispatch);
    }

    internal static Task<WindowsMidiEndpointObserver> Create(EndpointChangedHandler handler, CancellationToken ct)
        => Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var observer = new WindowsMidiEndpointObserver(handler);
            var succeeded = false;
            try
            {
                observer._watcher.Start();
                await observer._enumerated.Task.WaitAsync(ct).ConfigureAwait(false);
                succeeded = true;
                return observer;
            }
            finally
            {
                if (!succeeded)
                    await observer.DisposeAsync().ConfigureAwait(false);
            }
        }, ct);

    public IReadOnlyList<MidiEndpointInfo> Endpoints => Volatile.Read(ref _snapshot);

    public Task WaitForCompletion(CancellationToken ct) => _dispatch.WaitAsync(ct);

    public Task Refresh(CancellationToken ct) => Task.Run(() => Reconcile(ct), ct);

    private void Added(Native.MidiEndpointDeviceWatcher sender, Native.MidiEndpointDeviceInformationAddedEventArgs args)
        => _changes.Writer.TryWrite(true);

    private void Updated(Native.MidiEndpointDeviceWatcher sender, Native.MidiEndpointDeviceInformationUpdatedEventArgs args)
        => _changes.Writer.TryWrite(true);

    private void Removed(Native.MidiEndpointDeviceWatcher sender, Native.MidiEndpointDeviceInformationRemovedEventArgs args)
        => _changes.Writer.TryWrite(true);

    private void EnumerationCompleted(Native.MidiEndpointDeviceWatcher sender, object args)
    {
        Volatile.Write(ref _initialEnumerationCompleted, true);
        _changes.Writer.TryWrite(true);
    }

    private void Stopped(Native.MidiEndpointDeviceWatcher sender, object args)
    {
        _stopped.TrySetResult();
        lock (_gate)
        {
            if (_disposeTask is null)
                Fail(new MidiException("Windows MIDI endpoint observation stopped unexpectedly."));
        }
    }

    private async Task Reconcile(CancellationToken ct)
    {
        await _reconciling.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
                ThrowIfUnavailable();
            var initialEnumerationCompleted = Volatile.Read(ref _initialEnumerationCompleted);
            var next = _watcher.EnumeratedEndpointDevices.Values
                .Select(WindowsMidiEndpointMapper.Map)
                .OrderBy(endpoint => endpoint.Name, StringComparer.Ordinal)
                .ThenBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ct.ThrowIfCancellationRequested();
            var previous = Endpoints.ToDictionary(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase);
            var current = next.ToDictionary(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase);
            var notifications = new List<MidiEndpointChangedArgs>();
            foreach (var endpoint in previous.Values)
            {
                if (!current.ContainsKey(endpoint.Id))
                    notifications.Add(new(MidiEndpointChangeType.Removed, endpoint));
            }
            foreach (var endpoint in next)
            {
                if (!previous.TryGetValue(endpoint.Id, out var old))
                    notifications.Add(new(MidiEndpointChangeType.Added, endpoint));
                else if (!WindowsMidiEndpointMapper.Equivalent(old, endpoint))
                    notifications.Add(new(MidiEndpointChangeType.Updated, endpoint));
            }
            lock (_gate)
            {
                ThrowIfUnavailable();
                Volatile.Write(ref _snapshot, Array.AsReadOnly(next));
            }
            foreach (var notification in notifications)
            {
                lock (_gate)
                {
                    if (_disposeTask is not null)
                        return;
                }
                _handler(notification);
            }
            if (initialEnumerationCompleted)
                _enumerated.TrySetResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException) when (_disposeTask is not null)
        {
            throw;
        }
        catch (Exception error)
        {
            lock (_gate)
                Fail(error);
            throw;
        }
        finally
        {
            _reconciling.Release();
        }
    }

    private async Task Dispatch()
    {
        await foreach (var change in _changes.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate)
            {
                if (_disposeTask is not null)
                    return;
            }
            try
            {
                await Reconcile(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (_disposeTask is not null)
            {
                return;
            }
        }
    }

    private void Fail(Exception error)
    {
        if (_failure is not null)
            return;
        _failure = error;
        Trace.TraceError("MIDI endpoint observer failed: {0}", error);
        _enumerated.TrySetException(error);
        _changes.Writer.TryComplete(error);
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
        if (_failure is not null)
            throw new MidiException("MIDI endpoint observation has failed.", _failure);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCore));
    }

    private async Task DisposeCore()
    {
        try
        {
            var status = _watcher.Status;
            if (status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                _watcher.Stop();
                await _stopped.Task.ConfigureAwait(false);
            }
            else if (status == DeviceWatcherStatus.Stopping)
                await _stopped.Task.ConfigureAwait(false);
        }
        finally
        {
            _watcher.Added -= Added;
            _watcher.Updated -= Updated;
            _watcher.Removed -= Removed;
            _watcher.EnumerationCompleted -= EnumerationCompleted;
            _watcher.Stopped -= Stopped;
            _changes.Writer.TryComplete();
            await _reconciling.WaitAsync().ConfigureAwait(false);
            _reconciling.Release();
        }
        await _dispatch.ConfigureAwait(false);
    }
}
