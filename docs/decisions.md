# User Design decisions

The user design decisions made for the project, per topic: the issue, the decided direction and briefly why. The code
is the source of truth for how a decision was implemented; this file keeps the *why*.

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
