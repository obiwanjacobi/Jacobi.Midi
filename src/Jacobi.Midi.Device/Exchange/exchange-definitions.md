# Exchange definitions (proposal)

*Status: proposal, revision 6, 2026-10-31. Step 1 is implemented (see the [readme](readme.md)); step 2 and later are not.*

A configurable, generic way to describe the message exchange of a device: which messages go back and forth, how long
to wait, and how an exchange ends. The design is built up in small steps. This revision works out **step 1** and
**step 2**; the later steps are named, so the direction is visible.

**Step 1 as implemented**, where it differs from or adds to this proposal:

- **No separate content binding yet.** The runner uses the address map directly (`AddressMap.Split`, `Message`, `CreateData`, `Protocol.CreateMessage`, `TryReadData`, `BlockCoverage`). These are the operations the content binding will have; turning them into an interface belongs with the *message exchange for schemas without an address map* item of the [Device todo](../todo.md), where the types become generic too.
- **A timeout without an `on`** ends the run as `timedOut` (with the reason), instead of being a check error.
- **Exchange files as files:** `ExchangeDefinitions.AddFile` reads them, but the console only loads the files embedded in its assemblies; files next to the schema files of a rig or device are not loaded yet.
- **Not in the proposal:** `MessageExchange.Validate` lists the problems without sending anything; a `send` in a state without an `on` for `sent` waits for the events of the state.

## Principles

1. **The app decides what is transferred, and with which pattern.** The app calls one entry point with a plan range, the name of an exchange and what the exchange needs (a source, a sink). Neither the definition nor the messages decide which data is fetched or sent.
2. **Standard names.** The most important patterns have a central name (`requestData`, `sendData`). An app asks for the pattern by that name, whatever the device; the device's file says how that pattern goes for that device.
3. **References go one way: from a definition to the schemas.** A schema never refers to a definition: the same message can be used by many patterns. A definition file says which map root it is for and names the messages it uses.
4. **Three responsibilities**, each in one place:

   | Who | Decides |
   | --- | ------- |
   | **App** | *What* is transferred (the plan range), *how* (the exchange name) and *when* (the call). It gives what the exchange needs: a source for data that is sent, a sink for data that is received. |
   | **Content**: the schemas, with a content binding (the address map today) | What a message looks like. It cuts a range or a block into blocks for a message, builds a message (the `address`, `size`, `device.<name>` and `data` roles), recognises a message of the device and whether it belongs to the block, and tells when the data of a block is **complete**. |
   | **Exchange definition** | The pattern: which message is sent, which messages are expected back, how long to wait, the time between messages, retries, and what a failure means for the rest of the exchange. |

5. **Not bound to addresses.** The definition only knows messages, blocks and events such as *complete*. A content binding without an address map (dumps selected by a function code, see the [Device todo](../todo.md)) uses the same definitions.
6. **No message kinds.** A message is a group of the schemas; the definition gives it its part in the pattern. The only distinction the content makes is whether a message has a `data` role (it carries data of the range) or not (a request, a WSD, an ack, a store request: they are all just messages).
7. **The exchange makes it work; explicit only when needed.** Technical structure (such as the loop over the blocks) stays out of a definition as long as one clear rule covers it. Where the rule is not enough, the definition must say it, and the checks require that: nothing is guessed.
8. **No extension.** A definition stands on its own. A device that needs other messages or timing gets its own file; definitions are small.
9. **Location.** A definition file is XML (`*.exchange.xml`) or JSON (`*.exchange.json`), described by `exchange.xsd` and `exchange.schema.json`; by convention in the `Exchange` folder of the assembly that owns it, embedded as a resource (like the schemas), or a file next to schema files.
10. **No backwards compatibility.** The code protocols, `midi.exchange`, `midi.message` and `midi.messageInterval` are removed; the old code is refactored to the new design.

## Step 1: the existing patterns as definitions

**Goal:** what `oneWay` and `acknowledged` do today (request and collect data; send data, with or without an
acknowledgement), but from definitions, through one entry point. No new device patterns yet.

### The API

**`MessageExchange`** has one entry point; `Receive` and `Send` are removed:

```csharp
var result = await exchange.Execute(ExchangeNames.RequestData, range, new ExchangeData { Sink = sink }, ct);
var result = await exchange.Execute(ExchangeNames.SendData, range, new ExchangeData { Source = source }, ct);
```

- **Finding the definition:** the exchange file whose `for` is the root of the range's plan (e.g. `Gx700.AddressMap`), and in it the exchange with the name.
- **`ExchangeNames`** holds the standard names: `requestData` (get the data of the range from the device) and `sendData` (give the data of the range to the device). Later steps add names (e.g. a store). A file may also have exchanges with other names, for a pattern of that device only (e.g. a handshake next to the one-way messages); the app then passes that name.
- `ExchangeData` holds what an exchange may need: a `Source` (values of data that is sent) and a `Sink` (values of data that is received). One exchange can use both. Later steps add to it (the prompt callback).
- The device values and the connection are given when the exchange is created, as today.
- The result is a `TransferResult`, a `BlockResult` per block, as today.
- **Validation** before any message is sent. A problem throws, with all problems listed; the same check is available on its own (`Validate`), which is the start of the capabilities step:
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
address map, a block would be one dump message.

**The rule:** an exchange runs **once per block**, from its first state. The blocks are cut for the **cutting
message**: the first message the exchange sends that asks for data (an `address`/`size` role) or carries it (a `data`
role). A block that does not complete ends the exchange; the blocks that were not transferred are reported with
the same status. The author describes the exchange of
*one* block and writes no loop. When this rule is not enough, step 2 makes the loop explicit with `forEach`.

### The machine

**`send` and `receive` are actions:**

- `send` names the message to build. The content fills it in for the current block.
- `receive` names a data message whose content goes to the sink and counts towards `complete`. A data message that is not inside the block is not accepted, as today.

**Events of a state:**

| Event | When |
| ----- | ---- |
| `sent` | The message of the state's `send` was sent. |
| `complete` | A `receive` accepted data, and the block now has all its data. |
| `message="<name>"` | The device sent this message, and it matches the device values. For a data message accepted by `receive`: only while the block is not complete yet (else the event is `complete`). |
| `timeout` | No event within the timeout. The timer restarts with every accepted data message (a reply in several messages). |

A message the state has no `on` or `receive` for is ignored. Messages that arrive between two states are queued,
not lost: a state that only sends can go on with `sent` to the state that waits. A transition goes to another state
(`goto`) or ends the run for the block (`result`).

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
| `on/@event` | `sent`, `complete`, `timeout` | `complete` | See the events. |
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
      <on event="complete" result="completed"/>
      <on event="timeout" result="timedOut"/>
    </state>
  </exchange>
  <exchange name="sendData">
    <state name="write">
      <send message="Roland.Message32.DT1"/>
      <on event="sent" result="completed"/>
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
8. The next `DT1` fills the block. **Content:** complete. **Definition:** `complete` → `result="completed"`.
9. **Runner:** decodes the accepted data into the sink in address order, adds the `BlockResult`, and starts the next block at step 4.

A message that is neither a `DT1` of the block nor named in an `on` is ignored. When nothing arrives within 1000 ms:
`timedOut`, with the reason *"timeout in state 'request' of 'requestData'"*; the exchange ends and the remaining blocks are
reported with the same status.

**Sending a setting** with `sendData`: the cutting message is `DT1` (it carries data); per block the content builds
the `DT1` from the source's values, the runner sends it and `sent` completes the block. Between two `DT1`s the
runner keeps 60 ms. The GP-8 gets the same file with `Message16`, `for="Gp8.AddressMap"` and 20 ms.

### Example: data acknowledged by the device

A made-up device that answers a dump with DATA LOAD COMPLETED or ERROR:

```xml
<exchange name="sendData">
  <state name="write">
    <send message="Acme.Message.ProgramDump"/>
    <on message="Acme.Message.DataLoadCompleted" result="completed"/>
    <on message="Acme.Message.DataLoadError" result="rejected"/>
    <on event="timeout" result="timedOut"/>
  </state>
</exchange>
```

### What changes in the code

- **New:** `MessageExchange.Execute` and `Validate`, `ExchangeData`, `ExchangeNames`; `DeviceSession.Execute`; the definition model, its XML reader and checks; a registry that finds the files by their `for` root, in the registered assemblies and as files; the runner of a definition, which writes the reasons.
- **The checks:** a `goto` to an unknown state, a state that is never reached, a state that waits without a way out, an exchange without a cutting message, and an exchange that sends two different messages that ask for or carry data (it would need two cuts: it needs a `forEach`, step 2).
- **New: the content binding**, the runner's view of the content: cut a range into blocks for a message; build a message for a block; recognise a message (does it match the device values, is it data inside the block); is the block complete. The address map implements it. The coverage check moves from `RequestReplyMachine` to the address map. This is also the seam for content without an address map.
- **Changed: finding the messages.** Today `AddressProtocol` finds requests, data messages and replies through `midi.message`. Without the tag:
  - the messages of an exchange are the ones its definition names;
  - for use without an exchange (reading a dump file, the Schema Editor, the console offline), a data message is a group of an envelope root that holds an open choice with `midi.role="data"`, a tag that exists already. The address space follows from the `address` role of those data messages.
  - `AddressMap.Split` tells a data message from any other by its `data` role instead of its kind (see *Blocks*).
- **Renamed:** `DeviceSession.Receive`/`Send` become `RequestData`/`SendData`, with their logic unchanged; they call `Execute` with the standard names.
- **Removed:** `Receive`/`Send` on `MessageExchange`, `IExchangeProtocol`, `ExchangeProtocols` and its machines, `TransferSettings`, `ExchangeMessageKind`, `MessageExchangeOptions.Protocol` and `ResponseTimeout`, the tags `midi.message`, `midi.exchange` and `midi.messageInterval` (with `AddressMessage.Kind`, `AddressMessage.Interval`, the map root overrides, their checks and descriptions in `AddressMapTags`).
- **Kept:** `midi.role`, `midi.unit` and `midi.maxDataSize`: they describe what a message holds and can carry, which is content. `Listen` keeps recognising data messages through the content.
- **Schemas:** the Roland and test schemas lose `midi.message` and `midi.messageInterval`; the Roland assembly gets the GP-8 and GX-700 files.

## Step 2: `forEach`, retries and failure handling

**Goal:** patterns the rule of step 1 cannot express: messages once around the blocks, blocks inside blocks, the
definition's own handling of a failed block, and retries. This gives the Roland handshake and the framing of the
Yamaha Bulk Header/Footer. Step 1 exchanges stay as they are.

### `forEach`

`forEach` makes the loop over the blocks explicit. It is a **loop and a scope**:

```text
foreach (block in content.Cut(data, cuttingMessage))   // data: the range, or the current block of the forEach around it
    run the states inside, from the first one, until one of them gives a result
    then the forEach's on outcome decides: next block, or leave the loop
```

- Everything inside works on the **current block**: `send` builds its message for it, `complete` refers to it, the `max` counters start again for each block.
- **The cutting message** is derived as in step 1: the first message sent *directly* in the `forEach` (not in a nested one) that asks for or carries data. `message` overrides it.
- `forEach` can be **nested**: an inner `forEach` cuts the current block of the outer one. The Roland handshake announces a section with WSD and sends it as DATs: the outer blocks are cut for WSD, the inner ones for DAT, with DAT's `midi.maxDataSize`.
- A `forEach` has a **name** and is the target of a `goto`, like a state. A `goto` stays within its scope: it cannot jump into or out of another `forEach`.
- **States outside any `forEach`** run once per call. A message they send is built for the whole range. A `result` there ends the exchange.
- An exchange with a `forEach` has no implicit loop: what is not inside a `forEach` runs once.

### After each block: `on outcome`

The `on`s of a `forEach` itself (next to its states) are checked after each block:

| After a block | Without an `on` | With an `on` |
| ------------- | --------------- | ------------ |
| completed | the next block | (rarely needed) |
| not completed (`rejected`, `failed`, `timedOut`) | leave the loop; the scope around it ends with the same result | `continue="true"`: report it and go on with the next block; `goto` or `result`: leave the loop |
| after the last block: `on event="end"` | the scope around it completes | `goto` the state that follows |

- **The outermost `forEach`** gives the `BlockResult`s of the `TransferResult`, one per block. The data a block accepted (also in inner blocks) is decoded into the sink when that block completes.
- **When the exchange ends early**, the blocks that were not transferred are reported with the status it ended with: nothing fails silently.
- The step 1 rule is the same as one `forEach` around all states, without `on`s: the first block that does not complete ends the exchange.

### Retries: `on/@max`

An `on` with `max` is taken at most that many times per run of its scope (per block inside a `forEach`; per call
outside). After that it no longer matches, and the next `on` for the same event is taken. The order of the `on`s is
the fallback:

```xml
<on message="Roland.Handshake32.ERR" goto="write" max="3"/>   <!-- resend, up to 3 times -->
<on message="Roland.Handshake32.ERR" result="failed"/>        <!-- the 4th ERR -->
```

### The values added

| Where | Value | Example | Meaning |
| ----- | ----- | ------- | ------- |
| `forEach/@name` | a name, unique next to the states of its scope | `data` | The target of a `goto`. |
| `forEach/@message` | the schema name of a message, optional | `Roland.Handshake32.DAT` | Overrides the derived cutting message. |
| `forEach/on/@outcome` | `completed`, `rejected`, `failed`, `timedOut` | `rejected` | The result of the block that just ended. |
| `forEach/on/@event` | `end` | `end` | After the last block. |
| `forEach/on/@continue` | `true` | `true` | Go on with the next block (the outcome is reported). |
| `on/@max` | a number, 1 or more | `3` | The most times this `on` is taken per run of its scope. |

### Example: the Roland handshake, sending

For a Roland device that has the handshake messages next to RQ1/DT1. They are not in `Roland.xml` yet;
`Roland.Handshake32` stands for an envelope with WSD, RQD, DAT, ACK, EOD, ERR and RJC, with 4 address bytes.
The exchanges have names of their own, so `requestData`/`sendData` of the same file can stay one-way.

```xml
<exchange name="sendDataHandshake">
  <forEach>                                                   <!-- cut for WSD: one section per block -->
    <state name="announce">
      <send message="Roland.Handshake32.WSD"/>                 <!-- the address and size of the section -->
      <on message="Roland.Handshake32.ACK" goto="data"/>
      <on message="Roland.Handshake32.RJC" result="rejected"/>
      <on event="timeout" result="timedOut"/>
    </state>
    <forEach name="data">                                     <!-- the section, cut for DAT -->
      <state name="write">
        <send message="Roland.Handshake32.DAT"/>
        <on message="Roland.Handshake32.ACK" result="completed"/>
        <on message="Roland.Handshake32.ERR" goto="write" max="3"/>
        <on message="Roland.Handshake32.ERR" result="failed"/>
        <on message="Roland.Handshake32.RJC" result="rejected"/>
        <on event="timeout" result="timedOut"/>
      </state>
      <on event="end" goto="finish"/>                         <!-- all DATs sent -->
    </forEach>
    <state name="finish">
      <send message="Roland.Handshake32.EOD"/>
      <on event="sent" result="completed"/>
    </state>
    <on outcome="rejected" continue="true"/>                  <!-- a rejected section: report it, go on -->
  </forEach>
</exchange>
```

**What happens when the app sends a patch** with `Execute("sendDataHandshake", patch, ...)`:

1. **Outer `forEach`:** its cutting message is `WSD` (the first message directly in it that asks for data; `DAT` is in the nested one). The content cuts the patch into sections.
2. Per section: `announce` sends `WSD` with the section's address and size. The device answers `ACK` → `data`.
3. **Inner `forEach`:** its cutting message is `DAT` (it carries data). The content cuts the section into pieces of at most `DAT`'s `midi.maxDataSize`.
4. Per piece: `write` sends `DAT`. `ACK` → `completed` → the next piece. `ERR` → `write` again, which resends the same piece; the 4th `ERR` takes the second `on` → `failed`. `RJC` → `rejected`.
5. After the last piece: `on event="end"` → `finish` sends `EOD`; `sent` → the section is `completed` → the next section starts at step 2.
6. A piece that does not complete: the inner `forEach` has no `on` for it, so the section ends with the same result (no `EOD` follows). The outer `forEach` reports the section. When it was `rejected`, `continue="true"` goes on with the next section; any other failure ends the exchange, and the remaining sections are reported with that status.

### Example: the Roland handshake, requesting

The device sends the DATs, the host acknowledges each one. This needs no `forEach`: the step 1 rule covers it
(`RQD` is the only message sent that asks for or carries data; `ACK` does neither, `DAT` is received).

```xml
<exchange name="requestDataHandshake">
  <state name="request">
    <send message="Roland.Handshake32.RQD"/>
    <on event="sent" goto="collect"/>
  </state>
  <state name="collect">
    <receive message="Roland.Handshake32.DAT"/>
    <on message="Roland.Handshake32.DAT" goto="acknowledge"/>      <!-- more DATs to come -->
    <on event="complete" goto="acknowledgeLast"/>
    <on message="Roland.Handshake32.RJC" result="rejected"/>
    <on event="timeout" result="timedOut"/>
  </state>
  <state name="acknowledge">
    <send message="Roland.Handshake32.ACK"/>
    <on event="sent" goto="collect"/>
  </state>
  <state name="acknowledgeLast">
    <send message="Roland.Handshake32.ACK"/>
    <on event="sent" goto="end"/>
  </state>
  <state name="end">
    <on message="Roland.Handshake32.EOD" result="completed"/>
    <on event="timeout" result="timedOut"/>
  </state>
</exchange>
```

The exact order of the handshake (e.g. whether the host acknowledges the EOD) is to be checked against the Roland
manuals when the messages are added; the examples show the mechanism.

### Example: the Yamaha Bulk Header/Footer

A stored Live Set Sound of the CP88 is sent as Bulk Header, the bulk dumps at the edit buffer addresses, and Bulk
Footer. The header and footer are outside the `forEach`, so they are sent once and built for the whole range:

```xml
<exchange name="sendData">
  <state name="header">
    <send message="Yamaha.Cp88.BulkHeader"/>
    <on event="sent" goto="dumps"/>
  </state>
  <forEach name="dumps">                                       <!-- cut for BulkDump -->
    <state name="dump">
      <send message="Yamaha.Cp88.BulkDump"/>
      <on event="sent" result="completed"/>
    </state>
    <on event="end" goto="footer"/>
  </forEach>
  <state name="footer">
    <send message="Yamaha.Cp88.BulkFooter"/>
    <on event="sent" result="completed"/>
  </state>
</exchange>
```

The pattern is complete with step 2. The content is not: which header address belongs to a range (the slot), and
the blocks at the edit buffer addresses, need the *Bulk Header/Footer indirection* of the address map
(see `docs/analysis/yamaha.md`). That is a content design, apart from the exchange.

### Example: retry on a timeout

A device that sometimes misses a request; step 1 rule, no `forEach`:

```xml
<exchange name="requestData">
  <state name="request">
    <send message="Acme.Message.DumpRequest"/>
    <receive message="Acme.Message.Dump"/>
    <on event="complete" result="completed"/>
    <on event="timeout" goto="request" max="2"/>    <!-- ask again, twice -->
    <on event="timeout" result="timedOut"/>
  </state>
</exchange>
```

### Example: go on after a rejected block

By default the first block that does not complete ends the exchange. To go on after a rejected block (e.g. a
protected memory slot) and still stop on anything else, the step 1 exchange gets a `forEach` with `continue`:

```xml
<exchange name="sendData">
  <forEach>
    <state name="write">
      <send message="Acme.Message.ProgramDump"/>
      <on message="Acme.Message.DataLoadCompleted" result="completed"/>
      <on message="Acme.Message.DataLoadError" result="rejected"/>
      <on event="timeout" result="timedOut"/>
    </state>
    <on outcome="rejected" continue="true"/>      <!-- report it, go on with the next block -->
  </forEach>
</exchange>
```

### What changes in the code for step 2

- The definition model gets `forEach` (nested, with `name`, `message` and its own `on`s), states outside a `forEach`, and `max` on `on`.
- The runner keeps a stack of scopes (the range, a block, an inner block), each with its current state and the counters of its `on`s.
- **Content binding:** cut a *block* (not only a range) into blocks for another message; build a message for the whole range (for states outside a `forEach`).
- The checks: a `goto` within its scope, a `forEach` without a cutting message (none derived, none given), an `on outcome` or `on event="end"` outside a `forEach`, an event whose last `on` has a `max` (warning: after the retries the event would be ignored).

## Later steps

Each is worked out after the previous one is agreed. The order can change.

- **Step 3, values from replies, and states that always run:** see the preview below. Gives device modes and error codes; a mode that is switched for an exchange is restored at the end, also after a failure.
- **Step 4, prompts:** a state with literal text; the app gives a callback in `ExchangeData`, and validation requires it when the exchange prompts.
- **Step 5, listen patterns:** what the device starts (a Roland device that sends WSD after a dump on its panel and waits for an ACK), started by an API call of the app.
- **Step 6, commands without data** (write/store requests, with a standard name): API and responsibilities to be designed first.
- **Step 7, capabilities:** what the app can ask up front, built on `Validate`: which exchanges a root has, and whether they can run with the given device values and data.
- **A choice between messages per block**, when a device needs it. Today the address map already does this for a fixed pattern (`AddressMap.PlanBlocks`): without a `range` message, the atomic groups go with the atomic message and the other fields with the field message (Yamaha Dump Request for a bulk block and Parameter Request for one parameter; Korg program dump and parameter change). A sketch: `send` lists the alternatives and the content picks the one that fits each block; `receive` lists the possible replies. When the handling differs too, each block starts in the state that sends its message.

  ```xml
  <send message="Yamaha.Cp88.DumpRequest Yamaha.Cp88.ParameterRequest"/>
  <receive message="Yamaha.Cp88.BulkDump Yamaha.Cp88.ParameterChange"/>
  ```

- **Later:** a run-time override of the response timeout.
- **Separate item:** the Schema Editor's *Send* through the runner, so the time between messages is kept in one place.

### Preview of the values of step 3

To show the idea; the syntax is decided in step 3. The definition only handles values it owns: constants it sends
and values it reads from a reply. Addresses, sizes, device values and data stay with the content.

A device answers a mode request with its current mode. The schema marks the node with a role, as `device.<name>` does:

```xml
<group name="ModeData">
  <members>
    <!-- constants of the message -->
    <field name="Mode" datatype="sys.uint8" midi.role="param.mode"/>
  </members>
</group>
```

The definition tests it, and sends a constant in the same role of another message:

```xml
<state name="mode">
  <send message="Acme.Message.ModeRequest"/>
  <on message="Acme.Message.ModeData" when="mode = 2" goto="request"/>   <!-- already in edit mode -->
  <on message="Acme.Message.ModeData" goto="switch"/>
</state>
<state name="switch">
  <send message="Acme.Message.ModeChange">
    <param name="mode" value="2"/>                                       <!-- fills param.mode of ModeChange -->
  </send>
  <on message="Acme.Message.Ack" goto="request"/>
  <on message="Acme.Message.Nak" result="rejected"/>
</state>
```

A value read from a reply (an error code, `param.code`) can be added to the reason the runner writes.

## Decisions

- No reference from a schema to a definition; a definition file names its map root (`for`) and the messages it uses. `midi.message`, `midi.exchange` and `midi.messageInterval` are removed. There are no message kinds; a request is a message like any other.
- Several exchanges of one device in one file (`exchanges`), with the device's timing.
- Standard names for the most important patterns (`ExchangeNames`: `requestData`, `sendData`); a file can add names of its own.
- An exchange is one plain state machine with `send` and `receive` actions; no `when` filtering, no extension.
- No `reason` in the definition: the runner writes it.
- Messages are named by their full schema name, so each device gets its own small file (copies per envelope).
- The response timeout is set in the definition, with a default; a run-time override comes later.
- Blocks are cut by the content, for the cutting message: derived (the first message sent that asks for or carries data), with `forEach/@message` to override.
- Without `forEach` an exchange runs once per block; a block that does not complete ends the exchange. `forEach` is only needed for messages once around the blocks, blocks inside blocks, the definition's own failure handling, or an override of the cutting message; the checks require it where the rule is not enough.
- The definition decides what a failed block means: `on outcome` of a `forEach`, checked after each block (`continue`, `goto`, `result`). The default, with and without `forEach`: leave the loop.
- States that only send go on with `sent` to the state that waits; messages that arrive in between are queued.
- `MessageExchange.Execute` replaces `Receive`/`Send`. `DeviceSession` gets everything when it is created and has `Execute`, `RequestData` and `SendData`.
- Messages outside a `forEach` are built for the whole range; whether states without a range are needed will show later.
- The Schema Editor's *Send* through the runner is a separate item.
