# Message exchange

The message exchange transfers device settings between a value model and a MIDI device. The client works with
**schema nodes only**: it says, as a range of an execution plan (`PlanRange`), which data it wants to receive or
send, and with which **exchange**: a pattern of messages by name (e.g. `requestData`). The exchange file of the
range's root says how that pattern goes for the device; the root's [message map](../Messages/readme.md) (an
[address map](../Addressing/readme.md), or a dump) splits the range into blocks and writes and recognises the
messages. The exchange is a layer on top of the message maps: it knows nothing of what a message holds (e.g.
addresses).

Principles:

- The app decides *what* is transferred (the range), *how* (the exchange name) and *when* (the call). Neither the exchange file nor the messages decide which data is fetched or sent.
- Three responsibilities: the **app** (range, exchange name, source and sink), the **content** (the schemas, through the root's message map: what a message looks like, the blocks, when a block is complete) and the **exchange file** (the pattern: which message is sent, what is expected back, how long to wait, the time between messages, the outcome).
- References go one way: an exchange file names its root and the messages it uses; a schema never refers to an exchange file. A message has no kind in the schema: the same message can be used by several patterns.
- Before an exchange starts, the device is known (its device values, e.g. the Roland device id) and so is how to reach it (the MIDI session and endpoint). The exchange opens its own connection.

The design decisions are in [docs/decisions.md](../../../docs/decisions.md) (*Exchange definitions*); the open items
in the [Device todo](../todo.md) (*Message exchange patterns*).

## Client usage

```csharp
// once per application: the exchange files of the device assemblies (and files), and a factory on the MIDI session;
// exchanges open their own connections, the message maps are deduced from the serializer's schemas when a range is used
var exchanges = new ExchangeDefinitions();
exchanges.AddAssembly(typeof(Jacobi.Midi.Roland.Processors.ProcessorFactory).Assembly);   // returns the files it could not read
var factory = new MessageExchangeFactory(session, serializer, exchanges);
factory.PrepareAll();   // optional: create and validate all message maps up front

// once per connection to a device: what device (values the schema does not fix) and how to reach it
var device = new DeviceValues { ["deviceId"] = 0x00 };
await using var session = await factory.CreateSession(endpointId, device, options: null, sessionOptions: null, ct);
session.Disconnected += (_, _) => { /* the session is done: dispose it, connect again for a new one */ };
session.StateChanged += (_, args) => { /* args.Nodes changed: received, sent or invalidated */ };

// the client only works with plan ranges
var plan = serializer.GetPlan(new SchemaName("Gp8.AddressMap"));

// receive memory patch 6 (instance 5) into a value model (the standard exchange requestData)
var item = new InstancePath([5]);
var patch = plan.CreateRange("AddressMap.Memory.Patch", item, "AddressMap.Memory.Patch", item);
var result = await session.RequestData(patch, valueSink, ct);
// result.Blocks: per block whether it completed, timed out, was rejected or failed
// a second RequestData of the patch is answered by the session, without a request

// send one setting (sendData): the patch is atomic, so the session completes it from its state (requested from the
// device the first time) and sends the whole patch
var rate = plan.CreateRange("AddressMap.Temporary.Phaser.Rate", "AddressMap.Temporary.Phaser.Rate");
await session.SendData(rate, valueSource, ct);

// always a fresh copy from the device, for one call (or for all: DeviceSessionOptions.Data)
await session.RequestData(patch, valueSink, SessionData.Device, progress: null, ct);

// another exchange of the device's file, by its name
await session.Execute("requestDataHandshake", patch, new ExchangeData { Sink = valueSink }, progress: null, ct);

// the device changed values without telling (e.g. another patch was selected on the device)
session.Invalidate(plan.CreateRange("AddressMap.Temporary"));
```

An application that keeps its own copy of the device values can use a `MessageExchange` directly
(`factory.Create`) with `Execute(name, range, data, ct)`: it then works out itself what to receive and what to send in
one go (what the map transfers whole: atomic groups, a whole dump). `Validate(name, range, data)` lists the problems
that keep an exchange from running, without sending anything.

The value model sees the same paths and instances as with a normal read or write of the root's plan: the root, the
groups leading to the range (with their repeat items), then the fields. The message map may widen the range (an
address map to whole addresses and to the atomic groups it overlaps, a dump to the whole root): on receive, the sink
then also gets the other fields of the widened range.

## Exchange files

An exchange file holds the exchanges of one root, each a plain state machine of turns. It is XML
(`*.exchange.xml`) or JSON (`*.exchange.json`). The files of a device are grouped together in one folder (e.g.
`Boss/Gx700/` in the Roland assembly) and told apart by their sub-extension: `Gx700.schema.xml` is the schema,
`Gx700.exchange.xml` the exchange file. They are embedded as resources of the assembly that owns them
(`ExchangeDefinitions.AddAssembly`), or loaded as a file (`ExchangeDefinitions.AddFile`). A later file for the same root
replaces an earlier one. Loaders of embedded schemas skip these resources (`ExchangeDefinitions.IsDefinitionResource`).

The formats are described by [exchange.xsd](exchange.xsd) and [exchange.schema.json](exchange.schema.json); the rules
they cannot express are in their comments, and the reader checks them. Both formats have the same content: in JSON
the elements that repeat are arrays (`exchanges`, `turns`, `receive`, `on`), `send` and `receive` hold message
names, and the attributes are properties.

The files of the Roland devices are examples: [Gp8.exchange.xml](../../Jacobi.Midi.Roland/Gp8/Gp8.exchange.xml)
and [Gx700.exchange.xml](../../Jacobi.Midi.Roland/Boss/Gx700/Gx700.exchange.xml). The same `requestData` in JSON:

```json
{
  "for": "Gx700.AddressMap",
  "messageInterval": 60,
  "exchanges": [
    {
      "name": "requestData",
      "turns": [
        {
          "name": "request",
          "send": "Roland.Message32.RQ1",
          "receive": [ "Roland.Message32.DT1" ]
        }
      ]
    }
  ]
}
```

| Element | Attributes | Meaning |
| ------- | ---------- | ------- |
| `exchanges` | `for` (the root, e.g. `Gx700.AddressMap`), `responseTimeout` (ms, default 1000), `messageInterval` (ms, default 0) | The file. `messageInterval` is the least time between two messages sent to the device. |
| `exchange` | `name`, optional `responseTimeout`, `messageInterval` | One exchange; its timing overrides the file's. A standard name (`ExchangeNames`: `requestData`, `sendData`) or a name of the file's own. |
| `turn` | `name` | A turn in the conversation with the device; the first one is the start. On entry it sends its message (if any), then waits for the events it has an `on` for, or a default (see below). |
| `send` | `message` | The message to write for the block and send on entry: the schema name of the message group (document, root and path). |
| `receive` | `message` | A data message whose data is accepted for the block while in the turn, and counts towards `received`. |
| `on` | `event` (`sent`, `received`, `timeout`) or `message`; optional `retry` (1 or more, for `timeout` and messages); `goto` (a turn) or `result` (`completed`, `rejected`, `failed`, `timedOut`) | A transition; the first one that matches is taken. With `retry` it enters its turn again (sending its message again) at most that many times per block, then its `goto` or `result` is taken. A message without an `on` (or `receive`) is ignored. |

**Defaults.** A turn without an `on` for an event gets the default transition for it (`ExchangeTurn.Transition`); an
`on` for the event replaces the default:

| Event | Default | When |
| ----- | ------- | ---- |
| `sent` | `result="completed"` | The turn sends and waits for nothing: it has no `receive` and no `on`. A turn that waits for something (e.g. an `on` for an ack message) waits for its events instead. |
| `received` | `result="completed"` | The turn has a `receive`. |
| `timeout` | `result="timedOut"` | Always. |

So a one-way request is a `send` and a `receive`, and a one-way write is just a `send`.

The reader (`ReadXml`, `ReadJson`, or `Read` from a stream by the extension) turns both formats into the same tree
first, so the checks are the same: it checks the file on its own (elements, attributes, names, unknown and
unreachable turns, `sent` without a `send`, `received` without a `receive`, `retry` on `sent` or `received`) and reports all problems, located by line
(XML) or path (JSON), in an `ExchangeDefinitionException`. When an exchange
is used, the exchange checks it against the call (`ExchangeException`, before anything is sent): the messages exist
and fit the root's message map, the messages it receives carry data, there is a message to split the range for, a
source or sink is given when it sends or receives data; and it asks the message maps (`MessageMaps.Validate`) whether
the range can be transferred with its messages: the device values they need are known, and the map can split the
range for the split message (an address map: a message that asks for data, with a role `address`, or carries data).

## How a range is transferred

1. **Finding the exchange:** the file whose `for` is the root of the range's plan, and in it the exchange with the name.
2. **Blocks:** the root's message map splits the range into blocks for the **split message**: the first message the exchange sends, else the first data message it receives. The map may widen the range first. An address map widens to the atomic groups the range overlaps and splits at unmapped gaps, at the message's `midi.maxDataSize`, per field or per atomic group (`midi.unit`), never inside an atomic group; blocks for a data message leave out read-only fields, other blocks write-only fields. A dump is one block: the whole root.
3. **Per block** the exchange runs from its first turn (`ExchangeMachine`); `MessageExchange` executes it:
   - `send`: the map writes the message for the block (e.g. the address and size of an address map, the device values, the data from the source); the runner keeps the `messageInterval` since the previous message to the device, then sends it.
   - waiting: a message from the device is recognised by the map against the messages the exchange names (data inside the block, or a message without data its transitions wait for); data that a turn `receive`s is accepted and the timeout restarts; `received` follows when the map says the block has all its data.
   - the run ends with a `result`; the runner writes the reason (e.g. *"timeout in turn 'request' of 'requestData'."*, with *", after 2 retries"* when its retries were used). The accepted data of a completed block is decoded into the sink, in the order it arrived.
4. **The first block that does not complete ends the transfer**: the blocks after it are reported with the same status, as not transferred.

**Listen:** data messages the device sends on its own are decoded into the sink when they overlap the range, until
the call is cancelled. Data that belongs to a running transfer is not also delivered to a listener.

Transfers to a device run **one at a time**, so a message without data (an ack) belongs to the running transfer.

## Device session

A `DeviceSession` is the API for an application: it only says which nodes it is interested in, and the session makes
the transfer work. It gets all it needs when it is created, and keeps the **state of the device** per root for as long
as the connection lasts: an `IDeviceState` the root's message map creates, which decides what it holds and what a send
needs (an address map per field, with the access of each field and the atomic groups; a dump as a whole). The session
only orchestrates: it answers from the state or fetches, sends, and raises `StateChanged`.

- **`RequestData`:** per block of the range, the values come from the state when it holds them all (for an address map: write-only fields aside); otherwise the block is requested with `requestData` and the state updated. The sink gets the values of the blocks (also the other fields the map widened the range with). The first block that does not complete ends the transfer.
- **`SendData`:** the client's values of the range are taken as they are. What else the send needs comes from the state (an address map: the other fields of the atomic groups that are sent and can be received); what it does not hold is requested first (`requestData`). When that fails, nothing is sent and the result holds the blocks that could not be received. The client's values then go into the state (an address map leaves out read-only fields: the device keeps its own) and the range is sent from the state with `sendData`. A block that did not complete is invalidated: the device's values are no longer known. A dump is sent whole from the client's values (see [Messages](../Messages/readme.md)).
- **`Execute`:** another exchange of the device's file, with the state: one that sends data runs as `SendData`, one that receives data always requests and updates the state; one that does neither runs as it is. One that both sends and receives data is not supported yet (`NotSupportedException`).
- **`SessionData`:** `Session` (the default) uses the state as above; `Device` always requests (a fresh copy for a receive, a read-modify-write for a send) and updates the state. Per session (`DeviceSessionOptions.Data`) and per call.
- **Data the device sends on its own:** the first use of a root starts listening to all of it; what arrives goes into the state.
- **`Invalidate(range)` / `Clear()`:** forget values that the device changed without telling (the state may widen the range: an address map to whole addresses, a dump to the whole root); the next transfer requests them.
- **`Probe(range)`:** requests the range with `requestData` and discards it: a check that the device answers, without changing anything.
- **`StateChanged`:** the nodes that changed and how: `Received` (requested or sent by the device), `Sent`, `Invalidated`. Raised on the caller's flow, or on a background thread for data the device sends on its own.
- **Connection:** when the connection is lost (`Closed` or `Faulted`), the session is disconnected for good: `IsConnected` is false, `Disconnected` is raised once, and every call throws. The client disposes it and creates a new session; the state is not carried over.

## Types

Namespace `Jacobi.Midi.Device.Exchange`.

| Type | Purpose |
| ---- | ------- |
| `MessageExchangeFactory` | Creates sessions (`CreateSession`) and exchanges (`Create`) on an `IMidiSession` with the serializer whose schemas describe the devices and the exchange files; each gets its own connection. `Prepare(root)` / `PrepareAll()` create and validate message maps up front. |
| `DeviceSession` | `RequestData`, `SendData`, `Execute`, `Probe`, `Invalidate` and `Clear` for one connected device, with the state of the device kept between calls. `IsConnected`, `Disconnected` and `StateChanged`. Lives as long as its connection; disposing it closes the connection. |
| `DeviceSessionOptions`, `SessionData`, `DeviceStateChangedEventArgs`, `DeviceStateChange` | Where a transfer takes the values the client does not give, and what changed in the state. |
| `MessageExchange` | The runner: `Execute`, `Validate` and `Listen` for one device (connection and device values), without state; a device with several roots works through one exchange. `ConnectionState` and `ConnectionStateChanged`. Disposing it closes the connection. |
| `MessageExchangeOptions` | Runtime settings that belong neither in the schema nor in the exchange file: the UMP `Groups` (a `MidiGroupPair`: the group to send on and the group to receive on). |
| `ExchangeData` | What an exchange needs from the app: a `Source` for data that is sent, a `Sink` for data that is received. |
| `ExchangeNames` | The standard names of the most important patterns: `requestData`, `sendData`. |
| `ExchangeDefinitions`, `ExchangeDefinitionSet`, `ExchangeDefinition`, `ExchangeTurn`, `ExchangeTransition`, `ExchangeEvent` | The exchange files by root, and their content. |
| `ExchangeDefinitionReader`, `ExchangeDefinitionException` | Reads an exchange file (XML or JSON) and reports all its problems. |
| `ExchangeException` | An exchange that cannot run for a range, with all problems; thrown before anything is sent. |
| `TransferResult`, `BlockResult`, `BlockStatus` | The outcome per block (the block is the `PlanRange` of the nodes one message asks for or carries): `Completed`, `TimedOut`, `Rejected` or `Failed`, with the `Reason` the runner writes and the exception, if any. `Execute` also reports each block to an optional `IProgress<BlockResult>`. |
| `DeviceIdentity` | A universal Identity Request on an endpoint and UMP group, over a connection of its own, and the Identity Replies that arrive within a wait. Independent of the schemas (a test whether a device is there); older devices do not answer it. |

## Transport

The exchange sends and receives complete SysEx messages (F0 … F7) through an internal `ISysExPort`. The
connection-based port splits outgoing messages into SysEx7 packets on the options' UMP group and assembles incoming
packets with a `SysEx7Assembler`; its packet handler is the `MidiPacketReceivedHandler` the factory passes to
`IMidiSession.Connect`. Tests use a fake port that plays the device.

The Schema Editor's *Send* does not go through the runner yet: it keeps the `messageInterval` of the root's
`sendData` exchange between its messages.

## Not yet supported

See the [Device todo](../todo.md) (*Message exchange patterns*):
messages once around the blocks (Yamaha Bulk Header/Footer, a store request after the data), prompts for a manual
operation on the device, two levels of blocks (the Roland handshake for sending), patterns the device starts,
commands without data and without a range, a choice between messages per block, and areas of the session state that
depend on others.
