using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// One block to transfer and the message that carries it: a request (receive) or a data message (send).
/// </summary>
public sealed record Transfer(AddressMessage Message, AddressBlock Block);

/// <summary>
/// A message from the device, recognised by the message exchange: its kind, the tagged message it matched, and for a
/// data message the data it carries.
/// </summary>
public readonly record struct ExchangeMessage(ExchangeMessageKind Kind, AddressMessage Message, ReceivedData? Data = null);

/// <summary>Action: send the message of the transfer (built by the exchange from the block and the values); then <see cref="ITransferMachine.OnSent"/> is called.</summary>
public readonly record struct SendMessage(AddressMessage Message);

/// <summary>Action: wait for a message from the device; <see cref="ITransferMachine.OnMessage"/> or, after the timeout, <see cref="ITransferMachine.OnElapsed"/> is called.</summary>
public readonly record struct WaitForMessage(TimeSpan Timeout);

/// <summary>Action: pause (e.g. the interval a device needs between messages); then <see cref="ITransferMachine.OnElapsed"/> is called. Messages received meanwhile are not delivered to the machine.</summary>
public readonly record struct Pause(TimeSpan Duration);

/// <summary>Action: the message does not belong to this transfer; the exchange keeps waiting until the earlier timeout.</summary>
public readonly record struct Ignore;

/// <summary>Action: the transfer is finished with this status.</summary>
public readonly record struct Complete(BlockStatus Status, string? Reason = null);

/// <summary>
/// What the message exchange does next for a transfer, as decided by its <see cref="ITransferMachine"/>.
/// </summary>
public union ExchangeAction(SendMessage, WaitForMessage, Pause, Ignore, Complete);

/// <summary>
/// The state machine of one transfer: it gets events and answers with the next action. It never touches the
/// connection, timers or message bytes, so a machine can be implemented in code (the built-in protocols) or later be
/// driven by a definition. A machine is used for one transfer, from one thread at a time.
/// </summary>
public interface ITransferMachine
{
    /// <summary>The first action of the transfer.</summary>
    ExchangeAction Start();

    /// <summary>The message of the last <see cref="SendMessage"/> action was sent.</summary>
    ExchangeAction OnSent();

    /// <summary>A message from the device arrived while waiting (<see cref="WaitForMessage"/>).</summary>
    ExchangeAction OnMessage(ExchangeMessage message);

    /// <summary>The timeout of a <see cref="WaitForMessage"/> or the duration of a <see cref="Pause"/> has elapsed.</summary>
    ExchangeAction OnElapsed();
}

/// <summary>
/// The settings an exchange protocol gets for a transfer.
/// </summary>
public sealed record TransferSettings(TimeSpan ResponseTimeout);

/// <summary>
/// An exchange pattern of a device: creates the state machines that receive and send one block. Selected by the
/// envelope's <c>midi.exchange</c> tag (or inferred from its message kinds), or given in the
/// <see cref="MessageExchangeOptions"/>.
/// </summary>
public interface IExchangeProtocol
{
    /// <summary>A machine that gets the data of the block from the device, with the request of the transfer.</summary>
    ITransferMachine Receive(Transfer transfer, TransferSettings settings);

    /// <summary>A machine that gives the data of the block to the device, with the data message of the transfer.</summary>
    ITransferMachine Send(Transfer transfer, TransferSettings settings);
}
