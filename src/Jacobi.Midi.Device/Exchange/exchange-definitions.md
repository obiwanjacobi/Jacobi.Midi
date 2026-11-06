# Exchange definitions (proposal)

*Status: proposal, revision 8, 2026-11-04. Stages 1 and 2 are implemented (see the [readme](readme.md)); stage 3 and later are not.*

A configurable, generic way to describe the message exchange of a device: which messages go back and forth, how long
to wait, and how an exchange ends. The design is built up in small stages, each usable on its own. Stages 1 and
2 are implemented; stages 3 and 4 are worked out (`before` and `after`, prompts); the later stages are named, so the
direction is visible. Revision 7 drops `forEach` and the handling of failed blocks of revision 6. Revision 8 renames
the event `complete` to `received` and adds the default transitions (see *The machine*). Stage 1 below keeps the
names of its time: its `state` is a `turn` since stage 2.

**Stage 1 as implemented**, where it differs from or adds to this proposal:

- **The content binding is the message map** (`IMessageMap`, see [Messages](../Messages/readme.md)): the runner splits, writes and recognises through the map of the range's root and asks it when a block is complete (`Split`, `Write`, `TryRead`, `IsComplete`); an ordered chain of providers creates the map (the address map, else a dump). Added later, with the *message exchange for schemas without an address map* item of the [Device todo](../todo.md); the reasons are in [decisions.md](../../../docs/decisions.md).
- **The split message** (called the *cutting message* below) is the first message the exchange sends, else the first data message it receives; the map checks whether it can split for it (an address map: a message with an `address` role or data). Received data is decoded in the order it arrived.
- **A timeout without an `on`** ends the run as `timedOut` (with the reason), instead of being a check error.
- **Exchange files as files:** `ExchangeDefinitions.AddFile` reads them, but the console only loads the files embedded in its assemblies; files next to the schema files of a rig or device are not loaded yet.
- **Not in the proposal:** `MessageExchange.Validate` lists the problems without sending anything.

## Principles

1. **The app decides what is transferred, and with which pattern.** The app calls one entry point with a plan range, the name of an exchange and what the exchange needs (a source, a sink). Neither the definition nor the messages decide which data is fetched or sent.
2. **Standard names.** The most important patterns have a central name (`requestData`, `sendData`). An app asks for the pattern by that name, whatever the device; the device's file says how that pattern goes for that device.
3. **References go one way: from a definition to the schemas.** A schema never refers to a definition: the same message can be used by many patterns. A definition file says which map root it is for and names the messages it uses.
4. **Three responsibilities**, each in one place:

   | Who | Decides |
   | --- | ------- |
   | **App** | *What* is transferred (the plan range), *how* (the exchange name) and *when* (the call). It gives what the exchange needs: a source for data that is sent, a sink for data that is received. |
   | **Content**: the schemas, with a content binding (the message map: an address map or a dump) | What a message looks like. It cuts a range or a block into blocks for a message, builds a message (the `address`, `size`, `device.<name>` and `data` roles), recognises a message of the device and whether it belongs to the block, and tells when the data of a block is **complete**. |
   | **Exchange definition** | The pattern: which message is sent, which messages are expected back, how long to wait, the time between messages, retries, and what a failure means for the rest of the exchange. |

5. **Not bound to addresses.** The definition only knows messages, blocks and events such as *complete*. A content binding without an address map (the dump map, for dumps selected by a function code, see [Messages](../Messages/readme.md)) uses the same definitions.
6. **No message kinds.** A message is a group of the schemas; the definition gives it its part in the pattern. The only distinction the content makes is whether a message has a `data` role (it carries data of the range) or not (a request, a WSD, an ack, a store request: they are all just messages).
7. **The exchange makes it work; explicit only when needed.** Technical structure (such as the loop over the blocks) stays out of a definition as long as one clear rule covers it. Where the rule is not enough, the definition must say it, and the checks require that: nothing is guessed.
8. **No extension.** A definition stands on its own. A device that needs other messages or timing gets its own file; definitions are small.
9. **Location.** A definition file is XML (`*.exchange.xml`) or JSON (`*.exchange.json`), described by `exchange.xsd` and `exchange.schema.json`; by convention in the folder of its device, next to the device's schema (`*.schema.xml`), embedded as a resource of the assembly that owns it (like the schemas), or a file.
10. **No backwards compatibility.** The code protocols, `midi.exchange`, `midi.message` and `midi.messageInterval` are removed; the old code is refactored to the new design.

## Stage 1: the existing patterns as definitions

**Goal:** what `oneWay` and `acknowledged` do today (request and collect data; send data, with or without an
acknowledgement), but from definitions, through one entry point. No new device patterns yet.

### The API

**`MessageExchange`** has one entry point; `Receive` and `Send` are removed:

```csharp
var result = await exchange.Execute(ExchangeNames.RequestData, range, new ExchangeData { Sink = sink }, ct);
var result = await exchange.Execute(ExchangeNames.SendData, range, new ExchangeData { Source = source }, ct);
```

- **Finding the definition:** the exchange file whose `for` is the root of the range's plan (e.g. `Gx700.AddressMap`), and in it the exchange with the name.
- **`ExchangeNames`** holds the standard names: `requestData` (get the data of the range from the device) and `sendData` (give the data of the range to the device). Later stages add names (e.g. a store). A file may also have exchanges with other names, for a pattern of that device only (e.g. a handshake next to the one-way messages); the app then passes that name.
- `ExchangeData` holds what an exchange may need: a `Source` (values of data that is sent) and a `Sink` (values of data that is received). One exchange can use both. Later stages add to it (the prompt callback).
- The device values and the connection are given when the exchange is created, as today.
- The result is a `TransferResult`, a `BlockResult` per block, as today.
- **Validation** before any message is sent. A problem throws, with all problems listed; the same check is available on its own (`Validate`), which is the start of the capabilities stage:
  - there is a file for the root, with the exchange, and the exchange is valid (see the checks below);
  - the messages it names exist in the serializer's schemas;
  - the data messages it sends or receives can carry the range: their data open choice (`midi.role="data"`) accepts the body of the range;
  - the device values have a value for every `device.<name>` role of those messages;
  - a `Source` is given when it sends a data message, a `Sink` when it receives one.

**`DeviceSession`** gets everything it needs when it is created (the device values, the connection, its options).
Its API:

```csharp
await session.RequestData(range, sink, ct);   // today's Receive: Execute with requestData
await session.SendData(range, source, ct);    // today's Send: Execute with sendData
await session.Execute("sendDataHandshake", range, new ExchangeData { Source = source }, ct);
```

- `RequestData` and `SendData` are today's `Receive` and `Send`, renamed. Their logic stays: values from the state, atomic groups completed before a send (with `requestData` when the state does not hold them), invalidation of blocks that did not complete.
- `Execute` applies the same state logic, based on what the exchange does: when it sends a message with a `data` role, the atomic groups are completed from the state first; when it receives one, the data also goes into the state.
- The rest of the API (`Invalidate`, `Clear`, `Probe`, `StateChanged`, `Disconnected`) is unchanged. It can be revised later.

### The format

```xml
<exchanges for="..." responseTimeout="..." messageInterval="...">
  <exchange name="...">
    <state name="...">
      <send message="..."/>         <!-- optional: on entry, build and send this message -->
      <receive message="..."/>      <!-- optional, repeatable: accept this data message while in this state -->
      <on event="..." | message="..."  goto="..." | result="..."/>
    </state>
  </exchange>
  <exchange name="...">...</exchange>
</exchanges>
```

- `exchanges` is the file: the exchanges of one map root (`for`), with the timing of the device.
- An `exchange` is one plain state machine; the first state is the start.
- A state optionally sends a message, then waits for the events it has an `on` for.

### Blocks

A **block** is the part of the range that one message asks for or carries. Blocks exist today (`AddressBlock`, and
a `BlockResult` per block). The content cuts them, to honour what the device and the messages demand:

- **atomic groups** (`midi.atomic`, e.g. a voice allocation) go whole, never split;
- the **maximum size** of a message (`midi.maxDataSize`);
- **gaps**: a message covers contiguous addresses;
- the **unit** of a message (`midi.unit`): exactly one field, exactly one atomic group, or any range;
- **access**: blocks for a message with a `data` role leave out the read-only fields, blocks for any other message the write-only fields (today this follows from `midi.message`).

For example, a whole GX-700 patch cut for `RQ1` (unit `range`) gives one block per section: a section takes 128
addresses but holds fewer fields, so there is a gap after each one. That gives 14 blocks, so 14 `RQ1`/`DT1` pairs. A
device with another layout comes out differently from the same code (`AddressMap.Split`). For content without an
address map, a block is one dump message (the dump map).

**The rule:** an exchange runs **once per block**, from its first state. The blocks are cut for the **cutting
message**: the first message the exchange sends that asks for data (an `address`/`size` role) or carries it (a `data`
role). A block that does not complete ends the exchange; the blocks that were not transferred are reported with
the same status. The author describes the exchange of
*one* block and writes no loop. Messages once around the blocks come with stage 3 (`before` and `after`).

### The machine

**`send` and `receive` are actions:**

- `send` names the message to build. The content fills it in for the current block.
- `receive` names a data message whose content goes to the sink and counts towards `received`. A data message that is not inside the block is not accepted, as today.

**Events of a state:**

| Event | When |
| ----- | ---- |
| `sent` | The message of the state's `send` was sent. |
| `received` | A `receive` accepted data, and the block now has all its data. |
| `message="<name>"` | The device sent this message, and it matches the device values. For a data message accepted by `receive`: only while the block is not complete yet (else the event is `received`). |
| `timeout` | No event within the timeout. The timer restarts with every accepted data message (a reply in several messages). |

A message the state has no `on` or `receive` for is ignored. Messages that arrive between two states are queued,
not lost: a state that only sends can go on with `sent` to the state that waits. A transition goes to another state
(`goto`) or ends the run for the block (`result`).

**Defaults:** a state without an `on` for an event gets the default for it; an `on` replaces the default. The
defaults cover the common case, so a one-way request is a `send` and a `receive`, a one-way write just a `send`:

| Event | Default | When |
| ----- | ------- | ---- |
| `sent` | `result="completed"` | The state sends and waits for nothing: no `receive` and no `on`. A state that waits for something (an ack) waits for its events instead. |
| `received` | `result="completed"` | The state has a `receive`. |
| `timeout` | `result="timedOut"` | Always. |

The **reason** of a `BlockResult` is written by the runner from what happened, e.g.
*"'Acme.Message.DataLoadError' in state 'write' of 'sendData'"* or *"timeout in state 'request' of 'requestData'"*:
the definition does not write texts.

### The values

| Where | Value | Example | Meaning |
| ----- | ----- | ------- | ------- |
| `exchanges/@for` | the schema name of a map root | `Gx700.AddressMap` | The root whose ranges these exchanges transfer. |
| `exchanges/@responseTimeout`, `exchange/@responseTimeout` | milliseconds, default 1000 | `1000` | How long a state waits for an event. On the exchange it overrides the file. Replaces `MessageExchangeOptions.ResponseTimeout`. |
| `exchanges/@messageInterval`, `exchange/@messageInterval` | milliseconds, default 0 | `60` | The least time between two messages sent to the device. The runner keeps it between blocks and between calls. Replaces `midi.messageInterval`. |
| `exchange/@name` | a standard name or a name of the file's own | `requestData` | The name the app passes to `Execute`. Unique in the file. |
| `send/@message`, `receive/@message`, `on/@message` | the schema name of a message group | `Roland.Message32.RQ1` | A message of the schemas: schema, root and group, as plan ranges name nodes. |
| `on/@event` | `sent`, `received`, `timeout` | `received` | See the events; without an `on` the default applies. |
| `on/@goto` | the name of a state | `collect` | The next state. |
| `on/@result` | `completed`, `rejected`, `failed`, `timedOut` | `completed` | Ends the run for the block with this `BlockStatus`. |

### Example: the Roland request–data exchange

`Jacobi.Midi.Roland/Exchange/Gx700.exchange.xml`:

```xml
<exchanges for="Gx700.AddressMap" responseTimeout="1000" messageInterval="60">
  <exchange name="requestData">
    <state name="request">
      <send message="Roland.Message32.RQ1"/>
      <receive message="Roland.Message32.DT1"/>
    </state>
  </exchange>
  <exchange name="sendData">
    <state name="write">
      <send message="Roland.Message32.DT1"/>
    </state>
  </exchange>
</exchanges>
```

**What happens when the app receives a patch** with
`Execute(ExchangeNames.RequestData, patch, new ExchangeData { Sink = sink }, ct)`:

1. **Finding:** the root of the range's plan is `Gx700.AddressMap`; its file has `requestData`.
2. **Validation:** `RQ1` and `DT1` exist; the data choice of `DT1` accepts the GX-700 map (the range's body); the device values hold the device id and model id; a sink is given (the exchange receives data).
3. **Content:** the address map widens the range to its atomic groups and cuts it into blocks for `RQ1`, the cutting message (it asks for data): one block per section.
4. **Definition:** the first block starts in `request`, which sends `RQ1`.
5. **Content:** builds the `RQ1` for the block: its address, its size, the device id and the model id.
6. **Runner:** sends it, at least 60 ms after the previous message to the device. The state has events to wait for, so it waits up to 1000 ms.
7. A `DT1` arrives. **Content:** it matches the device values and lies inside the block; `receive` accepts it. The block is not complete yet and the state has no `on` for `DT1`, so the wait goes on; the timer restarts.
8. The next `DT1` fills the block. **Content:** complete. **Definition:** `received` → `result="completed"` (the default).
9. **Runner:** decodes the accepted data into the sink in address order, adds the `BlockResult`, and starts the next block at point 4.

A message that is neither a `DT1` of the block nor named in an `on` is ignored. When nothing arrives within 1000 ms:
`timedOut`, with the reason *"timeout in state 'request' of 'requestData'"*; the exchange ends and the remaining blocks are
reported with the same status.

**Sending a setting** with `sendData`: the cutting message is `DT1` (it carries data); per block the content builds
the `DT1` from the source's values, the runner sends it and `sent` completes the block (the default). Between two `DT1`s the
runner keeps 60 ms. The GP-8 gets the same file with `Message16`, `for="Gp8.AddressMap"` and 20 ms.

### Example: data acknowledged by the device

A made-up device that answers a dump with DATA LOAD COMPLETED or ERROR:

```xml
<exchange name="sendData">
  <state name="write">
    <send message="Acme.Message.ProgramDump"/>
    <on message="Acme.Message.DataLoadCompleted" result="completed"/>
    <on message="Acme.Message.DataLoadError" result="rejected"/>
  </state>
</exchange>
```

### What changes in the code

- **New:** `MessageExchange.Execute` and `Validate`, `ExchangeData`, `ExchangeNames`; `DeviceSession.Execute`; the definition model, its XML reader and checks; a registry that finds the files by their `for` root, in the registered assemblies and as files; the runner of a definition, which writes the reasons.
- **The checks:** a `goto` to an unknown state, a state that is never reached, a state that waits without a way out, an exchange without a cutting message, and an exchange that sends two different messages that ask for or carry data (it would need two cuts, see *Later stages*).
- **New: the content binding**, the runner's view of the content: cut a range into blocks for a message; build a message for a block; recognise a message (does it match the device values, is it data inside the block); is the block complete. The address map implements it. The coverage check moves from `RequestReplyMachine` to the address map. This is also the seam for content without an address map.
- **Changed: finding the messages.** Today `AddressProtocol` finds requests, data messages and replies through `midi.message`. Without the tag:
  - the messages of an exchange are the ones its definition names;
  - for use without an exchange (reading a dump file, the Schema Editor, the console offline), a data message is a group of an envelope root that holds an open choice with `midi.role="data"`, a tag that exists already. The address space follows from the `address` role of those data messages.
  - `AddressMap.Split` tells a data message from any other by its `data` role instead of its kind (see *Blocks*).
- **Renamed:** `DeviceSession.Receive`/`Send` become `RequestData`/`SendData`, with their logic unchanged; they call `Execute` with the standard names.
- **Removed:** `Receive`/`Send` on `MessageExchange`, `IExchangeProtocol`, `ExchangeProtocols` and its machines, `TransferSettings`, `ExchangeMessageKind`, `MessageExchangeOptions.Protocol` and `ResponseTimeout`, the tags `midi.message`, `midi.exchange` and `midi.messageInterval` (with `AddressMessage.Kind`, `AddressMessage.Interval`, the map root overrides, their checks and descriptions in `AddressMapTags`).
- **Kept:** `midi.role`, `midi.unit` and `midi.maxDataSize`: they describe what a message holds and can carry, which is content. `Listen` keeps recognising data messages through the content.
- **Schemas:** the Roland and test schemas lose `midi.message` and `midi.messageInterval`; the Roland assembly gets the GP-8 and GX-700 files.

## Stage 2: turns and retries

**Goal:** a name that fits the domain, and retries with a limit. The model stays one plain state machine per block.

### `state` becomes `turn`

An exchange is a conversation with the device, taken in *turns*: send a message, receive the replies. `state` is a
term of the mechanism, not of the domain. In the formats `state` becomes `turn` (JSON: `states` becomes `turns`), in
the code `ExchangeState` becomes `ExchangeTurn`; `goto` names a turn.

### Retries: `on/@retry`

An `on` with `retry` enters its turn again (the turn's `send` is sent again) at most that many times per block. After
that, the `goto` or `result` of the same `on` is taken. One element says both the retry and what comes after it:

```xml
<exchange name="requestData">
  <turn name="request">
    <send message="Acme.Message.DumpRequest"/>
    <receive message="Acme.Message.Dump"/>
    <on event="timeout" retry="2" result="timedOut"/>         <!-- ask again, twice; then timed out -->
  </turn>
</exchange>
```

```xml
<exchange name="sendData">
  <turn name="write">
    <send message="Acme.Message.ProgramDump"/>
    <on message="Acme.Message.DataLoadCompleted" result="completed"/>
    <on message="Acme.Message.DataLoadError" retry="3" result="rejected"/>   <!-- resend, up to 3 times -->
  </turn>
</exchange>
```

- The counter is per `on` and starts at zero for each block.
- Data the block already accepted stays accepted; the timeout starts again.
- The reason the runner writes mentions the retries, e.g. *"timeout in turn 'request' of 'requestData', after 2 retries"*.

### The values added

| Where | Value | Example | Meaning |
| ----- | ----- | ------- | ------- |
| `on/@retry` | a number, 1 or more | `2` | The most times the turn is entered again for this `on`, per block; then its `goto` or `result`. |

### What changes in the code for stage 2

- The rename in the model, both readers, `exchange.xsd`, `exchange.schema.json`, the Roland files and the tests.
- `ExchangeTransition.Retry`; the counters in `ExchangeMachine`, per block.
- The check: `retry` only on `timeout` and on a message (on `sent` it would resend without waiting, on `received` there is nothing to retry).

## Stage 3: before and after

**Goal:** messages once per call, around the blocks. This gives the framing of the Yamaha Bulk Header/Footer, a
command after the data, such as a store request (Korg Program Write Request, Yamaha Store To Flash), and the GX-700
Sound Change Request after a write to its temporary area.

An exchange gets an optional `before` and an optional `after`, each with turns of their own. The turns directly in the
exchange are the turns of a block, as in stage 1:

```xml
<exchange name="...">
  <before>                         <!-- optional: once, before the first block -->
    <turn name="...">...</turn>
  </before>
  <turn name="...">...</turn>      <!-- per block, as in stage 1 -->
  <after>                          <!-- optional: once, after the last block -->
    <turn name="...">...</turn>
  </after>
</exchange>
```

**The order of a call:**

1. `before` runs once, from its first turn. `result="completed"` starts the blocks. Any other result ends the call: no block is transferred, and each is reported with that status and reason.
2. The blocks run as in stage 1: once per block. The first block that does not complete ends the call; `after` does not run.
3. After the last block completed, `after` runs once. Its result is the result of `after` in the `TransferResult`.

`before`, the block and `after` are three separate machines: a `goto` stays within its own part.

**The messages of `before` and `after`** are built for the whole range (widened to its atomic groups), not for a block:
an `address` role gets the start of the range, a `size` role its size. They do not cut and they do not carry data:
a message with a `data` role, and a `receive`, are only allowed in the turns of a block.

**Fixed messages** can be sent in any turn: in `before`, in a block and in `after`. A fixed message has no `address`,
`size` or `data` role: the schema gives all its bytes, apart from the `device.<name>` roles (e.g. the device id). It
does not depend on the range, and it never cuts. In a block turn it is sent once per block (this already works in
stage 1); in `before` or `after` once per call.

**The result:** `TransferResult` gets `Before` and `After`: the status and reason of each part, null when the exchange
has no such part. `IsComplete` includes them: data that was sent but whose store request was rejected is not complete.

### Example: the Yamaha Bulk Header/Footer

```xml
<exchange name="sendData">
  <before>
    <turn name="header">
      <send message="Yamaha.Cp88.BulkHeader"/>
    </turn>
  </before>
  <turn name="dump">
    <send message="Yamaha.Cp88.BulkDump"/>
  </turn>
  <after>
    <turn name="footer">
      <send message="Yamaha.Cp88.BulkFooter"/>
    </turn>
  </after>
</exchange>
```

The pattern is complete with stage 3. The content is not: which header address belongs to a range (the slot), and
the blocks at the edit buffer addresses, need the *Bulk Header/Footer indirection* of the address map
(see `docs/analysis/yamaha.md`). That is a content design, apart from the exchange.

### Example: store after sending

A made-up device that writes a program from its edit buffer to memory after a store request:

```xml
<exchange name="sendDataAndStore">
  <turn name="write">
    <send message="Acme.Message.ProgramDump"/>
    <on message="Acme.Message.DataLoadCompleted" result="completed"/>
    <on message="Acme.Message.DataLoadError" result="rejected"/>
  </turn>
  <after>
    <turn name="store">
      <send message="Acme.Message.ProgramWriteRequest"/>
      <on message="Acme.Message.WriteCompleted" result="completed"/>
      <on message="Acme.Message.WriteError" result="rejected"/>
    </turn>
  </after>
</exchange>
```

### Example: the GX-700 Sound Change Request

The GX-700 applies data written to its temporary area only after a write of 00 to address 04 7F 7F 7F. That write is
a fixed message: a group of the GX-700 schema (to be added) with the complete DT1, address and data included, so it
has no `address`, `size` or `data` role. An exchange of its own name sends it after the data; the app uses that
exchange for the temporary area, and `sendData` for the other areas:

```xml
<exchange name="sendTemporary">
  <turn name="write">
    <send message="Roland.Message32.DT1"/>
  </turn>
  <after>
    <turn name="apply">
      <send message="Gx700.Message.SoundChangeRequest"/>
    </turn>
  </after>
</exchange>
```

### What changes in the code for stage 3

- The model: `ExchangeDefinition.Before` and `After` (lists of turns); the readers, `exchange.xsd` and `exchange.schema.json`.
- `MessageExchange.Execute` runs `before`, the blocks and `after`; one inbox for the whole call, so a reply that arrives between two parts is not lost.
- **Content:** build a message for the whole range (today a message is only built for a block).
- `TransferResult.Before` and `After`; `DeviceSession` reports them as they are.
- The checks: a `goto` outside its part, a `data` message or a `receive` in `before` or `after`, an empty `before` or `after`. The cutting message is derived from the turns of the block only.

## Stage 4: prompts

**Goal:** receiving data that the user starts on the device, e.g. by pressing its bulk dump key. The exchange must
already wait for the data when the user is asked, so the prompt belongs to the exchange, not to the app before the
call.

### The prompt

- A `receive` can have a `prompt`: a literal text. On entry of the turn the runner hands the text to the app and goes on at once: it waits for the data as usual. The exchange is never blocked by the prompt; the app shows it in its own way.
- **The prompt ends** when the exchange leaves the turns with that prompt, or when the call ends; the app then closes it. Entering a turn whose prompt is already shown does not show it again, so a prompt in a block turn is shown once, not once per block.
- **Cancel:** the user cancels the call, through the `CancellationToken` of `Execute`, as any cancel.
- **A longer wait:** `turn/@responseTimeout` overrides the timeout of the exchange for that turn (the user needs time to find the key).

The app gives the prompt in `ExchangeData`:

```csharp
// returns at once: show the text until 'ended' is cancelled
public interface IExchangePrompt
{
    void Show(string text, CancellationToken ended);
}

var result = await exchange.Execute("requestDataManual", range, new ExchangeData { Sink = sink, Prompt = prompt }, ct);
```

Validation requires a `Prompt` when the exchange has a `prompt`.

A prompt that waits for the user (*"set the device to receive, then confirm"*) is not part of the exchange: the
app asks the user before it calls `Execute`, as it decides when a transfer starts.

### Blocks when nothing is requested

When the device sends on its own, the exchange sends no message that asks for or carries data. Then the cutting
message is the data message the block turns receive; more than one is a problem, as for the messages sent.

### Example: the user starts the dump

```xml
<exchange name="requestDataManual">
  <turn name="wait" responseTimeout="30000">
    <receive message="Acme.Message.Dump" prompt="Press the BULK DUMP key on the device."/>
  </turn>
</exchange>
```

### The values added

| Where | Value | Example | Meaning |
| ----- | ----- | ------- | ------- |
| `receive/@prompt` | a literal text | `Press the BULK DUMP key on the device.` | Shown to the user while the turn waits for this data. |
| `turn/@responseTimeout` | milliseconds | `30000` | How long this turn waits for an event; overrides the exchange. |

### What changes in the code for stage 4

- The model: the prompt of a `receive`, `ExchangeTurn.ResponseTimeout`; the readers and the format descriptions.
- `ExchangeData.Prompt` (`IExchangePrompt`); the runner shows the prompt on entry of the turn and ends it when the turns with that prompt are left.
- The cutting message from a received data message when none is sent.
- The check: a `Prompt` is given when the exchange prompts.

## Later stages

Named so the direction is visible; each is worked out when it is needed. The order can change.

- **Two levels of blocks:** the Roland handshake for sending (WSD per section, DATs per piece). Receiving with the handshake already fits stage 1, once the handshake messages are in the schemas.
- **A choice between messages per block**, when a device needs it in an exchange file. Today the address map does this for a fixed pattern (`AddressMap.PlanBlocks`): without a `range` message, the atomic groups go with the atomic message and the other fields with the field message.
- **Commands without data and without a range** (a store request on its own), with a standard name: API and responsibilities to be designed first.
- **Listen patterns:** what the device starts (a device that sends a dump after a key on its panel and waits for an acknowledgement), started by an API call of the app.
- **Capabilities:** what the app can ask up front, built on `Validate`: which exchanges a root has, and whether they can run with the given device values and data.
- **An `after` that always runs**, also after a failed block (e.g. to restore a device mode), when a device needs it.
- **A run-time override** of the response timeout.
- **Separate items:** the Schema Editor's *Send* through the runner, so the time between messages is kept in one place; dependent areas of the session state (e.g. a program change that replaces the temporary area), see the [Device todo](../todo.md).

**Dropped** (revision 6 had them): `forEach` with nested blocks and its scopes; going on after a block that did not
complete (`on outcome`, `continue`); values from replies and conditions (`when`, `param`). A device mode is switched
by the app, with an exchange of its own name, before it calls the exchange that needs the mode.

## Decisions

- No reference from a schema to a definition; a definition file names its map root (`for`) and the messages it uses. `midi.message`, `midi.exchange` and `midi.messageInterval` are removed. There are no message kinds; a request is a message like any other.
- Several exchanges of one device in one file (`exchanges`), with the device's timing. The files are XML and JSON, read into the same model.
- Standard names for the most important patterns (`ExchangeNames`: `requestData`, `sendData`); a file can add names of its own.
- An exchange is a plain state machine of turns with `send` and `receive` actions (a `receive` can prompt the user, stage 4); no conditions, no values from replies, no extension.
- No `reason` in the definition: the runner writes it.
- Messages are named by their full schema name, so each device gets its own small file (copies per envelope).
- The response timeout is set in the definition, with a default, and can be set per turn (stage 4); a run-time override comes later.
- Blocks are cut by the content, for the cutting message: the one message the block turns send that asks for or carries data, else (stage 4) the one data message they receive.
- An exchange runs once per block; the first block that does not complete ends the call. There is no explicit loop and no way to go on after a failed block.
- Retries are an attribute of the `on` (`retry`), followed by its own `goto` or `result`.
- Messages once per call are in the optional `before` and `after`, built for the whole range; `after` only runs after all blocks completed. Fixed messages (no `address`, `size` or `data` role) can be sent in any turn.
- Turns that only send go on with `sent` to the turn that waits; messages that arrive in between are queued.
- `MessageExchange.Execute` replaces `Receive`/`Send`. `DeviceSession` gets everything when it is created and has `Execute`, `RequestData` and `SendData`.
- The Schema Editor's *Send* through the runner is a separate item.
