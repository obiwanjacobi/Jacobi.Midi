# Message exchange

The message exchange transfers device settings between a value model and a MIDI device. The client works with
**schema nodes only**: it says, as a range of an execution plan (`PlanRange`), which data it wants to receive or
send. The exchange deduces the [address map](../Addressing/readme.md) and the vendor messages from the schemas of the
range's plan, cuts the range into blocks and transfers each block with the device's **exchange protocol**.

Principles:

- The API assumes nothing but the `PlanRange` it gets, plus the value model for it. Every transfer, including listening for data the device sends on its own, starts with a range and ends when the range is exhausted (for a send: when the source has supplied the last logical field of the range).
- Before an exchange starts, the device is known (its device values, e.g. the Roland device id) and so is how to reach it (the MIDI session and endpoint). The exchange opens its own connection.
- The exchange patterns (which messages go back and forth, and when a transfer is done) are separate from the content (how a message maps to schema nodes). The common patterns are implemented in code; the model is shaped so that a configurable state machine can be added later without changing the API.

## Client usage

```csharp
// once per application: exchanges open their own connections on the session; the address maps are deduced
// from the serializer's schemas when a range is used (and cached per plan)
var factory = new MessageExchangeFactory(session, serializer);
factory.PrepareAll();   // optional: build and validate all address maps up front

// once per connection to a device: what device (values the schema does not fix) and how to reach it
var device = new DeviceValues { ["deviceId"] = 0x00 };
await using var session = await factory.CreateSession(endpointId, device, options: null, sessionOptions: null, ct);
session.Disconnected += (_, _) => { /* the session is done: dispose it, connect again for a new one */ };
session.StateChanged += (_, args) => { /* args.Nodes changed: received, sent or invalidated */ };

// the client only works with plan ranges
var plan = serializer.GetPlan(new SchemaName("Gp8.AddressMap"));

// receive memory patch 6 (instance 5) into a value model
var item = new InstancePath([5]);
var patch = plan.CreateRange("AddressMap.Memory.Patch", item, "AddressMap.Memory.Patch", item);
var result = await session.Receive(patch, valueSink, ct);
// result.Blocks: per block whether it completed, timed out, was rejected or failed
// a second Receive of the patch is answered by the session, without a request

// send one setting: the patch is atomic, so the session completes it from its state (requested from the device
// the first time) and sends the whole patch
var rate = plan.CreateRange("AddressMap.Temporary.Phaser.Rate", "AddressMap.Temporary.Phaser.Rate");
await session.Send(rate, valueSource, ct);

// always a fresh copy from the device, for one call (or for all: DeviceSessionOptions.Data)
await session.Receive(patch, valueSink, SessionData.Device, progress: null, ct);

// the device changed values without telling (e.g. another patch was selected on the device)
session.Invalidate(plan.CreateRange("AddressMap.Temporary"));
```

An application that keeps its own copy of the device values can use a `MessageExchange` directly
(`factory.Create`): it then works out itself what to receive and what to send in one go (whole atomic groups).

The value model sees the same paths and instances as with a normal read or write of the map plan: the root, the groups
leading to the range (with their repeat items), then the fields. The range may be widened to whole addresses and to
the atomic groups it overlaps: on receive, the sink then also gets the other fields of those groups.

## Types

Namespace `Jacobi.Midi.Device.Exchange`.

| Type | Purpose |
|------|---------|
| `MessageExchangeFactory` | Creates sessions (`CreateSession`) and exchanges (`Create`) on an `IMidiSession` with the serializer whose schemas describe the devices; each gets its own connection. `Prepare(root)` / `PrepareAll()` build and validate address maps up front (`PrepareAll` finds the roots a data message can carry and throws an `AddressMapException` for the first invalid one, like `Serializer.PrepareAll`). |
| `DeviceSession` | `Receive`, `Send`, `Invalidate` and `Clear` for one connected device, with the state of the device kept between calls (see below); `Probe` requests a range and discards it (a check that the device answers, without changing anything). `IsConnected`, `Disconnected` and `StateChanged`. Lives as long as its connection; disposing it closes the connection. |
| `DeviceSessionOptions`, `SessionData`, `DeviceStateChangedEventArgs`, `DeviceStateChange` | Where a transfer takes the values the client does not give (`Session`: the state, `Device`: always a fresh copy), and what changed in the state. |
| `MessageExchange` | The transport of a session: `Receive`, `Send` and `Listen` for one device (connection and device values), without state; a device with several map roots works through one exchange. `ConnectionState` and `ConnectionStateChanged`. Disposing it closes the connection. |
| `MessageExchangeOptions` | Runtime settings that do not belong in the schema: the UMP `Groups` (a `MidiGroupPair`: the group to send on and the group to receive on), the `ResponseTimeout`, and a `Protocol` that replaces the one the schemas select. |
| `TransferResult`, `BlockResult`, `BlockStatus` | The outcome per block: `Completed`, `TimedOut`, `Rejected` (a nak) or `Failed`, with the `Reason` the protocol gives and the exception, if any. `Receive` and `Send` also report each block to an optional `IProgress<BlockResult>`. |
| `IExchangeProtocol`, `ITransferMachine`, `ExchangeAction`, `Transfer`, `ExchangeMessage`, `TransferSettings` | The exchange protocol model (see below). |
| `ExchangeProtocols` | The built-in protocols. |
| `DeviceIdentity` | A universal Identity Request on an endpoint and UMP group, over a connection of its own, and the Identity Replies that arrive within a wait. Independent of the schemas (a test whether a device is there); older devices do not answer it. |

## How a range is transferred

**Choosing the messages.** The device values select the messages: a message is only used if its `device.<name>`
roles have values. For the range, the exchange picks the message that needs the fewest messages: a `range` message
where one exists, else `atomic` messages for the atomic groups and `field` messages for the remaining fields. The
address map then splits the range into the blocks for that message.

**Receive:**

1. The range is widened to the atomic groups it overlaps.
2. Each block is transferred by the receive machine of the protocol: the request is sent, and the data messages that fall inside the block are collected until all its addresses are covered (a device may answer one request with several messages, e.g. Yamaha 512-byte packets).
3. When the machine completes, the collected data is decoded into the sink in address order.

**Send:**

1. The range is widened to the atomic groups it overlaps; the source must supply all fields that are sent (completing the groups is the session's job).
2. Each block is transferred by the send machine of the protocol: the data message is built from the values and sent, and the machine decides whether to wait for an acknowledgement.

**Listen:**

- Data messages the device sends on its own are decoded into the sink when they overlap the range, until the call is cancelled. Data that belongs to a running transfer is not also delivered to a listener.

Transfers to a device run **one at a time**, so a reply without an address (ack, nak) belongs to the running transfer.

## Device session

A `DeviceSession` is the API for an application: it only says which nodes it is interested in, and the session makes
the transfer work. It keeps the **state of the device** per address map (field values by path and instance of the map
plan) for as long as the connection lasts.

- **Receive:** per block of the widened range, the values come from the state when it holds them all (write-only fields aside); otherwise the block is requested and the state updated. The sink gets the values of the blocks (also the other fields of the atomic groups).
- **Send:** the client's values of the range are taken as they are. The other fields of the atomic groups (the fields that are sent and can be received) come from the state; what it does not hold is requested first. When that fails, nothing is sent and the result holds the blocks that could not be received. The client's values then go into the state (read-only fields aside: the device keeps its own) and the widened range is sent from the state. A block that did not complete is invalidated: the device's values are no longer known.
- **`SessionData`:** `Session` (the default) uses the state as above; `Device` always requests (a fresh copy for a receive, a read-modify-write for a send) and updates the state. Per session (`DeviceSessionOptions.Data`) and per call.
- **Data the device sends on its own:** the first use of a map starts listening to all of it; what arrives goes into the state.
- **`Invalidate(range)` / `Clear()`:** forget values (widened to whole addresses) that the device changed without telling; the next transfer requests them.
- **`StateChanged`:** the nodes that changed and how: `Received` (requested or sent by the device), `Sent`, `Invalidated`. Raised on the caller's flow, or on a background thread for data the device sends on its own.
- **Connection:** when the connection is lost (`Closed` or `Faulted`), the session is disconnected for good: `IsConnected` is false, `Disconnected` is raised once, and every call throws. The client disposes it and creates a new session; the state is not carried over.

## Exchange protocols

A transfer of one block is run by an `ITransferMachine`, a state machine that only gets events and answers with the
next action. It never touches the connection, timers or message bytes:

```csharp
public union ExchangeAction(SendMessage, WaitForMessage, Pause, Ignore, Complete);

public interface ITransferMachine
{
    ExchangeAction Start();
    ExchangeAction OnSent();                              // the message of the last SendMessage was sent
    ExchangeAction OnMessage(ExchangeMessage message);    // a recognised message from the device: kind, message, data
    ExchangeAction OnElapsed();                           // the timeout of WaitForMessage / the duration of Pause elapsed
}

public interface IExchangeProtocol
{
    ITransferMachine Receive(Transfer transfer, TransferSettings settings);
    ITransferMachine Send(Transfer transfer, TransferSettings settings);
}
```

| Action | The exchange |
|--------|--------------|
| `SendMessage` | builds the request or data message for the block and sends it, then calls `OnSent` |
| `WaitForMessage` | waits for a recognised message (`OnMessage`) or the timeout (`OnElapsed`) |
| `Pause` | waits the duration (e.g. the interval a device needs between messages), then calls `OnElapsed`; messages meanwhile are not delivered |
| `Ignore` | the message is not the machine's business; keeps waiting until the earlier deadline |
| `Complete` | ends the transfer with a status and reason; a completed receive decodes the data the machine accepted |

`MessageExchange` is the runner of these machines: it executes the actions and turns incoming SysEx into
`ExchangeMessage`s with the address map (data) and the envelope (ack, nak, error). Retries and multi-step handshakes
fit the same model: a machine counts its attempts and returns `SendMessage` again. A configurable (declarative) state
machine would be another `ITransferMachine` implementation.

**Selecting the protocol.** `midi.exchange` on the envelope root (or the map root) names a built-in protocol; without
it, an envelope with `ack`/`nak` replies is `acknowledged`, otherwise `oneWay`. `MessageExchangeOptions.Protocol`
replaces the selected protocol, for a device that needs something else.

**Built-in protocols** (`ExchangeProtocols`):

| Protocol | Receive | Send |
|----------|---------|------|
| `oneWay` (Roland "one way" RQ1/DT1, Yamaha) | send the request; collect the data messages until the block is covered (the timeout restarts with every message); nak → `Rejected`, error → `Failed`, timeout → `TimedOut`; then the interval of the request | send the data; then the interval of the message |
| `acknowledged` (Korg DATA LOAD COMPLETED / ERROR) | as `oneWay` | send the data and wait for the reply: ack → `Completed` (after the interval), nak → `Rejected`, error → `Failed`, timeout → `TimedOut` |

## Transport

The exchange sends and receives complete SysEx messages (F0 … F7) through an internal `ISysExPort`. The
connection-based port splits outgoing messages into SysEx7 packets on the options' UMP group and assembles incoming
packets with a `SysEx7Assembler`; its packet handler is the `MidiPacketReceivedHandler` the factory passes to
`IMidiSession.Connect`. Tests use a fake port that plays the device.

## Not yet supported

See [todo.md](../todo.md): the Roland handshake protocol (with retries), framed transactions (Yamaha Bulk
Header/Footer), commands without data (write/store requests), device modes, a hook for a manual operation on the
device (e.g. "press the bulk dump key"), and areas of the session state that depend on others.
