# Message exchange

The message exchange transfers device settings between a value model and a MIDI device. The client works with
**schema nodes only**: it says, as a range of an execution plan (`PlanRange`), which data it wants to receive or
send, and with which **exchange**: a pattern of messages by name (e.g. `requestData`). The exchange file of the
range's map root says how that pattern goes for the device; the [address map](../Addressing/readme.md) cuts the range
into blocks and builds and recognises the messages.

Principles:

- The app decides *what* is transferred (the range), *how* (the exchange name) and *when* (the call). Neither the exchange file nor the messages decide which data is fetched or sent.
- Three responsibilities: the **app** (range, exchange name, source and sink), the **content** (the schemas with the address map: what a message looks like, the blocks, when a block is complete) and the **exchange file** (the pattern: which message is sent, what is expected back, how long to wait, the time between messages, the outcome).
- References go one way: an exchange file names its map root and the messages it uses; a schema never refers to an exchange file. A message has no kind in the schema: the same message can be used by several patterns.
- Before an exchange starts, the device is known (its device values, e.g. the Roland device id) and so is how to reach it (the MIDI session and endpoint). The exchange opens its own connection.

The design and its later steps (states around the blocks, retries, values from replies, prompts) are in the
proposal: [exchange-definitions.md](exchange-definitions.md).

## Client usage

```csharp
// once per application: the exchange files of the device assemblies (and files), and a factory on the MIDI session;
// exchanges open their own connections, the address maps are deduced from the serializer's schemas when a range is used
var exchanges = new ExchangeDefinitions();
exchanges.AddAssembly(typeof(Jacobi.Midi.Roland.Processors.ProcessorFactory).Assembly);   // returns the files it could not read
var factory = new MessageExchangeFactory(session, serializer, exchanges);
factory.PrepareAll();   // optional: build and validate all address maps up front

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
one go (whole atomic groups). `Validate(name, range, data)` lists the problems that keep an exchange from running,
without sending anything.

The value model sees the same paths and instances as with a normal read or write of the map plan: the root, the groups
leading to the range (with their repeat items), then the fields. The range may be widened to whole addresses and to
the atomic groups it overlaps: on receive, the sink then also gets the other fields of those groups.

## Exchange files

An exchange file holds the exchanges of one map root, each a plain state machine. It is XML (`*.exchange.xml`) or
JSON (`*.exchange.json`), by convention in the `Exchange` folder of the assembly that owns it, embedded as a resource
(`ExchangeDefinitions.AddAssembly`), or a file (`ExchangeDefinitions.AddFile`). A later file for the same root
replaces an earlier one. Loaders of embedded schemas skip these resources (`ExchangeDefinitions.IsDefinitionResource`).

The formats are described by [exchange.xsd](exchange.xsd) and [exchange.schema.json](exchange.schema.json); the rules
they cannot express are in their comments, and the reader checks them. Both formats have the same content: in JSON
the elements that repeat are arrays (`exchanges`, `states`, `receive`, `on`), `send` and `receive` hold message
names, and the attributes are properties.

The files of the Roland devices are examples: [Gp8.exchange.xml](../../Jacobi.Midi.Roland/Exchange/Gp8.exchange.xml)
and [Gx700.exchange.xml](../../Jacobi.Midi.Roland/Exchange/Gx700.exchange.xml). The same `requestData` in JSON:

```json
{
  "for": "Gx700.AddressMap",
  "messageInterval": 60,
  "exchanges": [
    {
      "name": "requestData",
      "states": [
        {
          "name": "request",
          "send": "Roland.Message32.RQ1",
          "receive": [ "Roland.Message32.DT1" ],
          "on": [
            { "event": "complete", "result": "completed" },
            { "event": "timeout", "result": "timedOut" }
          ]
        }
      ]
    }
  ]
}
```

| Element | Attributes | Meaning |
| ------- | ---------- | ------- |
| `exchanges` | `for` (the map root, e.g. `Gx700.AddressMap`), `responseTimeout` (ms, default 1000), `messageInterval` (ms, default 0) | The file. `messageInterval` is the least time between two messages sent to the device. |
| `exchange` | `name`, optional `responseTimeout`, `messageInterval` | One exchange; its timing overrides the file's. A standard name (`ExchangeNames`: `requestData`, `sendData`) or a name of the file's own. |
| `state` | `name` | A state; the first one is the start. On entry it sends its message (if any), then waits for the events it has an `on` for. |
| `send` | `message` | The message to build for the block and send on entry: the schema name of the message group (document, root and path). |
| `receive` | `message` | A data message whose data is accepted for the block while in the state, and counts towards `complete`. |
| `on` | `event` (`sent`, `complete`, `timeout`) or `message`; `goto` (a state) or `result` (`completed`, `rejected`, `failed`, `timedOut`) | A transition; the first one that matches is taken. A message without an `on` (or `receive`) is ignored. A timeout without an `on` ends the run as timed out. |

The reader (`ReadXml`, `ReadJson`, or `Read` from a stream by the extension) turns both formats into the same tree
first, so the checks are the same: it checks the file on its own (elements, attributes, names, unknown and
unreachable states, `sent` without a `send`, `complete` without a `receive`) and reports all problems, located by line
(XML) or path (JSON), in an `ExchangeDefinitionException`. When an exchange
is used, the exchange checks it against the schemas and the call (`ExchangeException`, before anything is sent): the
messages exist and fit the map, there is exactly one message that asks for or carries data, the device values are
known, and a source or sink is given when it sends or receives data.

## How a range is transferred

1. **Finding the exchange:** the file whose `for` is the root of the range's plan, and in it the exchange with the name.
2. **Blocks:** the range is widened to the atomic groups it overlaps and cut into blocks for the **cutting message**: the one message the exchange sends that asks for data (a role `address`, no data) or carries data (a role `data`). The address map cuts at unmapped gaps, at the message's `midi.maxDataSize`, per field or per atomic group (`midi.unit`), never inside an atomic group; blocks for a data message leave out read-only fields, other blocks write-only fields.
3. **Per block** the exchange runs from its first state (`ExchangeMachine`); `MessageExchange` executes it:
   - `send`: the address map builds the message for the block (address, size, device values, data from the source); the runner keeps the `messageInterval` since the previous message to the device, then sends it.
   - waiting: a message from the device is recognised against the messages the exchange names (data inside the block, or a message without data its transitions wait for); data that a state `receive`s is accepted and the timeout restarts; `complete` follows when the block has all its data.
   - the run ends with a `result`; the runner writes the reason (e.g. *"timeout in state 'request' of 'requestData'."*). The accepted data of a completed block is decoded into the sink, in address order.
4. **The first block that does not complete ends the transfer**: the blocks after it are reported with the same status, as not transferred.

**Listen:** data messages the device sends on its own are decoded into the sink when they overlap the range, until
the call is cancelled. Data that belongs to a running transfer is not also delivered to a listener.

Transfers to a device run **one at a time**, so a message without an address (an ack) belongs to the running transfer.

## Device session

A `DeviceSession` is the API for an application: it only says which nodes it is interested in, and the session makes
the transfer work. It gets all it needs when it is created, and keeps the **state of the device** per address map
(field values by path and instance of the map plan) for as long as the connection lasts.

- **`RequestData`:** per block of the widened range, the values come from the state when it holds them all (write-only fields aside); otherwise the block is requested with `requestData` and the state updated. The sink gets the values of the blocks (also the other fields of the atomic groups). The first block that does not complete ends the transfer.
- **`SendData`:** the client's values of the range are taken as they are. The other fields of the atomic groups (the fields that are sent and can be received) come from the state; what it does not hold is requested first (`requestData`). When that fails, nothing is sent and the result holds the blocks that could not be received. The client's values then go into the state (read-only fields aside: the device keeps its own) and the widened range is sent from the state with `sendData`. A block that did not complete is invalidated: the device's values are no longer known.
- **`Execute`:** another exchange of the device's file, with the state: one that sends data runs as `SendData`, one that receives data always requests and updates the state; one that does neither runs as it is.
- **`SessionData`:** `Session` (the default) uses the state as above; `Device` always requests (a fresh copy for a receive, a read-modify-write for a send) and updates the state. Per session (`DeviceSessionOptions.Data`) and per call.
- **Data the device sends on its own:** the first use of a map starts listening to all of it; what arrives goes into the state.
- **`Invalidate(range)` / `Clear()`:** forget values (widened to whole addresses) that the device changed without telling; the next transfer requests them.
- **`Probe(range)`:** requests the range with `requestData` and discards it: a check that the device answers, without changing anything.
- **`StateChanged`:** the nodes that changed and how: `Received` (requested or sent by the device), `Sent`, `Invalidated`. Raised on the caller's flow, or on a background thread for data the device sends on its own.
- **Connection:** when the connection is lost (`Closed` or `Faulted`), the session is disconnected for good: `IsConnected` is false, `Disconnected` is raised once, and every call throws. The client disposes it and creates a new session; the state is not carried over.

## Types

Namespace `Jacobi.Midi.Device.Exchange`.

| Type | Purpose |
| ---- | ------- |
| `MessageExchangeFactory` | Creates sessions (`CreateSession`) and exchanges (`Create`) on an `IMidiSession` with the serializer whose schemas describe the devices and the exchange files; each gets its own connection. `Prepare(root)` / `PrepareAll()` build and validate address maps up front. |
| `DeviceSession` | `RequestData`, `SendData`, `Execute`, `Probe`, `Invalidate` and `Clear` for one connected device, with the state of the device kept between calls. `IsConnected`, `Disconnected` and `StateChanged`. Lives as long as its connection; disposing it closes the connection. |
| `DeviceSessionOptions`, `SessionData`, `DeviceStateChangedEventArgs`, `DeviceStateChange` | Where a transfer takes the values the client does not give, and what changed in the state. |
| `MessageExchange` | The runner: `Execute`, `Validate` and `Listen` for one device (connection and device values), without state; a device with several map roots works through one exchange. `ConnectionState` and `ConnectionStateChanged`. Disposing it closes the connection. |
| `MessageExchangeOptions` | Runtime settings that belong neither in the schema nor in the exchange file: the UMP `Groups` (a `MidiGroupPair`: the group to send on and the group to receive on). |
| `ExchangeData` | What an exchange needs from the app: a `Source` for data that is sent, a `Sink` for data that is received. |
| `ExchangeNames` | The standard names of the most important patterns: `requestData`, `sendData`. |
| `ExchangeDefinitions`, `ExchangeDefinitionSet`, `ExchangeDefinition`, `ExchangeState`, `ExchangeTransition`, `ExchangeEvent` | The exchange files by map root, and their content. |
| `ExchangeDefinitionReader`, `ExchangeDefinitionException` | Reads an exchange file (XML or JSON) and reports all its problems. |
| `ExchangeException` | An exchange that cannot run for a range, with all problems; thrown before anything is sent. |
| `TransferResult`, `BlockResult`, `BlockStatus` | The outcome per block: `Completed`, `TimedOut`, `Rejected` or `Failed`, with the `Reason` the runner writes and the exception, if any. `Execute` also reports each block to an optional `IProgress<BlockResult>`. |
| `DeviceIdentity` | A universal Identity Request on an endpoint and UMP group, over a connection of its own, and the Identity Replies that arrive within a wait. Independent of the schemas (a test whether a device is there); older devices do not answer it. |

## Transport

The exchange sends and receives complete SysEx messages (F0 … F7) through an internal `ISysExPort`. The
connection-based port splits outgoing messages into SysEx7 packets on the options' UMP group and assembles incoming
packets with a `SysEx7Assembler`; its packet handler is the `MidiPacketReceivedHandler` the factory passes to
`IMidiSession.Connect`. Tests use a fake port that plays the device.

## Not yet supported

See [exchange-definitions.md](exchange-definitions.md) (the later steps) and [todo.md](../todo.md): states around the
blocks and retries (the Roland handshake, Yamaha Bulk Header/Footer), values read from replies (device modes, error
codes), prompts for a manual operation on the device, patterns the device starts, commands without data (write/store
requests), a choice between messages per block, and areas of the session state that depend on others.
