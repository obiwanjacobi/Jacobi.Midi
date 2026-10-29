using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// The built-in exchange protocols by name (the values of <c>midi.exchange</c>).
/// </summary>
public static class ExchangeProtocols
{
    /// <summary>
    /// Requests are answered with data messages; sent data is not acknowledged (Roland "one way" RQ1/DT1, Yamaha).
    /// </summary>
    public static IExchangeProtocol OneWay { get; } = new OneWayProtocol();

    /// <summary>
    /// Requests are answered with data messages (or a nak); sent data is acknowledged with an ack or nak, and the next
    /// message is only sent after the acknowledgement (Korg DATA LOAD COMPLETED / ERROR).
    /// </summary>
    public static IExchangeProtocol Acknowledged { get; } = new AcknowledgedProtocol();

    /// <summary>The built-in protocol with the name; null when there is none.</summary>
    public static IExchangeProtocol? Find(string name)
        => name switch
        {
            _ when String.Equals(name, AddressProtocol.OneWay, StringComparison.OrdinalIgnoreCase) => OneWay,
            _ when String.Equals(name, AddressProtocol.Acknowledged, StringComparison.OrdinalIgnoreCase) => Acknowledged,
            _ => null,
        };

    private sealed class OneWayProtocol : IExchangeProtocol
    {
        public ITransferMachine Receive(Transfer transfer, TransferSettings settings) => new RequestReplyMachine(transfer, settings);
        public ITransferMachine Send(Transfer transfer, TransferSettings settings) => new SendOnlyMachine(transfer);
    }

    private sealed class AcknowledgedProtocol : IExchangeProtocol
    {
        public ITransferMachine Receive(Transfer transfer, TransferSettings settings) => new RequestReplyMachine(transfer, settings);
        public ITransferMachine Send(Transfer transfer, TransferSettings settings) => new AcknowledgedSendMachine(transfer, settings);
    }
}

/// <summary>
/// Receives a block: sends the request, then collects the data messages that fall inside the block until all its
/// addresses are covered (a device may answer with several messages). A nak or error from the device ends the
/// transfer; so does the timeout, which restarts with every accepted data message.
/// </summary>
internal sealed class RequestReplyMachine(Transfer transfer, TransferSettings settings) : ITransferMachine
{
    private readonly bool[] _covered = new bool[transfer.Block.Addresses.Size];
    private bool _pausing;

    public ExchangeAction Start() => new SendMessage(transfer.Message);

    public ExchangeAction OnSent() => new WaitForMessage(settings.ResponseTimeout);

    public ExchangeAction OnMessage(ExchangeMessage message)
    {
        switch (message.Kind)
        {
            case ExchangeMessageKind.Nak:
                return new Complete(BlockStatus.Rejected, $"The device rejected the request ({message.Message.Name}).");
            case ExchangeMessageKind.Error:
                return new Complete(BlockStatus.Failed, $"The device reported an error for the request ({message.Message.Name}).");
            case ExchangeMessageKind.Data when message.Data is { } data:
                var overlap = transfer.Block.Addresses.Intersect(data.Block.Addresses);
                if (overlap.IsEmpty)
                {
                    return new Ignore();
                }
                for (var address = overlap.Start; address < overlap.End; address++)
                {
                    _covered[address - transfer.Block.Addresses.Start] = true;
                }
                if (!_covered.All(c => c))
                {
                    return new WaitForMessage(settings.ResponseTimeout);
                }
                return Finish();
            default:
                return new Ignore();
        }
    }

    public ExchangeAction OnElapsed()
        => _pausing ? new Complete(BlockStatus.Completed) : new Complete(BlockStatus.TimedOut);

    private ExchangeAction Finish()
    {
        if (transfer.Message.Interval > TimeSpan.Zero)
        {
            _pausing = true;
            return new Pause(transfer.Message.Interval);
        }
        return new Complete(BlockStatus.Completed);
    }
}

/// <summary>
/// Sends a block without an answer from the device, then pauses for the interval of the message.
/// </summary>
internal sealed class SendOnlyMachine(Transfer transfer) : ITransferMachine
{
    public ExchangeAction Start() => new SendMessage(transfer.Message);

    public ExchangeAction OnSent()
        => transfer.Message.Interval > TimeSpan.Zero
            ? new Pause(transfer.Message.Interval)
            : new Complete(BlockStatus.Completed);

    public ExchangeAction OnMessage(ExchangeMessage message) => new Ignore();

    public ExchangeAction OnElapsed() => new Complete(BlockStatus.Completed);
}

/// <summary>
/// Sends a block and waits for the device to acknowledge it: an ack completes the transfer (after the interval of
/// the message), a nak rejects it, an error or the timeout fails it.
/// </summary>
internal sealed class AcknowledgedSendMachine(Transfer transfer, TransferSettings settings) : ITransferMachine
{
    private bool _pausing;

    public ExchangeAction Start() => new SendMessage(transfer.Message);

    public ExchangeAction OnSent() => new WaitForMessage(settings.ResponseTimeout);

    public ExchangeAction OnMessage(ExchangeMessage message)
    {
        switch (message.Kind)
        {
            case ExchangeMessageKind.Ack:
                if (transfer.Message.Interval > TimeSpan.Zero)
                {
                    _pausing = true;
                    return new Pause(transfer.Message.Interval);
                }
                return new Complete(BlockStatus.Completed);
            case ExchangeMessageKind.Nak:
                return new Complete(BlockStatus.Rejected, $"The device rejected the data ({message.Message.Name}).");
            case ExchangeMessageKind.Error:
                return new Complete(BlockStatus.Failed, $"The device reported an error for the data ({message.Message.Name}).");
            default:
                return new Ignore();
        }
    }

    public ExchangeAction OnElapsed()
        => _pausing
            ? new Complete(BlockStatus.Completed)
            : new Complete(BlockStatus.TimedOut, "The device did not acknowledge the data.");
}
