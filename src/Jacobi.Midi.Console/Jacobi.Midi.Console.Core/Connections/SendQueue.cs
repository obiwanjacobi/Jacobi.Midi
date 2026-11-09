using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Device.Exchange;

namespace Jacobi.Midi.Console.Core.Connections;

/// <summary>
/// The changes waiting to be sent to one connected device (see <see cref="RigConnections.Send"/>). A parameter is
/// queued at most once per exchange: one that changes again before it was sent is sent once, with its value at that
/// moment (read from the device state when it is sent), so a dragged knob sends its latest value, not every step. One
/// send at a time, in the order the parameters were first queued; the exchange keeps the time between the messages
/// (its <c>messageInterval</c>). Closing it drops what was not sent yet.
/// </summary>
internal sealed class SendQueue(DeviceSession session, Action<SendResult> completed)
{
    private readonly Lock _lock = new();
    private readonly List<(string Exchange, ParameterReference Parameter, DeviceState State)> _pending = [];
    private readonly CancellationTokenSource _closing = new();
    private bool _isSending;

    /// <summary>Queues the parameter; the send starts at once when nothing is being sent.</summary>
    public void Enqueue(string exchange, ParameterReference parameter, DeviceState state)
    {
        lock (_lock)
        {
            if (_closing.IsCancellationRequested)
                return;
            if (!_pending.Any(entry => entry.Exchange == exchange && entry.Parameter == parameter))
                _pending.Add((exchange, parameter, state));
            if (_isSending)
                return;
            _isSending = true;
        }
        _ = Task.Run(SendAll);
    }

    /// <summary>Drops what was not sent and ends a send that is running; nothing is reported for them.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _pending.Clear();
            _closing.Cancel();
        }
    }

    private async Task SendAll()
    {
        while (true)
        {
            (string Exchange, ParameterReference Parameter, DeviceState State) next;
            lock (_lock)
            {
                if (_pending.Count == 0 || _closing.IsCancellationRequested)
                {
                    _isSending = false;
                    return;
                }
                next = _pending[0];
                _pending.RemoveAt(0);
            }
            if (await Send(next.Exchange, next.Parameter, next.State) is { } result)
                completed(result);
        }
    }

    // null when the queue was closed during the send
    private async Task<SendResult?> Send(string exchange, ParameterReference parameter, DeviceState state)
    {
        try
        {
            var range = state.Plan.CreateRange(parameter.Path, parameter.Instance, parameter.Path, parameter.Instance);
            var result = exchange == ExchangeNames.SendData
                ? await session.SendData(range, state.Source, _closing.Token)
                : await session.Execute(exchange, range, new ExchangeData { Source = state.Source }, progress: null, _closing.Token);
            return new SendResult(parameter, exchange, result);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            // e.g. an exchange that cannot run (ExchangeException) or a lost connection: reported, not thrown
            return new SendResult(parameter, exchange, null) { Error = exception };
        }
    }
}

/// <summary>
/// The outcome of sending one parameter to a device (see <see cref="RigConnections.Send"/>): the transfer per block,
/// or the exception when the exchange could not run.
/// </summary>
/// <param name="Parameter">The parameter that was sent.</param>
/// <param name="Exchange">The exchange it was sent with.</param>
/// <param name="Result">The transfer; null when it could not run (see <see cref="Error"/>).</param>
public sealed record SendResult(ParameterReference Parameter, string Exchange, TransferResult? Result)
{
    /// <summary>Why the exchange could not run; null when it ran.</summary>
    public Exception? Error { get; init; }

    /// <summary>True when every block was sent.</summary>
    public bool IsComplete => Result?.IsComplete == true;
}
