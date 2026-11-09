# User Design decisions

The user design decisions made for the project, per topic: the issue, the decided direction and briefly why. The code
is the source of truth for how a decision was implemented; this file keeps the *why*.

## Device values in the console's device panel (2026-11-09)

Issue: the device values of a rig device were one text (`name=value, …`); the user should pick the names the schema
knows. The console only had `SchemaLibrary.Maps` (`AddressMapAdapter`); the generic `IMessageMap` and `MessageMaps` were
internal.

1. **A list of device values, a name and a value per row; the droplist offers the names the map root needs from the
   client** (`deviceId`), not the ones it fixes. Any other name can be typed; a name the map root does not need is
   marked, not dropped. *Why:* start with what the user must supply; a value kept from another map root stays visible.
2. **A client sees a message map through a public, read-only wrapper, `MessageMapInfo`; `IMessageMap` stays
   internal.** It exposes only what a client needs (now: `Root`, `Device`, `RequiredDeviceValues`) and grows with real
   needs. *Why:* `IMessageMap` is the generic abstraction over address maps and dumps, but its operations (and
   `IDeviceState`) are for the exchange; `IMessageMap.Device` alone holds only the values the root fixes.
3. **The entry point is a public `MessageMaps`, created per serializer by its owner: `maps.Info(root)`.** The members
   that hand out an `IMessageMap` stay internal. *Why:* the owner (e.g. `SchemaLibrary`) controls the cache's lifetime;
   no hidden static cache, as a static factory or an extension on `Serializer` would need.
4. **`AddressMapAdapter` is to be phased out of the console** in favour of `MessageMaps`; `SchemaLibrary` keeps `Maps`
   for `IsMap`/`ItemCount` until it is decided what they mean for every kind of map (Console todo). *Why:* one generic
   mechanism instead of address-only code in the console.

## Message exchange in the console (2026-11-09)

Issue: the console has a value model per device (`DeviceState`) and MIDI connections with a `DeviceSession` per
device (`RigConnections`), but no data is exchanged with the devices yet; only dumps are loaded and saved, and only for
address maps. See the [proposal](../src/Jacobi.Midi.Console/sync-proposal.md).

1. **Two value models:** the console's `DeviceState` (what the user sees and sets) and the session's state of the
   device (Midi.Device, internal). The console does not own or hand in the session's state. *Why:* keeps the
   separation between a client app and the device API.
2. **The operations with a device are configured in the dashboard and bound to message exchange patterns**, not
   hard-wired in the console. *Why:* devices differ in their patterns; the console stays data-driven.
3. **`DeviceState` supports every kind of root the Device API supports** (address maps and plain dump schemas), for
   dump files and for the exchange alike; no address-only code in the console. *Why:* the Device API already handles
   both; one way of working for all devices.
4. **Exchange with the devices in Play mode only, to start with.** *Why:* smallest step; Edit mode can follow.
5. **Midi.Device gets a public API to read the data of a root from SysEx bytes and to write it as SysEx bytes**,
   without a connection or exchange file, for every kind of message map (address-map data messages and dump
   messages). The console's dump files use it instead of `AddressMapAdapter`. *Why:* the messages in a dump file are
   the same as on the wire; one place recognises and writes them (see *Device messages without an address map*,
   decision 15).
6. **"Dump file" in the console, "dump message" in Midi.Device.** The console names its side explicitly as a dump
   file (e.g. *Open › Dump file…*, the `DeviceState` members and `ValueOrigin`); "dump" alone means the message.
   *Why:* the two were easily mixed up.
7. **No new layer: `IMessageMap` gets `Load` (and later `Save`)**, implemented once in the interface from the members
   every map has (`DataMessages`, `TryRead`, `Split`, `Write`). `IMessageMap` and `MessageMaps` become public; the
   members the exchange uses stay internal. `Save` is on the Device todo. *Why:* no extra type next to the maps; every
   map gets it without code of its own; the Device API is a generic API, not only for the console and Schema Editor.
8. **A dump file holds real SysEx messages**, as the device sends and accepts them: a file and a MIDI connection
   differ only in the target, both need the same message maps. Decisions 5 and 7 are **parked**: the console keeps
   `AddressMapAdapter.ReadData`/`EncodeData` for its dump files for now (address maps only); dump files with dump
   messages come with `Load`/`Save` (Device todo). *Why:* the message exchange is the goal now; dump files are a
   separate feature.
9. **The dashboard names the exchange it requests the data with** (where its exchange file comes from: decision 11).
   *Why:* decision 2; the console does not assume `requestData` or which exchange file a device has.
10. **One dashboard at a time for the exchange, to start with**; several open dashboards are on the Console todo.
    *Why:* smallest step.
11. **Exchange files are sources like schema files:** embedded in an assembly (loaded with the assembly, e.g. the
    Roland devices) or an `.exchange.xml`/`.exchange.json` file the user authors, listed in the sources; a later
    file for the same root replaces an earlier one. The dashboard names, per device, the exchange to receive with
    (to send with, later); it does not name the file. *Why:* the same way of working as for schemas; the user can
    write an exchange file for a device without an assembly.

## Device messages without an address map (2026-11-06)

Issue: the message exchange (`MessageExchange`, `DeviceSession`) finds, builds and recognises every message through
the address map, so a device whose dumps are ordinary schemas (e.g. Korg function-code dumps) cannot be exchanged.
See the Device todo, *Message exchange for schemas without an address map*.

1. **A generic mechanism for device messages, with address as one specialization.** Other specializations will
   follow (e.g. the R3 program index, keyed parameter changes). *Why:* one mechanism instead of a second one next to
   addresses; new device schemes plug in.
2. **Providers, not locators.** What differs per device is more than *which part* a message carries (also keyed
   parameter changes, when a block is complete). A provider owns the link between a plan range and messages; the
   pattern (order, acks, retries, timing) stays in the exchange files, the bytes (packing, checksum) in the processors.
   *Why:* clear boundaries, room for what is not discovered yet.
3. **Two concepts: `IMessageMapProvider` and `IMessageMap`.** The provider claims a plan (`TryCreate`) and returns
   the map of that root; the map holds the operations. `AddressMap` is one map, `DumpMap` the default.
   *Why:* claiming and working with a root are different responsibilities.
4. **A fixed, ordered provider chain in Midi.Device:** address first, the default dump provider last. No registry or
   factories until needed. *Why:* simplest; predictable.
5. **A provider claims when its tags are present, then validates.** It does not decline because its tags are wrong;
   the chosen provider is visible (`Validate`/`Prepare`). The default dump provider reports an error when it cannot
   handle the root. *Why:* no silent fallback that hides a schema error.
6. **Selection per range, i.e. per plan root, cached** (like `AddressMaps` today). *Why:* simplest option.
7. **A minimal `IMessageMap`:** `Root`, `Device`, `DataMessages`, `Message(name)`, `IsData(message)`,
   `Validate(range, splitBy)`, `Split(range, message)`, `Write(message, block, source, device)`,
   `TryRead(message, sysEx, device, out data)`, `IsComplete(block, received)`. Extended when a real need arises.
   *Why:* these are exactly the calls the exchange makes now; nothing theoretical.
8. **Names:** `Split` (not "cut"); `Write` symmetric with `TryRead`. *Why:* self-explanatory, matches the existing
   `AddressMap.Split`.
9. **Recognising and decoding are separate; `Decode` is on `ReceivedData`, not on the map.** The map creates its
   `ReceivedData` with its own way of decoding. *Why:* recognition runs on the receive thread, only data of a
   completed block reaches the sink, a recognised message is not always accepted, and the address map does trial
   reads; a separate member on the map is not needed for that.
10. **The map decides what is data (`map.IsData(message)`); `DeviceMessage` holds schema facts only** (name, range,
    roles). *Why:* a root that is itself a message carries data without a `data` role.
11. **`IsComplete` gets the received data; no coverage object.** *Why:* the exchange already keeps the list; no extra
    interface.
12. **A block is a `PlanRange`.** `AddressBlock` stays inside the address map (and `DataPlan`). *Why:* `PlanRange` has
    `Equals`, `Overlaps` and `Contains` (added to Jacobi.BinarySerializer for this).
13. **The message exchange has no knowledge of addressing.** It validates exchange concerns only (definition, received
    messages carry data, source/sink, which message splits: the first sent message, else the first received data
    message) and delegates the rest to the Device.Messages API (the registry's `Validate`: device values, and the
    map's `Validate` for the provider-specific checks). *Why:* layering; a new provider needs no change in the exchange.
14. **Providers are stateless:** a provider or map never interprets a call based on context of earlier calls. Caches
    for speed are fine (e.g. the learned message overhead of the address map). *Why:* predictable, thread-safe.
15. **Layering: Device.Messages is the base, the message exchange a layer on top** (namespace
    `Jacobi.Midi.Device.Messages`). The base must keep working without an exchange file; not built now, but no
    decision may make it impossible. *Why:* schema-based device messages are useful on their own (offline, tools).
16. **Open-choice lookups belong in Jacobi.BinarySerializer** (forward: choice → bodies; reverse: body → choices, at
    schema and plan level); `AddressProtocol` uses them instead of its own matching. The schema level finds the
    document, the plan level the choice and its path. *Why:* the serializer owns the schema and the open-choice
    mechanism; a single place of truth.
17. **The default dump provider handles (a) a root that is a message itself and (b) a root carried as the body of a
    message's open choice.** Carried only inside another body (nesting) is not supported yet. Case (b) is the address
    map without addresses: finding carriers, `SelectBody` and writing/reading the envelope become shared helpers.
    *Why:* covers the simple dumps; the address-specific code stays separate instead of a type hierarchy.
18. **Simple whole dumps first;** the R3/Prophecy index becomes another provider later. *Why:* smallest step that
    proves the mechanism.
19. **Generic types get generic names:** `AddressMessage` → `DeviceMessage`; `DeviceValues` and `FieldValueStore` move
    out of the `Addressing` namespace; `midi.unit`/`midi.maxDataSize` stay with the address map. No backward
    compatibility. *Why:* a clear split between generic and address code.
20. **The map creates the session's state of a root (`IMessageMap.CreateState()` → `IDeviceState`).** `DeviceSession`
    only orchestrates (answer from the state or fetch, send, raise `StateChanged`); how the state decides is the map's.
    The state is the sink and source of the exchange and `Listen`, and has `Contains(block)`, `CopyTo(range, sink)`,
    merging the client's values for a send (and what is missing to complete it), `Clear(range)` and `Clear()`. The
    address state works per field (access, atomic groups), the dump state per root. *Why:* `DeviceSession` was built
    for addresses only (`WidenToAtomic`, `FieldsOf`, `midi.access` around a generic store); one member on the map
    instead of three; a dump needs no field occurrences (repeats sized by the data are no problem). Named after what
    the user sees: the state of the device.
21. **One generic problem and exception type for all message maps** (`MessageMapProblem`, `MessageMapProblemSeverity`,
    `MessageMapException`, in `Messages`; they replace the `AddressMap*` ones). Every kind of map reports its schema
    problems the same way, located at a schema node. *Why:* the shared helpers (`DeviceMessages`, `ConformingBodies`,
    `DeviceValues`) report problems too, and a client handles an invalid map the same way whatever its kind; the
    base layer (`Messages`) no longer depends on `Addressing` (apart from the provider chain).
22. **The address provider claims a root by its tags:** an address-map tag (`midi.address`, `midi.stride`,
    `midi.repeat`, `midi.atomic`, `midi.access`) on a node of the root, or a data message with an `address` role that
    carries the root. Every other root goes to the dump provider. *Why:* decision 5; a broken address map still gets
    address errors.
23. **A dump is sent whole, from the client's values only** (for now): the dump state does not complete a partial
    send from what it holds or from the device; a source that does not supply every value is told so. The fields
    with a role (e.g. the device id) are not kept in the dump state: the device values supply them. *Why:* the
    smallest working step; completing a partial send of a dump can be added when a device needs it.

## Exchange definitions (2026-11-06)

Issue: the message exchange patterns (request and collect data, send data with or without an acknowledgement) were
code protocols, chosen through schema tags (`midi.message`, `midi.exchange`, `midi.messageInterval`). Devices need
other patterns (handshakes, framing, store requests, prompts) without new code. See the Device todo, *Message
exchange patterns*, and the [Exchange readme](../src/Jacobi.Midi.Device/Exchange/readme.md) for how it works.

1. **The app decides what, how and when:** the range, the name of the exchange and the call. Neither the exchange
   file nor the messages decide which data is transferred. *Why:* one entry point; the device only describes how.
2. **Standard names for the most important patterns** (`ExchangeNames`: `requestData`, `sendData`); a file can add
   names of its own. *Why:* an app asks for a pattern whatever the device.
3. **References go one way:** an exchange file names its root (`for`) and the messages it uses; a schema never refers
   to an exchange file. There are no message kinds: a request is a message like any other. `midi.message`,
   `midi.exchange` and `midi.messageInterval` are removed, with no backwards compatibility. *Why:* the same message
   can be used by several patterns.
4. **Three responsibilities:** the app (range, exchange name, source and sink), the content (the schemas through the
   root's message map: what a message looks like, the blocks, when a block is complete) and the exchange file (the
   pattern and its timing). The exchange is not bound to addresses. *Why:* one place for each concern.
5. **One file per root with the device's timing; XML or JSON, read into the same model.** Files stand on their own
   (no extension); messages are named by their full schema name, so each device gets its own small file. *Why:*
   simple, explicit files.
6. **An exchange is a plain state machine of turns** with `send` and `receive` actions and transitions on events;
   no conditions, no values from replies. Defaults cover the common case (`sent`, `received` → completed; `timeout` →
   timed out). *Why:* small definitions; explicit only where the rule is not enough.
7. **No reason texts in a definition:** the runner writes them from what happened. *Why:* consistent reasons.
8. **The response timeout and message interval are set in the definition**, with defaults; per turn and at run time
   later. *Why:* the timing is a property of the device.
9. **Blocks are split by the root's message map for the split message:** the first message the block turns send,
   else the first data message they receive. The map checks whether it can split for it. *Why:* the exchange knows
   nothing of what a message holds.
10. **An exchange runs once per block; the first block that does not complete ends the call.** No explicit loop and
    no way to go on after a failed block. *Why:* the author describes one block; no partial transfers to reason about.
11. **Retries are an attribute of the `on` (`retry`),** followed by its own `goto` or `result`. *Why:* one element
    says both.
12. **Messages that arrive between turns are queued;** a turn that only sends goes on with `sent`. *Why:* no reply
    is lost.
13. **Messages once per call go in an optional `before` and `after`,** built for the whole range; `after` only runs
    when all blocks completed. Fixed messages (no `address`, `size` or `data` role) can be sent in any turn. *Why:*
    framing and store requests without a loop construct.
14. **A prompt for the user belongs to the exchange** (a literal text on a `receive`), because the exchange must
    already wait for the data when the user is asked. A prompt that waits for the user to confirm is the app's, before
    the call. *Why:* the app decides when a transfer starts.
15. **A device mode is switched by the app**, with an exchange of its own name, before it calls the exchange that
    needs the mode. Dropped: `forEach` with nested blocks, going on after a failed block, values from replies and
    conditions. *Why:* keep the exchange a plain state machine.
16. **`MessageExchange.Execute` replaces `Receive`/`Send`; `DeviceSession` gets everything when it is created** and
    has `Execute`, `RequestData` and `SendData`. *Why:* one entry point per layer.
17. **The Schema Editor's *Send* through the runner is a separate item.** *Why:* not needed for the exchange itself.
18. **`before` and `after` are built together (2026-11-09)**, `before` with tests of its own, though no device uses it
    yet (the Yamaha Bulk Header also needs the address map indirection). They hold turns, like the blocks, so they can
    wait for an ack, retry and `goto` within their part. *Why:* decision 13 as one piece; the same turn model everywhere.
19. **An `after` that fails after a send leaves the sent values in the session's state** (2026-11-09); the result
    reports the failure. *Why:* the data reached the device.
20. **A message can be a root of its own** (named `document.root`, e.g. `Gx700.SoundChangeRequest`), a fixed
    message next to the address map. *Why:* a fixed message is not part of the device's memory.
