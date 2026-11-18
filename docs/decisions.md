# User Design decisions

The user design decisions made for the project, per topic: the issue, the decided direction and briefly why. The code
is the source of truth for how a decision was implemented; this file keeps the *why*.

## The LCARS look in the console (2026-11-17)

Issue: a third look, after the LCARS-style control design (rounded caps, pastel bars, condensed capitals on black).

1. **LCARS is a module of its own (`Looks/Lcars`), built in steps:** first its palette, typography and chrome (control
   and section) on the kit of the Anodized module, then a kit of its own. *Why:* the look can be judged before the
   faces are drawn; a module may borrow a kit.
2. **The value stays in the chrome**, in a pill in the accent, not drawn inside the knob as in the design: a face only
   gets its face state, not the value text. *Why:* keep the face contract.
3. **The section's header strip shows the title only**, not the device name of the design: a section has a `Header`
   only. *Why:* no new section data for a look.
4. **Fonts a look needs are embedded under `Assets/Fonts`, with their license** (Antonio, SIL Open Font License).
   *Why:* the same type on every platform; condensed system fonts differ per platform.
5. **The frame of the design (elbows, the options on the side) is not part of the look for now**; it may become a
   variant of the (selector) tabs later. *Why:* the look styles controls and sections; start simple.
6. **A face is composed of parts and settings (e.g. a knob's track, arc, cap, pointer, stroke) that a control can
   switch; the look supplies their defaults.** The user can change them per control. *Why:* a new look is mostly a
   combination of options, not a new kit drawn in code; controls added in a look get that look's style.
7. **A face option the control does not set is inherited from the look** (not copied into the control when it is
   added), as the colors: switching the look changes the options the user did not set. *Why:* one rule for
   everything a look supplies.
8. **The knob is the first control with face options**, the other controls follow one by one. *Why:* work out the
   basics on one case.
9. **Options are generic: declared once per control kind** (`FaceOptions` in Console.Core: name, switch, number or
   choice, default), with the values by name (`OptionValues`), not a typed record per kind. The file, the control
   panel and the inheritance are written once; an option name means the same in every kind (`width`, `ends`).
   *Why:* a control kind adds its declaration and its drawing only.
10. **In the dashboard file the options are one `options` element per control** (`"options": { ... }` in JSON),
    only those the control sets, each checked against the declaration. *Why:* apart from the control's own
    attributes; strict as the rest of the file.
11. **The chrome has options too: `plate` and `fasteners`**, for every control with a face. `plate` replaces the
    look's `ControlsOnPanel` (it is the look's default: off for Anodized and LCARS). *Why:* one mechanism for
    every part a look draws.
12. **A plate color has no effect while `plate` is off;** giving a color does not switch the plate on. *Why:* the
    user decides what is shown; a color does not change it.
13. **Foundry's knob became the shared arc knob (`ArcKnobFace`)**: Foundry uses it with all its parts, LCARS
    without body and pointer, with a wide arc and flat ends; a look overrides a single face with `Kit.With`.
    *Why:* one drawing, combined by options.
14. **The value of a knob can be drawn by its face** (amends 2): the knob's option `value` (`below`, `inside`); with
    `inside` the control puts the value text in the face state and gives the chrome none. Only the knob for now; a
    general move of the value from the chrome to the faces was not decided. *Why:* seen on one control first.
15. **The label's place is a chrome option `label`: `above`, `below` or `none`.** *Why:* the LCARS design has its
    labels below; a control may need none.
16. **Every control kind has its options** (slider, switch, selector, number box, text, XY pad, envelope; see the
    console's readme), honoured by the faces of every kit where they have the part; a face ignores an option it has
    nothing for. *Why:* the user switches parts in any look; the looks only differ in their defaults.
17. **LCARS draws with the Foundry kit and its own palette and option defaults, and has one face of its own: the ON/OFF
    pair of keys (`PairSwitchFace`).** *Why:* flat parts are Foundry's parts without their materials; only the switch
    is a different drawing. (Its ▲/▼ arrows follow, in the display text color of Foundry's faces.)

## Loading exchange files in the console (2026-11-17)

Issue: a rig or dashboard could list schema files and assemblies, but not exchange files (decision 11 of *Message
exchange in the console*): a device of an authored schema file could not be synchronised.

1. **Exchange files are a list of their own in the sources** (`SchemaSources.Exchanges`, an `exchange` element,
   `exchanges` in JSON), not schema files told apart by their extension. *Why:* explicit in the file; no extension
   sniffing in the file format.
2. **One *Open…* in the file menu (☰) for every kind of file**: rig, dashboard, schema file, exchange file and dump,
   in place of *Open › Rig… / Dashboard… / Dump…* and the rig's *Schemas from a schema file…*. The end of the file's
   name tells its kind (`ConsoleFileTypes`: `*.rig.xml`, `*.dashboard.json`, `*.schema.xml`, `*.exchange.xml`, `*.syx`,
   ...), with a filter per type and no plain `*.xml`/`*.json` or `*.*` filter; a file whose name does not follow the
   convention is not opened (a message box). The picker that locates a dashboard's rig uses the rig filters too, and
   so do the save pickers (rig, dashboard, generated schema): a name that does not follow the convention is not saved
   (a message box), since *Open…* would not open it.
   *Why:* one way in for every file; a file picker does not tell which filter was chosen, the name does.
3. ***Load assembly…* stays a separate item of the file menu** (in place of the rig's *Schemas from an assembly…*).
   *Why:* an assembly brings several kinds of resources at once (schemas, exchange files, processors).
4. **A loaded file must bring something the rig can use**, else it is not added (reported): an exchange file for a
   root the schemas do not have is refused, as a schema file without a root group; an assembly must bring a root
   group or an exchange file. The library reports such an exchange file too. *Why:* nothing fails silently.

## A field that takes the rest of the data (2026-11-17)

Issue: the name of a File Dump header or request runs up to F7: no length, no terminator. A field without a length
could only take the rest of an enclosing size window (only `sys.bytes`, implicitly), and a message root has no size.

1. **`byteLength="-1"` (a constant) declares that a field takes the rest**: of the enclosing size window, or without one
   of the data, up to the tails the layout processors reserve (e.g. F7, a checksum). The field (or the group that ends
   in it) must be last. Any data type with a length; a referenced length cannot be -1. *Why:* explicit in the schema,
   no format change, no processor needs to tell the plan whether it delimits a value itself (a string terminator).
2. **`sys.bytes` declares it too:** a field without a `byteLength` has no length (reading or writing it fails; the plan
   still builds, it is also used without bytes, e.g. Property Exchange). *Why:* one rule for every data type.
3. **A string that takes the rest is written as is**, without padding, and read without trimming. *Why:* the bytes
   left are the text.

## Universal SysEx messages in the base schemas (2026-11-17)

Issue: the universal System Exclusive messages (7E non-real time, 7F real time) had no schema definitions.

1. **The common messages are in the base `Midi` schema** (General Information, Device Control, the handshake of the
   sample and file dumps); **every other category has a schema of its own, `Midi.<Category>`** (e.g. `Midi.GM`,
   `Midi.SampleDump`, `Midi.CapabilityInquiry`), embedded in Jacobi.Midi.Device. *Why:* a device schema that includes
   `midi` does not get every universal message; a category is loaded when it is needed.
2. **One root per message, on a shared base:** the nodeDef `sysexMessage` holds the `midi.sysex` processor;
   `universalNonRealTime` / `universalRealTime` add the header (`UniversalId`, `DeviceId` with `midi.role="device.deviceId"`);
   a message adds `SubId1`, `SubId2` and its data. A category can add its own header nodeDef and let the message
   override a member of it (`Midi.CapabilityInquiry`: `ciMessage`, each message sets `SubId2`). *Why:* the framing and
   header are written once (see *A nodeDef on a root*); a message is a dump that is a message itself.
3. **A `FieldValueStore` writes a repeat without a count with the items it holds values of** (the first item without
   values ends it). *Why:* the trailing data of a message (e.g. MMC commands) could not be written from a store.
4. **The universal checksum is a processor of its own, `midi.xorchecksum`** (the XOR of the group's bytes, 7 bits), in
   Jacobi.Midi.Device next to `midi.sysex`; not `sys.crc` (a polynomial CRC) and not a generic `sys.checksum`. Like
   `roland.checksum` it appends and checks the byte, which is no field of the schema. It is declared on the message
   root: the checksum covers the whole message between F0 and itself, so no checksum scope group. *Why:* a small,
   specific processor, the same kind as the vendor checksums; a client never computes it.
5. **The 7-in-8 packing is a processor, `midi.pack7`, a field processor of a `sys.bytes` field** (not a dataTypeDef:
   facets cannot change the number of bytes). The `byteLength` of the field is the packed length on the wire, the value
   the unpacked data; `bitorder` says where the high bit of a group's first byte is (`little`: bit 0, Korg; `big`: bit
   6, File Dump). In the `midi` namespace because a universal message uses it. A layout variant for structured dumps
   (Korg) follows when a schema needs it. *Why:* the File Dump data is one blob; the minimal processor that covers it.

## Copy and paste of values in the Schema Editor (2026-11-17)

Issue: the values of a group could only be pasted into a group of the same shape (the same nodeDef or schema node); with
a nodeDef that a node extends (members of its own, overrides), the same nodeDef no longer means the same members.

1. **The values are pasted into the members with the same name, at every level;** the others are skipped. No shape
   check. *Why:* all common fields are useful; a wrong match by name is the user's concern.
2. **The Paste button of a group is disabled when no field matches** the copied values on the clipboard (read again
   when the dialog opens and when the window is activated). *Why:* no paste that does nothing.
3. **The outcome is only a count:** "Pasted 2 of 8 value(s)". Constants and supplied values are not copied. *Why:* a
   partial paste is normal use, not a problem to list.

## A nodeDef on a root (2026-11-17)

Issue: the universal SysEx messages would repeat the `midi.sysex` processor on every root. A root that referenced a
nodeDef did not get its members or processors: the BinarySerializer treated such a root only as a body of an open
choice (the nodeDef as its contract).

1. **A nodeDef reference applies the same way on every node, roots included:** the members of the nodeDef first, then
   the node's own; processors and properties merge as on any other node. *Why:* one rule for all nodes; a root can
   share a base (e.g. a SysEx message with its processor).
2. **Whether a nodeDef is a contract is decided by the open choice** (its `memberNodeDef`), not by the nodeDef or the
   nodes that reference it. A body gets the members of its contract. *Why:* the contract is the open choice's concern.
3. **A member with the name of a nodeDef member overrides it** (the referencing node wins, no error). *Why:* a use can
   adapt one member of a shared base.
4. **Sibling members never share a name;** a duplicate is a compile error. *Why:* members are addressed by name.
5. **An open choice on its own contract is only supported on a concrete node,** not inside the nodeDef that is the
   contract (a clear compile error). *Why:* every body would get the choice again; a concrete node is enough.

## Parameter sources other than message maps (2026-11-14)

Issue: the console only supports parameters of a message map (SysEx through the message exchange). Devices also
expose parameters as channel messages (CC, NRPN, Program Change, MIDI 2.0 controllers) and through MIDI-CI Property
Exchange. See the console todo, *Parameter sources and targets*. In progress: the fit of Property Exchange with the
value model is being examined.

1. **Channel messages are not part of the message maps.** Midi.Device gets a separate API for sending and receiving
   ordinary MIDI messages; `IMessageMap` stays for schema-described device messages. *Why:* different concerns; the
   message maps are about SysEx data described by schemas.
2. **Ordinary MIDI goes through the MIDI 2.0 (UMP) interface,** and works for MIDI 1.0 and MIDI 2.0 messages; ~~the
   MIDI 2.0 drivers do the conversion for legacy devices~~ (revised by 19: the form is chosen by the schema, not
   translated by the driver). *Why:* one path; no MIDI 1.0 byte stream code of our own.
3. ~~**The schemas do not describe ordinary MIDI support** (no CC numbers etc. as schema tags). *Why:* the schemas rest
   on Jacobi.BinarySerializer, which is generic and knows nothing of MIDI; it would become awkward.~~ Revised by 7.
4. **The console's `DeviceState` is the candidate place where the parameter streams come together**, only if it stays
   clean. *Why:* the controls already see only logical values through it.
5. **Property Exchange is one source next to the others, not an umbrella over them:** at worst three streams
   (channel messages, schema-described messages, Property Exchange) that the client merges. The design of the
   Midi.Device API comes first; how the console merges them comes later. *Why:* the PE Common Rules (M2-103 §3.1
   "Use MIDI Messages Whenever Possible", §11) require a CC or Program Change to be used where it can, also for a
   subscribed property; PE does not hide channel messages.
6. **A distinct Midi.Device API per stream; no shared parameter model in Midi.Device (for now).** Message maps,
   channel messages and Property Exchange each get their own API. The client abstracts them in the way that works
   for it; for the console that is the console's value model. Combining the schema-based and PE streams in
   Midi.Device may come later, when the details and problems are better understood. *Why:* a client that knows what
   it wants uses only that part; it keeps clear what each part does.
7. **Channel-message parameters are described in the schemas, in a root of their own** that is tagged for it (as
   the address map root is) and is never transferred as a schema (no data messages). The fields carry `midi.*`
   node properties for the channel message; groups structure them (decision 16); the data type gives the logical
   value. The serializer stays unchanged: the `midi.*` properties are free-form node properties. *Why:* gives the
   simple MIDI messages a shared meaning and structure, reuses data types, node defs and the Schema Editor; the
   tagged root keeps the scenarios apart, prevents failing schema transfers and allows validation.
8. **The MIDI channel ~~and group~~ is a device value**, supplied by the client; the schema author decides (as for
   `deviceId`). The group: revised by 31. *Why:* the same device can be on any channel; it is how the device is reached, not a setting of it.
9. **A schema is device specific:** a field describes the channel message of that device (MIDI 1.0 or MIDI 2.0);
   no field needs a MIDI 1.0 and a MIDI 2.0 variant. Only generic concepts are shared (e.g. through includes).
10. **The channel-message API works on plan ranges, as the message maps do.** Both are schema roots, so the client
    uses them the same way; which stream a parameter takes is the client's choice (question 3 of the plan).
11. **The schema describes channel messages that carry a setting: Control Change, RPN, NRPN and Program Change.**
    Channel Pressure, Pitch Bend, per-note and relative controllers are left out; an app sends those with the
    message types of Jacobi.Midi.Messages through Core. *Why:* only these describe a setting of the device; the
    others are performance data or increments.
12. **One tag on a field: the kind and its number** (the CC number; the parameter number of an RPN/NRPN; none for a
    Program Change). **The resolution comes from the data type** of the field, whether it is a predefined or a
    schema-local data type (no dependency on predefined resolution types). A 14-bit CC is a CC with a 14-bit type (the MSB/LSB pair on a MIDI 1.0 device), not a kind of
    its own. *Why:* few, generic variants; the data type already says how the logical value relates to the
    physical one.
13. **For now the channel-message API gets the physical value of a field by serializing a one-field range** and
    reading the number back from its bytes. A conversion API in Jacobi.BinarySerializer that runs only the value
    pipeline of a field is on its todo list. *Why:* works without a change in the serializer; the whole value
    pipeline of the field (not only the facets) is applied, so there is one place of truth for the conversion.
14. **A channel root can fix the channel** (`midi.device.channel`; ~~`midi.device.group`~~: revised by 31); the device values
    the client passes override them, as for the message maps (`DeviceValues.With`: client over map root). *Why:*
    the same mechanism as `deviceId`; the schema author decides what is fixed.
15. **Tags: `midi.role="cmd"` on the root, `midi.cmd="<kind>#<number>"` on its fields** (e.g. `cc#74`); the role
    value is `cmd1` or `cmd2` since decision 19.
    `midi.role` becomes "the role of a node"; the validator accepts `cmd` on a root only, and the message roles
    (`address`, `size`, `data`, `device.<name>`) not on a `cmd` root. *Why:* no new tag; the role says that the
    fields of the root carry `midi.cmd`. Considered: claiming the root by the presence of `midi.cmd` on its fields
    (as the address provider claims by its tags), but an explicit role makes the intent visible and lets the
    validator report a field without `midi.cmd`.
16. **A `cmd` root holds groups and fields only;** a field without `midi.cmd` is a validation error, and so are
    repeats and choices. *Why:* the root is a lookup of controllers (name, number, data conversion) structured by
    groups. Repeat items would have the same `midi.cmd` and cannot be told apart on the wire; a choice selects a
    layout from a value, and there is no layout. Multitimbral parts are the same root used with other device values
    (channel). An index-based number per repeat item can be added when a device needs it.
17. **Receiving accepts both UMP forms of channel messages** (MIDI 1.0 type 2 and MIDI 2.0 type 4) and assembles the
    multi-message parameters of type 2 itself (NRPN/RPN as CC 99/98 or 101/100 + 6/38, a 14-bit CC as MSB + LSB,
    Bank Select + Program Change), with state per group and channel. *Why:* cross-platform; the drivers of other
    platforms may translate differently or not at all.
18. **Channel messages that match no field of the `cmd` root are ignored, not reported.** *Why:* notes, clock and
    the traffic of other channels are normal, not an error (unlike SysEx that is received but not accepted).
19. **The MIDI protocol of a `cmd` root is explicit in its role: `midi.role="cmd1"` (MIDI 1.0) or `"cmd2"` (MIDI
    2.0).** `midi.cmd` stays one tag with the same kinds for both. The API sends the form the root prescribes:
    `cmd1` as UMP type 2, building the multi-message sequences itself (the mirror of decision 17); `cmd2` as type 4.
    The validator allows the 7- and 14-bit resolution types in a `cmd1` root and the 32-bit type in a `cmd2` root. A
    device that supports both has two roots, which can share groups through node defs. *Why:* cross-platform (no
    dependency on driver translation); the schema is device specific, so the author knows the protocol; one tag on
    the root cannot be forgotten or mixed per field; the protocol is not derived from the resolution types, because
    a MIDI 2.0 device can have a 7-bit range and still receive type 4.
20. **The channel-message API has no offline layer** (no counterpart of `MessageMaps.Load`/`Save` for files): it
    works on a connection, send and listen. *Why:* channel messages are real-time and fleeting; SysEx has dump
    files, channel messages have no such use.
21. ~~**One device uses one MIDI connection**, shared by its streams (message maps and channel messages), until proven
    otherwise.~~ Revised by 22.
22. **The Midi.Device APIs work on an `IMidiConnection`; how it is created and shared is decided outside them.**
    Opening a connection is not the job of the message exchange: `MessageExchange` gets a create that takes an
    existing `IMidiConnection` (the one that opens the connection may remain as a convenience); the channel-message
    API takes an `IMidiConnection` too. No device-connection object in Midi.Device. Each stream may have a connection
    of its own to the same endpoint. *Why:* Midi.Device is a generic API (our apps are test cases); an app that does
    not use the message exchange still opens connections; sharing one connection object between streams invites
    threading problems, and the drivers support more than one client (also for our own apps and library).
23. **`IMidiConnection` raises the received packets with an event (`PacketsReceived`);** `IMidiSession.Connect` no
    longer takes a receive handler. *Why:* an API that is given an existing connection must be able to receive on
    it; every subscriber gets the same packets. Packets that arrive while there is no subscriber are not kept.
24. **A connection is created and opened in two steps:** `IMidiSession.CreateConnection` gives a connection that is
    not open, `IMidiConnection.Open` opens it (once); a failed open faults the connection, the caller disposes it.
    `Connect` remains as an extension method that does both. *Why:* handlers are set up before packets arrive, so
    nothing is missed. The advice to open one connection per endpoint in a session (Windows MIDI Services: each
    native connection has its own buffers and thread) stays.
25. **Names: namespace `Jacobi.Midi.Device.Commands`; an internal `CommandMap` per `cmd1`/`cmd2` root; a public
    `CommandMessages` to send and listen.** *Why:* after the root role and `midi.cmd`.
26. **`CommandMap` is not an `IMessageMap`.** Nearly all members of `IMessageMap` are about SysEx device messages
    (`DataMessages`, `Message`, `Split`, `Write`, `Match`, `TryRead`, `CreateState`); a command map would have to fake
    them. They share helpers instead (the device values a root fixes, `MidiProperties`). The message-map provider
    chain does not claim a `cmd1`/`cmd2` root: asking its message map is an error that says what the root is.
    *Why:* decision 1; no fake implementations.
27. **The command map reports its problems with `MessageMapProblem`, `MessageMapProblemSeverity` and
    `MessageMapException`, as they are.** *Why:* the same kind of problem (a tag on a node); no rename for now.
28. **`CommandMessages`: a constructor on the serializer, an existing `IMidiConnection` and the client's device
    values (it subscribes to `PacketsReceived` and does not own the connection; disposing it unsubscribes),
    `Validate(root)`, `Send(range, source, ct)` and `Listen(range, sink, ct)`.** A field of the range without a
    value in the source is an error that names the fields, and nothing is sent. *Why:* the agreed value-model
    shape; nothing is skipped silently, a partial send is a smaller range.
29. **One helper decides whether a root is a command root** (`midi.role` `cmd1`/`cmd2`, in `MidiProperties` next to
    `RoleOf` and `IsData`). The message-map provider chain uses it to leave command roots alone, the command map to
    refuse other roots; neither codes the other's logic. *Why:* a single place of truth; the message maps stay free
    of command logic. Received packets are already apart by UMP message type, before any map sees them: the SysEx
    port's `SysEx7Assembler` only takes type 3 packets (SysEx7) of its group, so a message map only gets SysEx; the
    command side only takes channel voice (type 2 and 4). No map calls the other to filter received data.
30. **`DeviceSession` is the entry point of the Device API, in the namespace `Jacobi.Midi.Device`.** It is created on
    an existing `IMidiConnection` (the app owns it; the factory's convenience that opens one may own it), the
    serializer, the device values and the UMP groups; the exchange files are optional. It keeps its API and routes
    per root: a message-map root through the message exchange, a command root through `CommandMessages`, with the
    state of each root. On a command root `RequestData` answers from the state only (a value the session does not hold
    is reported as not available: it cannot be requested); `Execute` runs the standard exchanges as `SendData` and
    `RequestData`; `Probe` does not apply. *Why:* both work with schemas on plan ranges; the rig stays device focused.
31. **The UMP group of the channel messages is the group of the session** (the send group to send, the receive group
    to listen), not a device value; `channel` is the only device value of a command root. *Why:* one place for the
    groups of a device, as the endpoint gives them.
32. ~~**In the console a device has its map root and the command roots of the same schema document**, found~~ Revised by 45.
    automatically; the parameter tree shows the command roots next to the map root's nodes. *Why:* the schema is
    device specific (decision 9): the document already says what belongs together; nothing to configure.
33. **MIDI-CI comes from the platform, behind a Core abstraction.** Windows MIDI Services has a MIDI-CI layer
    (`Windows.Devices.Midi2.CapabilityInquiry`: discovery, Property Exchange get/set/subscribe, the foundational
    resources, profiles, and a responder for virtual devices); Jacobi.Midi.Windows implements the Core abstraction
    with it. *Why:* little code of our own, maintained by the platform; another platform implements the abstraction
    (or has none). Considered: our own MIDI-CI layer on `IMidiConnection` (cross-platform, as decision 17), more work.
34. **Property Exchange is tested against the in-box General MIDI synthesizer of Windows MIDI Services** (Get,
    Subscribe, paging; it answers Set with a NAK). A test device for Set (a virtual device with a capability inquiry
    responder) is made when Set is built. The Windows MIDI Services SDK and runtime are upgraded to the latest
    version. *Why:* a real responder that is always there, nothing to build first.
35. **The Core abstraction of MIDI-CI stays raw: a resource request is a JSON header and a body;** no typed resources
    and no profiles in Core. The resources the Device API needs (e.g. `ResourceList`) are parsed in Midi.Device.
    *Why:* no dependency on the platform's types; Core only carries what the specification puts on the wire.
36. **The members of the Core abstraction (`IMidiCapabilityInquiry`):** `Discover` (the responders: MUID, function
    block, Property Exchange support, maximum SysEx size), `GetProperty`, `SetProperty`, `Subscribe`, `Unsubscribe`
    and a `SubscriptionUpdated` event. A header is a `JsonObject` (`System.Text.Json.Nodes`), so every header field of
    the specification passes through; a body is bytes (JSON text, or binary for a resource with a `mediaType`); a
    response has the status, the reply header and the body. *Why:* the minimal set for Property Exchange.
37. ~~**MIDI-CI is created on the connection** (`IMidiConnection.CreateCapabilityInquiry()`).~~ **MIDI-CI is created
    on the API, for a connection** (`IMidiApi.CreateCapabilityInquiry(IMidiConnection)`); a platform without MIDI-CI
    throws `NotSupportedException`. *Why:* the connection stays a plain packet channel; MIDI-CI is an optional
    platform service next to it, as the API already creates sessions and endpoint watchers.
38. **Property Exchange resources work in the existing value model through a generated schema:** the Device API
    generates a schema document from what the device describes (its `ResourceList` and JSON Schemas), one root per
    resource, so a resource is used with plan ranges like the other roots. The API accepts a saved or external
    schema for such a dynamic device, and (re)generates one when none is given or cached. Saving it (for editing in
    the Schema Editor, for offline use) is up to the client: the console can discover dynamic devices on an open
    connection and offer to save the schema. *Why:* one value model for every kind of parameter; a saved schema
    gives the offline bindings and the domains a device leaves out.
39. **First make a generated schema work end to end; `resId` and arrays (repeats of unknown length) come after.**
    A first check compiled a hand-written generated-style document (strings without a length, repeats without a
    count, a string enum, bool and decimal fields) and built its plan without errors.
40. **The rules of the generated schema (`PropertySchemaGenerator`):** a root per JSON resource with a schema,
    `midi.role="pe"` ~~and `midi.resource` (the resource name)~~, named after the resource (revised by 52); `object` → group, `array` → repeat (a count when
    `minItems` = `maxItems`), an array of values has the field `item`, a resource that is an array the repeat
    `items`, a resource that is a value the field `value`; `integer` → `sys.int64`, `number` → `sys.decimal`,
    `string` → `sys.string`, `boolean` → `sys.bool`; `minimum`/`maximum` and `enum` → a data type def; a name is made
    a valid schema name and the JSON key kept in `midi.key`; `description` (else `title`) → `midi.doc`. What cannot be
    generated (no schema, non-JSON data, an unresolved `$ref`, no single `type`) is reported as a note, not skipped
    silently. A `pe` root has no message map. *Why:* a JSON Pointer maps to a path by these rules, and back.
41. **The published JSON Schemas of the standard resources are embedded in Midi.Device** (`StandardSchemas`:
    `DeviceInfo`, `ChannelList`, `ProgramList`, `ChCtrlList`, copied from the MIT licensed MIDI 2.0 Workbench of
    MIDI2.dev) and used when the `ResourceList` gives no schema; embedded as `*.jsonschema`, so nothing that loads the
    schema documents of an assembly takes them. *Why:* a device does not repeat the schemas of the specifications;
    without them its standard resources cannot be generated.
42. **`DeviceSession` reads a `pe` root with a Get of its resource, whole** (one block, the root; the sink gets all
    of it), through a `PropertyExchangeDevice` (the responder and its `ResourceList`, found with
    `PropertyExchangeDevice.Discover`) given to the session. For now Get only: Set and Subscribe come next; a resource
    that needs a `resId` is reported as not available. *Why:* a working end-to-end example first (decision 39).
43. **In the console a Property Exchange device is detected on an open MIDI connection** (an endpoint the user
    selects): discovery, a generated schema, and a device in the rig with its roots (decision 32, for `pe` roots as for
    `cmd` roots). The generated schema is kept in the library, marked as changed in the UI; the user chooses to save
    it (or not). On connect, `RigConnections` discovers the `PropertyExchangeDevice` and gives it to the session. The
    rig has one entry per device, and (for now) one schema file per device, authored or generated. *Why:* a device
    can only be added with a schema; a dynamic device has none until it is detected. We can even take multiple connections ourselves.
44. **The console saves a generated schema right away** (to a file the user picks, added to the schemas of the rig;
    the schema library stays as it is: this revises the saving of 43), **marks the device as detected**
    (`detected="true"` in the rig file, so its schema can be generated again), and **discovers the Property Exchange
    device asynchronously on the open connection** (`DeviceSession.AttachPropertyExchange`); the device is online once
    that is done. *Why:* the simplest change; the session's other streams subscribe before the connection opens, and
    a receive when the device comes online can read the `pe` roots.
45. **In the console a device is a schema document: all its roots belong to the device** (message-map roots, command
    roots, Property Exchange roots); the rig names the document instead of a map root, and the + of the rig offers
    documents (those with device data), not roots. A root without parameters (e.g. a fixed message) does not show in
    the parameter tree. *Why:* one device entry per device (one schema file per device, decision 43); several
    message-map roots in one device; nothing to choose or keep in step. Considered: an explicit list of roots per
    device.
46. **A resource with a `resId` (M2-103 §8.5) is a closed choice named `resId` directly under its `pe` root, with an
    alternative per `resId`** (the name is the `resId`, `midi.key` when it is not a valid name; the structure of the
    resource once, as a `nodeDef`), e.g. `ProgramList.resId.melodic.items[3].title`. A `resId` is a header property
    that selects the whole Property Data of a resource, so it is always at the root. The choice has no
    `selectedIndex`: the value model selects the alternative, which needs an extension of Jacobi.BinarySerializer.
    *Why:* "one of several entries" is what a choice is; the original structure and paths of the resource stay; the
    `resId`s are explicit in the schema and belong to the resource; no new `midi.*` tag. Considered: a root per
    `resId` (renames the root), a repeat with a list of `resId`s in a tag or a data type with `options` (an index
    instead of the name, a new tag) and an open choice (its bodies are roots: renamed paths, unique names, the
    structure copied per body).
47. **In Jacobi.BinarySerializer a choice without `selectedIndex` is open: the value model selects it**, with the
    existing `IOpenChoiceSink`/`IOpenChoiceSource` (read and write alike). Its alternatives are its members (new) or
    the roots that conform to its `memberNodeDef` (as before). `IValueSource.GetSelectedIndex`, which the engine
    never calls, is removed. The result names the selected `Member` (was `Group`; a member can be a field) and gives
    the scope ~~that holds its values, entered without a further call~~ of the choice (revised by 48); it is made with factory methods per case
    (the whole member, one node, a range), and a target on a field is an error. A target in a member group is an
    error when the range of the envelope already begins or ends inside the choice. `valueProcessors` without
    `selectedIndex` is an error. Ranges are named begin/end instead of from/to throughout (`CreateRange`,
    `PlanRange`, the open choice result). *Why:* a choice the model selects is open, whatever its alternatives are;
    one mechanism, symmetric for read and write; one way to express a range; begin/end says range, from/to says
    copy. Considered: `GetSelectedIndex` on write and an error on read (not symmetric); the member's index or
    `NodeInfo` as the result (a different result type per variant).
48. **In Jacobi.BinarySerializer every scope is entered and exited, on the sink and the source alike (the same calls
    with the same parameters: the source also gets the index of a choice and the number of items), and an open node
    differs from a closed one only in its enter call.** Group: `EnterGroup`/`ExitGroup`; repeat:
    `EnterRepeat`/`ExitRepeat` with per item `EnterItem`/`ExitItem` on the repeat's scope; choice:
    `EnterChoice`/`ExitChoice`. An open choice is entered with `EnterOpenChoice`, which returns the selected member and
    the scope of the choice; the member is then handled through that scope with the normal calls (`EnterGroup` for a
    group or body, `SetField`/`GetField`, `EnterRepeat`, `EnterChoice` or `EnterOpenChoice`), and the choice ends with
    `ExitChoice`. An open repeat is entered with `EnterOpenRepeat` (decision 49). Exits are called on the scope the
    enter returned, and all enter/exit members are required. *Why:* the navigation is the same for every node and does
    not depend on what was selected; the selected scope always means the same thing; the sink learns the index of a
    choice member before its fields; a source can follow the structure as a sink does. Considered: entering a member
    directly with the scope of the result (its meaning depended on the kind of member, an open choice ended with
    `ExitGroup`, and a choice member lost its `EnterChoice`); entering only groups directly; source exits as default
    interface methods.
49. **`IValueSource.GetCount` is removed; an open repeat (no count in the schema) is entered with
    `EnterOpenRepeat`** of `IOpenRepeatSource`/`IOpenRepeatSink`, which returns the repeat's scope and the number of
    items when the model knows it, or "item by item": the engine then asks `NextItem(context, index)` on that scope
    before each item. On write an open repeat needs `IOpenRepeatSource` (a flat model's adapter implements it with the
    `EndOfData` probe); on read, without `IOpenRepeatSink` the engine's own rule (end of the input or size window)
    applies. The engine passes the count it knows to `EnterRepeat`/`EnterItem`; the item index is a method
    parameter, not part of the context (`OpenRepeatContext.Index` goes). *Why:* `GetCount` had three uses (the count
    of an open repeat, a check that could not be answered by every model, deriving a count field) that each get a
    single, explicit place; read and write are symmetric; the context describes the node, the parameters the step.
    Considered: the model returning a count from `EnterRepeat` (two ways to give the count); keeping the `-1` answer
    of `GetCount`.
50. **A count field the model has no value for is derived by the engine after the repeat:** the field is deferred
    like a size field (one mechanism, `DeferredField`), the repeat is written as an open repeat, and the number of
    items written is filled in. Deferred fields must nest; overlapping ones are an error. *Why:* models need not keep
    a count next to their list; one place derives counts and sizes. Considered: the model always supplies the count
    field.
51. **The generator takes the `resId`s from the links in the data of the device, and generates what it finds:**
    at detection it Gets the resources it can read without a `resId` and collects every `{resource, resId}` in their
    data (links, M2-103 §13). Every resource that is linked to gets the `resId` choice (decision 46), with an
    alternative per `resId` found (one is fine), whether it requires a `resId` or not; a resource that requires one and
    is not linked to stays a plain root. Where it helps the schema author, the generator says so in `midi.doc` (where
    the `resId`s came from; that a resource can also be read without a `resId`; that no `resId` was found). *Why:* no
    inquiry lists the `resId`s; the links are how a device tells them; a generated schema is touched up by its author,
    and a plain representation of what the device gives is clearer to them than rules in the generator.
52. **A `pe` root is named after its resource; `midi.key` holds the resource name when it is not a valid schema name
    (no `midi.resource`)**, and the same for a `resId` alternative. *Why:* one mechanism for every name that comes from
    the device (properties, resources, `resId`s).
53. **`midi.access` is the access tag of any node, not only of an addressed one** (`readwrite`, `read`, `write`):
    a read-only Property Exchange resource gets `midi.access="read"` on the group that is read-only (e.g. the root
    `DeviceInfo`). No tag of its own for Property Exchange. *Why:* one tag for one concept; a schema author should not
    have to learn a second tag that means the same.
54. **The generator gives a resource with `canSet` `none` `midi.access="read"` on its root;** `full` and `partial`
    get no tag (the default `readwrite`). *Why:* the device says per resource whether it can be written; the
    standard resources (`DeviceInfo`, the lists) default to `none`, so no resource needs a special case.
55. **A Set of a resource with `canSet` `full` uses `midi.transfer="atomic"`, and the session's state completes it**
    (as a send of an address map does: widen to the atomic nodes, the missing values from the state or a Get first);
    for a resource with `resId`s the tag goes on each entry, as a Set goes per entry. `midi.transfer` is, like
    `midi.access`, a tag of any node. *Why:* the same mechanism as the address map, no tag of its own for PE.
56. **`midi.access` and `midi.transfer` are read on plan nodes by `MidiProperties`** (`AccessOf` with inheritance,
    `TransferOf` the node's own; the value checked once, by `Validate`), with `NodeAccess` and `NodeTransfer` in
    `Messages`; where a transfer may be (not on a field, no `individual` inside an atomic node) stays with its user,
    `AddressLayout`. The widening of a `PlanRange` to the atomic nodes it overlaps, by the plan structure, is the
    extension method `WidenToAtomic` (`PlanRangeExtensions`); `AddressMap.WidenToAtomic` adds only the widening to
    whole addresses. *Why:* a `pe` root has no addresses; the readers of a tag belong with the other readers of tags
    (`RoleOf`, `CommandProtocolOf`), a range operation is not a tag reader; one place of truth for each.
57. **The address provider claims a root by `midi.address`, `midi.stride` or `midi.repeat` only** (or a data message
    with an `address` role that carries it), not by `midi.transfer` or `midi.access` (revises decision 22). *Why:* those
    two are tags of any node (decisions 53, 55); `midi.repeat` stays, as it only means something in an address space.
58. **The session keeps a state for a `pe` root** (`PropertyDeviceState`, an `IDeviceState`): a Get goes into the
    state, the sink gets the block from it, and a `RequestData` is answered from it as for any root (`SessionData`). A
    block (the whole resource or an entry) is held whole or not at all. The session keys all its states by the plan of
    the root, not by the message map (a `pe` root has none), so `Invalidate` and `Clear` are one code path. *Why:* a Set
    of an atomic resource is completed from the state (decision 55); the same mechanism as the other roots.
59. **The `pe` state keeps flat field values plus the item counts in a new `FieldValueStoreWithCounts`**, not the JSON
    and not an extended `FieldValueStore`: as a sink it takes the count of every repeat it enters with one, so its data
    must be complete. *Why:* merging JSON is more cumbersome than keeping a dictionary and counts in sync; the count of a
    partial transfer (an address map read up to a target item) is too small, so the existing store and its users stay
    as they are.
60. **A Set of a `pe` root follows the tags of the schema, not the `ResourceList`:** a block (the resource or an entry)
    that is atomic (`midi.transfer="atomic"`) is set whole, any other with `setPartial` and the values of the range by
    JSON Pointer; a read-only root (`midi.access="read"`) is not sent. A block the state does not hold is read first,
    as its fields and items are only known from its data; then the client's values, completed as for an address map.
    *Why:* the schema is the source of truth the author may touch up (the generator writes the tags from `canSet`);
    one rule for every kind of root.
61. **In the console a read-only parameter (`midi.access="read"`, inherited) is shown but cannot be changed, and is
    never sent:** `ParameterInfo.Access` (from `MidiProperties.AccessOf`, now public); a control cannot change it in
    Play mode (`CanChange`; a control with slots only its writable slots), and `RigConnections.Send` refuses it. The
    same for every kind of root. *Why:* a change the device does not take is no change; for an address map it went
    nowhere without a word, for a `pe` root every change was a failed send.
62. **A write-only value (`midi.access="write"`) is not given back by `DeviceSession.RequestData` (for now):** the
    session's state keeps what was sent, but a request only gives what the device sends; the console keeps the value
    the user set in its own `DeviceState`, which a receive does not overwrite. *Why:* the console already has the
    value; giving the state's copy back on a request waits for a client that needs it.
63. **Property Exchange data that is not JSON (`mediaTypes`) is not supported:** the generator leaves such a resource
    out, with a note. *Why:* not needed; the parameters of a device are JSON resources.

## Invalid input in the item editors of the Schema Editor (2026-11-13)

Issue: the item editors (includes, data type defs, processor defs) threw on invalid input, so the box showed a validation
error. An empty required field is a normal state while typing, and an editable drop-down cleared its text when the next
item was selected or added, which wrote that empty text to the model and threw where the user did nothing wrong.

1. **Invalid input is rejected with a warning of the field, not an exception.** `ItemEditor.Edit` parses the input; an
   `ArgumentException` or `FormatException` leaves the model unchanged and becomes the warning of that property
   (`Warnings[Name]`, orange below the field); the next valid input clears it. *Why:* emptying a field and clicking
   elsewhere happens naturally; show it like the other warnings. One place for all item editors.
2. **Editable drop-downs bind their text with `UpdateSourceTrigger=LostFocus`**, like the other editable lists of the
   node editor. *Why:* an editable `ComboBox` changes its text by itself (e.g. when its items change); only the text
   the user leaves in the box is an edit.
3. **Each selected item gets new editor controls** (`CollectionTabViewModel` clears the editor before it creates the
   next one), instead of the view reusing the controls with a new data context. *Why:* a reused editable `ComboBox`
   kept the selection state of the previous item and showed an empty box.

## Schema source viewer in the Schema Editor (2026-11-13)

Issue: there was no way to see a schema as it is serialized (XML, JSON) without saving it and opening the file elsewhere.

1. ***View source…* in the context menu of a schema root**, of an open document (schema tree) and of an embedded
   schema (assembly tree) alike. *Why:* the source belongs to the whole document; where it was loaded from does not matter.
2. **The text is serialized from the schema itself** (`SchemaFileService.SerializeCanonical`: `EditNodeMapper.ToSchemaDocument`
   and the serializer of the format), not taken from the file or resource, and without the source formatting that *Save*
   keeps for XML. *Save* uses the same path (`Serialize` adds the source formatting on top). *Why:* shows what the schema
   model holds; one serialize path.
3. **AvaloniaEdit (`Avalonia.AvaloniaEdit`) for the text**, read-only, with its built-in XML and JSON highlighting and
   its `XmlFoldingStrategy`; JSON folding is our own small strategy (AvaloniaEdit has none). Not AvaloniaEdit.TextMate.
   *Why:* the official Avalonia editor control; coloring and folding out of the box. TextMate needs a native regex
   library that probably does not run in the Browser (WebAssembly) host.
4. **The syntax colors come from the theme palette** (`AppSyntax…Brush` in Themes/Palette.axaml), mapped onto the named
   colors of the built-in highlighting definitions (`SourceSyntax`). *Why:* the built-in colors are made for a light
   background; one place for the colors per theme variant.
5. **XML and JSON as tabs in the header row** (a `TabStrip` with the shared `switch` style, also used by the
   Schema | Workbench switch); format names for the user from `SchemaFileService.DisplayName`. YAML can be added later.
   *Why:* switching formats is a view choice; one style for all such switches.
6. **On Desktop the viewer is a window of its own, in the Browser a modal overlay.** One window per document (opening it
   again brings it to the front), owned by the main window (stays in front of it, closes with it, can be moved to another
   screen). The view decides (`MainView`); the view model only keeps the open viewers (`MainViewModel.SchemaSources`).
   *Why:* a window can sit next to the editor or on another screen; the Browser host has no windows.
7. **The text is a snapshot**, taken when a format is first shown; it does not follow edits yet (see the Schema Editor
   todo). *Why:* start simple; a modal overlay did not need it.

## Data type defs of included schemas in the Schema Editor (2026-11-13)

Issue: the data type drop-downs and the field Value validation only knew the registered types and the data type defs
of the edited document; a def of an included schema counted as a custom type and was not validated.

1. **The drop-downs list the defs of the document's own includes only**, not those of transitive includes, as the
   node def options do. *Why:* the user refers to what the document includes itself.
2. **The validation knows the defs of all (transitive) includes.** *Why:* a def of an include can be based on a def of
   that schema's own include; the value must be checked against the whole chain.
3. **A def of a loaded schema that the document does not include gets a warning** (next to the data type), not the
   "custom data type" note. *Why:* it may resolve when compiled with all loaded schemas, but the document does not
   say it depends on that schema.
4. **One place tells which schemas a document sees: `SchemaSetWorkspace.GetScope` (`SchemaScope`)**, found by name as
   when compiling. The node def options use it too. *Why:* single place of truth for resolving includes.
5. **Every data type in the drop-downs shows what it is, muted, after its name** (`DataTypeOption`): a def its base
   type and the facets that are set (range, number of options, scale/shift, value type), then its processors (at the
   end); a registered type its size
   and CLR type, taken from the descriptor (it has no description). *Why:* the user sees what a type represents
   without opening the def; nothing to maintain for the registered types.

## Following node references in the Schema Editor (2026-11-13)

Issue: node def names and `ref:` paths in the node editor are plain text; finding the node they refer to means
searching the schema tree by hand.

1. **A button next to the property follows the reference** and selects the node it refers to. First for node def names
   (NodeDef, MemberNodeDef) and `ref:` paths; data type defs, processor defs, `pub:` and *Back* come later (see the
   Schema Editor todo). *Why:* a button is the most obvious for the user; start simple.
2. **The lookup is editor-side, on `SchemaSetWorkspace`, with the serializer's rules** (`SchemaName`,
   `SchemaNodeRef.TryParse`/`Resolve`). Not on the compiled `SchemaSet`. *Why:* the schema being edited often does not
   compile, and the compiled nodes are merged copies that do not lead back to the `EditNode` the tree shows.
   `SchemaSetWorkspace` already decides which loaded schema (open document or embedded schema) answers to a name.
3. **A reference into an embedded schema is followed too**: the *Assemblies* tab is shown with the node selected.
   *Why:* follow the breadcrumbs; *Back* will lead the way back later.
4. **A *Back* button returns to the node a reference was followed from** (navigation history, 2026-11-13). Only a
   followed reference (→) is recorded, not every selection; there is no *Forward* and no keyboard shortcut (yet). A node
   that is no longer in a loaded schema (deleted, its document closed) is skipped and the status says so.
   *Why:* start with the known need; a button is the most obvious for the user.
5. **A data type follows to its data type def on the *Data Type Defs* tab of its document** (2026-11-13). Data type
   defs are not in the schema tree, so the document root is selected with the def selected in that tab; *Back* returns
   to the field. Only the data type of a field gets the → button for now, not the `basedOn` of a data type def (the
   history holds nodes only). *Why:* the def is edited there; one place shows it.
6. **The navigation history holds document items too** (2026-11-13; extends 4 and 5). A followed reference records
   where it was followed from: a node or a data type def (selected again on its tab by *Back*). The `basedOn` of a data
   type def now gets the → button. Processor defs will be added the same way. *Why:* references also start at
   document items; one history and one follow path (`MainViewModel.Follow`) for all of them.
7. **A processor name follows to a processor def, or else to the processor of a loaded assembly** (2026-11-13). A
   processor def (as the serializer resolves it) is selected on the *Processor Defs* tab; a `namespace.key` that a loaded
   assembly provides selects its processor namespace in the assembly tree. A name that is neither is an invalid
   reference: the → is disabled and its tool tip says why (a processor def also shows it as a warning). Only where the
   reference lives (own processors, the processor of a processor def); not on inherited rows. *Why:* follow the
   breadcrumbs to what defines the processor; the effective processors are a separate (later) topic.
8. **A data type without a data type def disables the →** (2026-11-13): a registered type (`sys.*`) or a def that is not
   found; the tool tip tells why. For the data type of a field and the `basedOn` of a data type def. *Why:* as for
   processors (7): a button that cannot go anywhere should not invite a click.
9. **Every → is disabled when its reference cannot be followed** (2026-11-13; extends 8): node defs, member node defs
   and the value-or-reference properties too (not set, a constant or `pub:` instead of `ref:`, or not found); the tool
   tip tells why. The lookup is one place, `NodeFacets.FindReference`, used by both the button state and the command.
   *Why:* consistent behaviour for all references.

## Included nodes of an address map in the Schema Editor (2026-11-13)

Issue: the editor kept the included nodes of an address map body one contiguous run (`KeepContiguous`): including a
node also included the nodes in between, leaving one out also left out everything after it. That did not work well.

1. **The included nodes need not be adjacent; a left-out node stays left out.** Each included node is a range of the
   transfer; the map splits it (`IMessageMap.Split`), so a gap is never sent and the messages conform to the schema
   (atomic groups, cuts, maximum size). *Why:* the map already splits; the editor only says what is included.
2. **Adjacent included nodes are not joined into one message** (for now). *Why:* a few extra messages that conform
   to the schema are no problem; keep it simple.
3. **The editor uses the message API (`MessageMaps`), not the message exchange.** *Why:* the editor describes and
   writes the messages; the exchange belongs to a device session.
4. **The editor waits a fixed 500 ms between the messages it sends** (`SchemaInstantiator.MessageInterval`), instead of
   the `messageInterval` of the device's exchange file; the editor does not load the exchange files. *Why:* follows
   from 3; a generous pause suits any device.
5. **A match that is not read through a message map suggests where it is** (always, also when the read succeeds):
   when a map recognises the bytes as one of its data messages, a warning names the data message and the root it
   carries (e.g. a DT1 matched in the `Gx700.AddressMap` root itself). Midi.Device finds them with
   `MessageMaps.FindDataMessage(sysEx, device)`. *Why:* matching in the address map root instead of the data message is
   an easy mistake, and reading raw bytes can also "succeed" with nonsense values; identifying a message is useful
   beyond the editor (e.g. the console).

## Saving a new rig and a new dashboard (2026-11-12)

Issue: *Save* with a new rig and a new dashboard shows two save dialogs in a row; it is not clear which is which, nor
that two files are written.

1. **Before the two dialogs, a message explains that the rig and the dashboard are separate files, and the order:
   first the rig, then the dashboard** (it refers to where the rig is saved); *Cancel* saves nothing. *Why:* the
   user typically keeps a few central rigs for many dashboards, so the rig is not placed next to the dashboard
   automatically; a message is the simplest step for now.
2. **A new rig with an already saved dashboard gets no message and is not placed next to the dashboard.** *Why:* only
   the two dialogs in a row are confusing; the rig's place is the user's choice.

## Collapsible sections in the console (2026-11-12)

Issue: a section always shows all its controls; the user wants to fold one away in Play mode.

1. **`collapsible` is a setting of a section (default off), saved in the dashboard; collapsing is Play mode only.**
   A click on the title collapses it to its title and expands it again. *Why:* an option per section, as asked; Edit
   mode always shows the controls to edit.
2. **In a grid the controls below a collapsed section move up, in Play mode only** (`GridLayout.ShiftsUp`): a control
   moves up as far as every control directly above it moved up and shrank, the least of those. A stack closes up,
   the gaps stay, nothing comes to overlap; the positions in the dashboard do not change. In a row or column the
   others close up by themselves. *Why:* the dashboard's root is a grid, and closing its main groups was the reason
   for the feature. (First decided as "the cells stay taken"; changed the same day.)
3. **Collapsed or not is not saved:** a dashboard opens with its sections expanded. *Why:* it is a view state.
4. **The grid around a collapsed section keeps its size** (also a section around it: it does not shrink, the space is
   at its bottom); every section can be collapsible, not only those on the root. *Why:* passing the shrink up goes
   through every kind of parent (grid, row, column, tabs) and is not needed yet.

## No schema sources on a rig device (2026-11-12)

Issue: a device of the rig had sources of its own (schema files and assemblies, `DeviceInstance.Sources`), added in
its panel. A device is made from a root group the library already has, so they were not needed.

1. **A device has no sources; the rig (and the dashboard) list them.** `DeviceInstance.Sources` and the device panel's
   *Its own schemas* are removed; a `schema` or `assembly` in a device of a rig file is reported as an error, not
   ignored. *Why:* one place that decides what is loaded (see *A dashboard refers to its rig*); exchange files follow
   the same sources (decision 11 of *Message exchange in the console*).

## The label and the selector's options in the console (2026-11-12)

Issue: the size of a control's content (its face) followed from the length of its label and, for a selector, from
the number of options, so controls of the same kind and plate size looked different.

1. **The label never sizes the control**; a label longer than the plate is cut off ("…") and shown in full in a tip.
   Splitting it over more lines was dropped: the extra rows would size the face again (in height).
2. **The selector's presentation (buttons or list) is a setting of the control**, in its display settings, without
   an *Auto* that picks one by the number of options. *Why:* an automatic choice is almost never what is wanted.
3. **The buttons of all selectors have the same size**, whatever their number; more buttons than fit in a row go on
   to the next row. *Why:* two selectors should look alike, not differ by their number of options.
4. **The size of the face is a setting of every parameter control**, apart from the size of its plate (cells): the
   face is no longer fitted (scaled) to the plate. *Why:* controls of the same kind and setting look alike, whatever
   their plate.
   The setting sizes the face only: the plate, its label, value text and the parts of the look (screws) keep their
   size.
5. **A face larger than its plate is cut off**, within the room the label and value text leave for it. *Why:* the user sees it and makes the plate (or the face) fit; no
   automatic scaling that makes one control differ from the others.
6. **The face size is a free number** (a scale), edited with a numeric up/down in steps of 0.1. *Why:* fine control
   where needed.
7. **The face size is set per control only**, not inherited from a section or the dashboard. *Why:* the dashboard's
   zoom (its cell size) already scales everything; start minimal.
8. **The face size runs from 0.5 to 3.0**, defined in one place. 1.0 is the face's own size (its cells).
9. **A selector shows a list unless it is set to buttons.** *Why:* a list fits any number of options.
10. **A selector's face always takes the width of its plate** (amends 3 and 4 for the width): the list's display and
    the buttons stretch across the room the chrome gives the face, never narrower than the face's own size; the
    buttons of a row share the width equally (all equally wider), their height and minimum width stay the same for
    every selector. No setting for it. *Why:* making the plate wider is how the user makes room to show the option
    names in full.
11. **The ▲/▼ step arrows of a display have a fixed width** (a selector's list and the number box), whatever the
    width of the display; the extra width goes to the text. *Why:* a wider list showed wide, mostly empty arrows.
12. **The number box takes the width of its plate too**, as a selector's list (10, 11). *Why:* a number of a choice
    shows the name of its option, which needs the same room.

## Dismissing notifications in the console (2026-11-10)

Issue: the notifications could not be cleared; they hold both reports of what happened and the problems of the
current state (rig, dashboard, schema library), which are derived again on every refresh.

1. **Only reports can be dismissed**, one by one (✕) or all (*Clear*); the problems of the current state stay until
   their cause is gone. *Why:* those describe what is wrong now; hiding them would hide a real problem.

## The console always has a rig (2026-11-10)

Issue: the console started without a rig; devices could not be added until a rig file was opened.

1. **The console starts with a new, empty rig** that has no file yet (*Save rig* asks where). A rig is always open.
   *Why:* devices can be added right away, and no part of the console has to handle "no rig".
2. **A new rig is started from the + menu of the rig (*New rig*),** after confirming to discard unsaved changes.
   *Why:* next to adding devices, where a rig is put together.
3. **A schema file or assembly from the + menu of the rig only brings in its schemas** (and exchange files): it
   becomes a source of the rig (`Rig.Sources`, saved with it) and its root groups are offered by the + menu; no device
   is added. *Why:* an assembly holds many devices; the rig has only those the user adds. Listing and removing the
   rig's sources, and keeping a long + menu manageable, wait for later.

## Unsaved changes when the console is closed (2026-11-10)

Issue: closing the console window discarded the unsaved changes of the rig and the open dashboards without asking.

1. **Closing the window asks one confirmation for all changed files** (the rig and the open dashboards, by name),
   with the console's own confirmation dialog; *Quit* discards them, *Cancel* keeps the window open. No *Save all*
   from the dialog (for now). *Why:* the same discard-confirmation as *Open rig*, *New rig* and *Close dashboard*.
2. **Desktop only.** A browser page cannot wait for an in-app dialog when it is closed, and an OS shutdown does not
   wait either. *Why:* only the desktop window can be held open for the answer.

## A selector with pages in the console (2026-11-10)

Issue: some parameters are only valid for some options of another parameter (e.g. the wave parameters per oscillator
type); the dashboard should show only the parameters of the selected option.

1. **A new selector control that shows a page per option instead of buttons**; the existing tabs layout control stays
   as it is. *Why:* it is a selector at heart (bound to a parameter with `options`); tabs remain plain layout.
2. **The pages are taken from the options in the schema (at design time, like everything else), each tab named by
   the option's name. On creation every option gets a page, in the schema's order; after that the user removes and
   adds pages for options of the schema. The dashboard file keeps the pages as designed, each with the option it
   belongs to. At runtime (Play) only the selected page is determined: the page of the current value; no value (or an
   option without a page) is no page selected.** *Why:* the dashboard is a design on top of the schema; not every
   option needs a page.
3. **One notion of "a control that holds controls" in the model**, so the walks over the control tree do not depend
   on `LayoutControl`; the new control is a `ParameterControl` that also holds its pages. *Why:* one place of truth for
   walking the tree, instead of teaching either the binding code (layout kind with a slot) or every `Children` walk
   (parameter control with pages) about a special case.
4. **The pages hold controls only; no index variables (for now).** *Why:* no known need.
5. **Each page is a plain grid `LayoutControl`, as a page of tabs is; the editor does not offer variables on them.**
   *Why:* drops, moves and placement on a page work unchanged (the page is the target); only the tree walks change.
6. **The controls a control holds are a virtual `Children` on `DashboardControl`** (empty by default), overridden by
   `LayoutControl` and the new control; every tree walk uses it without a type test. *Why:* simplest for the walks;
   one member, no interface.
7. **A page records its option by name, matched without case.** *Why:* readable in the file and the term the console
   uses for a choice; a page survives a change of the stored values.
8. **A page whose option is no longer in the schema is reported, kept (showing its stored name) and never selected**;
   the user removes it or points it to another option. *Why:* the page may hold a design; nothing is lost silently.
9. **A page keeps its option name in its own `Label`**, as a page of tabs is named. *Why:* uses what is there; no new
   member or type.
10. **The new control is called `SelectorTabs`** (the `ParameterControlKind`, the palette item and the file element).
11. **The pages are chosen in a check-droplist on the `SelectorTabs` properties**: every option of the bound parameter
    in the schema's order, checked when it has a page. Checking adds an empty grid page (named by the option, at its
    schema position among the pages), unchecking removes it (an undoable edit). A page's `Label` is not edited
    otherwise. A page whose option is gone is listed checked and marked missing. This replaces pointing a page to
    another option (decision 8). *Why:* one editor covers adding, removing and the fixed names.
12. **Unchecking a page that holds controls removes it without a prompt**; undo brings it back. *Why:* every
    dashboard edit is undoable. **The only page stays** (its check and its Delete button are disabled, the Delete key
    says why): selector tabs without pages show nothing; to remove them, delete the selector tabs. Plain tabs may be
    empty: new tabs start so, and a dropped control becomes a page.
13. **(Re)binding the `value` slot replaces the pages**: all pages are removed and a page is added for every option of
    the new parameter. *Why:* the pages belong to the options of the bound parameter; simple and predictable. Only
    when the resolved parameter changes: a binding that is written again for the same parameter (a move, a device
    alias renamed, another `base`) keeps the pages. The same parameter is the same field of the same device, whatever
    repeat item (`BoundField.IsSameField`): its options are the same. Unbinding keeps the pages.
14. **In Edit mode the pages behave as those of tabs** (the page shown is UI state, a header click selects the page,
    selecting a control shows its page; nothing is sent). **In Play mode the value decides**: a header click sends its
    option, a received or loaded value shows its page, an unknown value or an option without a page shows none.
    *Why:* every page can be designed whatever the device's value. A click on the header of the selected page selects
    the (selector) tabs themselves (both kinds of tabs): their headers may cover all of a small control, which could
    then not be selected, sized or edited any more.
15. **`SelectorTabs` has no chrome**, as tabs: the headers are the selector, the pages hold their own controls.
16. **A single-value control can change its kind to `SelectorTabs` and back** (Properties): it keeps its binding and
    gets a page per option of the bound parameter, as on a first binding; selector tabs that become another kind lose
    their pages (the status says so; undo brings them back). The size stays, except that selector tabs get at least
    their default size (pushing others aside, as a resize; the pages follow). *Why:* a dropped options field becomes
    a selector (the first choice); turning it into selector tabs is the quick way to pages, which need room.
17. **Resizing tabs or selector tabs sizes their pages** to the space below the headers (`GridLayout.PageSize`), but a
    page never gets smaller than its controls need; then the tabs grow around it. *Why:* a page is the drop area for
    its controls; it should fill the control the user sized. Both kinds of tabs behave the same. **A page is not sized
    itself** (no handle, its size fields disabled, `Place` refuses): it has the size of its tabs. **No control is sized
    smaller than its controls need** (`GridLayout.MinimumSize`: for tabs the largest page content below the headers);
    the handle stops there. *Why:* one size for all pages; a resize that would snap back is not offered. A dashboard
    that is opened gets the same (`GridLayout.Complete`): pages that do not fill their tabs are sized (reported, save to
    keep it). The page area of selector tabs is the drop target of the page shown; their headers are the selector tabs.

## An edit that fails in the console (2026-11-10)

Issue: an exception in an edit (e.g. a page of selector tabs changed to a section) crashed the app.

1. **Edits are all or nothing**: an `EditGroup` that fails undoes what it did; `EditHistory` remembers an edit (and
   moves it between undo and redo) only when it succeeded; a refresh of the views that fails takes the edit back.
   *Why:* the dashboard, its file and the history always agree.
2. **A failed edit, undo or redo is a problem in the notifications** ("'…' failed and is not done", with the
   message); the app goes on, and the status says no success. *Why:* nothing goes wrong silently, nor fatally.

## Documentation in the schemas (2026-11-10)

Issue: the schemas have no place for documentation (e.g. text from the manual) that the Schema Editor can author and
the console can show on its controls (as a tooltip).

1. **One tag, `midi.doc`: plain text, possibly multi-line, no markup (for now).** Allowed on any node, any value is
   valid; it describes the node it is on and is not inherited. *Why:* a group's text is about the group, not its
   members.
2. **A newline in XML is written as a character reference in the attribute** (`midi.doc="Line one.&#10;Line two."`),
   not as a `<property>` element and not as a `\n` escape of our own. *Why:* not verbose; standard XML, so no decoding
   code; the serializer writes it back the same way (round trip tested); JSON (`\n`) and YAML hold the same text.
3. **The `midi.*` tags are not address-map specific: `AddressMapTags` is folded into a public `MidiProperties`**
   (`Messages`): the names, `Descriptors`, `IsTag`, `Validate`, `RoleOf`, `IsData` and `DocOf`; the helpers that read
   tags from nodes stay internal. An unknown tag is *"not a midi property"*. *Why:* one place for all tags; `midi.doc`,
   `midi.role` and `midi.device.*` have nothing to do with address maps.
4. **The Schema Editor edits `midi.doc` in a box of its own, not in the property list.** Every node's details (repeat,
   choice, group, field, also node defs) have a multi-line *Documentation* box with the note that it is stored as the
   `midi.doc` property; the Properties tab hides it. One place reads and writes it (`EditNode.Doc`). *Why:* a long
   multi-line text does not fit a property row; one place to edit it.
5. **In the workbench, the slot note is the `midi.doc` of the slot's root** (`Message<n>`) and the field panel has a
   documentation box for the field (written with *Define field*/*Apply*, copied to other slots). The first note of a slot
   without a root creates the root (and the schema). The slot notes are no longer kept in the session file; old notes
   are dropped (no backward compatibility). The note on bytes stays a session note: it also covers bytes that are no
   field. *Why:* what the user learns about a message belongs in the schema; the console can show it.
6. **The console shows the `midi.doc` of the bound fields in the tip of a control's label** (2026-11-12), below the
   full label; read once with the rest of the field's info (`ParameterInfo.Doc`, from `MidiProperties.DocOf`), joined
   per control by `DashboardSession.DocOf` (a control with more documented fields: each under its field name, a field
   in two slots once). No change to Midi.Device was needed. *Why:* the label already has the tip with the full name; a
   tip on the whole control would pop up while the user turns a knob.
7. **Group docs: a layout control shows the doc of the node its own `base` refers to** (2026-11-12): in the tip of a
   section's title and of a tab page's header; a control that only inherits its base shows none. A header of selector
   tabs shows the page's doc and then the selector field's (the options have no node of their own to document). The
   rig tree shows every node's doc in its tip, instead of the detail it already shows as text. One tip for all of these
   (`DocTip`). Midi.Device got `MidiProperties.DocOf(NodeInfo)`: the doc of any plan node (field, group, repeat,
   choice), next to `DocOf(SchemaNode)`. *Why:* an inherited base would repeat the outer doc on every nested control;
   clients work with plan nodes, so reading a doc from one is a generic need, not a console one.

## The data of a root for a client (2026-11-10)

Issue: the console (dump files, `IsMap`, `ItemCount`) and the Schema Editor (instantiate, match, the role fields) both
still use the address-only `AddressMapAdapter`; both must also work with dump messages. A first proposal added an
adapter shaped after the Schema Editor's needs.

1. **Midi.Device is a generic API; the console and the Schema Editor are two example clients.** A change to its API is
   checked against both, not designed from one. *Why:* no API that fits one app only.
2. **A new type or layer is fine for a generic concept of Midi.Device, not to fix a problem of one app.** This is
   the intent of decision 7 of *Message exchange in the console* ("no new layer"); it is not a ban. *Why:* prevents
   layers upon layers of app-specific fixes.
3. **The offline read and write of a root's data (`Load`, `Save`) and the lookup of the root a message carries go on
   `MessageMaps`; the facts of a root go on `MessageMapInfo`. `IMessageMap` stays internal.** Built once from the
   internal `IMessageMap` members, so every kind of map has them. Revises decision 7 of *Message exchange in the
   console* (`Load`/`Save` on a public `IMessageMap`). *Why:* `MessageMaps` is already the public entry point its
   owner creates; the other members of `IMessageMap` (`Split`, `Match`, `IsComplete`, `CreateState`) are for the
   exchange, not for a client.
4. **`IsMap` is replaced by a kind on `MessageMapInfo`: how the data of the root travels** (in parts by address, whole
   as the carried body of a dump message, or the root is the message itself). Every root has a message map since the
   provider chain, so "is a map" no longer tells anything; each app decides what to do with the kind. *Why:* the
   console (*Add device* menu, root order, dump files) and the Schema Editor (the address map UI of a body) branch on
   the same question. Named **`MessageMapKind`** (`MessageMapInfo.Kind`) with `Address`, `CarriedDump` and
   `MessageDump`. *Why:* "kind of map" is the jargon already used; not `MessageKind` (there are no message kinds,
   decision 3 of *Exchange definitions*), not `...TransferKind` (`midi.transfer` means how a group is cut into blocks).
5. **The console offers every root that can be device data as a device**, also one without an exchange file: its
   messages are then sent as is, and received when the user dumps them from the device (Console todo, *Devices
   without message exchange patterns*). *Why:* a device without exchange patterns is still usable.
6. **The dump provider is for SysEx messages: a root that is a message itself must be a complete SysEx message**
   (`midi.sysex` on the root); a root that is neither carried by a data message nor a SysEx message gets an error (no
   valid map), so it is not device data. A range of a dump is widened to the whole root (`DumpMap.Split`; decision 23
   of *Device messages without an address map*). *Why:* otherwise every plain group counted as a dump and was offered
   as a device. Such a root keeps getting an error from the dump provider (it does not decline it; decision 5 of
   *Device messages without an address map*).
7. **The item count of a repeat is a fact of the map: `MessageMapInfo.ItemCount(repeat)`** (`IMessageMap.ItemCount`):
   the items of the address space for an address map, the constant count of the schema for a dump. *Why:* the
   console (parameter tree, index variables) and the Schema Editor (addressed repeats) both need it, whatever the kind.
8. **`MessageMapException.Kind`: the kind of map that claimed the root and found the problems.** A client tells an
   invalid address map (reported) from a root that is no address map (e.g. a plain group: the console takes the
   schema's count). No other context properties for now: `Root` and the `Problems` (document, path, severity) cover
   the known cases. *Why:* both apps need it (the console's item counts, the Schema Editor's note "is an address map,
   but it cannot be used as one").
9. **`MessageMaps.Save(range, source, device, message?)` writes one data message per block; the message is optional.**
   Given: that data message (e.g. the one the Schema Editor's user instantiated); omitted: the first data message of
   the map whose device values are known. The range is split (and widened) by the map for that message.
   `MessageMaps.Load(root, messages, device, sink, warnings)` decodes the data messages of the root and reports the
   others (with the reason of a near miss). They replace `AddressMapAdapter.ReadData`/`EncodeData`; the console's dump
   files use them, so they work for dump messages too (un-parks decision 8 of *Message exchange in the console*).
   *Why:* offline there is no exchange file to name the message; the default covers the console, the parameter the
   Schema Editor. Not the map's own choice per block (`AddressMap.SelectBlocks`): one message per call is predictable.
10. **How a role field is supplied is a fact of the map: `MessageMapInfo.DescribeRole(role)`**, replacing
    `AddressMapAdapter.DescribeRole`. The device values the root fixes are described for every kind of map; the roles
    of a map's own (`address`, `size`, `data`) by the map (`IMessageMap.DescribeRole`). `DeviceValues.NameOfRole`
    gives the device value of a role (`device.deviceId`: `deviceId`), so a client does not parse the role. *Why:* the
    same fact as `RequiredDeviceValues`, seen from the message; a carried dump's root fixes device values too.
11. **The Schema Editor writes a message that carries a dump with `MessageMaps.Save`**, with the instantiated data
    message, the body's values and the device values of the role fields the root does not fix. *Why:* a role field
    the root fixes is no longer typed in the instance, so the map must supply it, as `WriteData` does for an address
    map.
12. **`MessageMaps.FindCarriedRoot(message, sysEx, device)`: the root a data message of the device carries**, tried
    with the map of every root that message can carry (a dump's body selected by its device values, the address map
    of the model). The Schema Editor's Match selects the body of a data open choice with it before the read; another
    body than the selected one is selected with a warning (kept when accumulating). *Why:* the user no longer has to
    pick the body first; the maps recognise the messages, the editor does not.
13. **What is an address map is decided in one place: `AddressMapProvider.Claims`.** `AddressProtocol.FindMapRoots`
    (used by `AddressMapAdapter` and `AddressMaps`) listed every carried root, so a carried dump body was read as an
    address map by the adapter. *Why:* a single place of truth; found when the editor read a carried dump.
14. **`AddressMapValidator` becomes `MessageMapValidator` (in `Messages`)**: it already validated every carried root
    through the provider chain; it now also validates the dumps that are a message themselves (`DumpMap.IsMessageDump`:
    a SysEx root that no data message carries and that has no data open choice). A root that is no SysEx message is
    not validated as a map (it is no device data). The Schema Editor's check follows (`MessageMapCheck`, *Message map*
    in the schema panel). *Why:* the check shows the problems of every kind of map; a plain group or an envelope gives
    no noise.
15. **A root that is a dump message itself is handled like a carried dump in the Schema Editor:** its role fields
    are marked with `MessageMapInfo.DescribeRole` and the instance of the root is written by `MessageMaps.Save` (the
    root is its own data message). An instance of a nested node of it is written plainly. *Why:* the same rules for
    every kind of map; a part of a dump is no message of its own.
16. **`AddressMapAdapter.PlanData`/`WriteData` move into `MessageMaps`, not `MessageMapInfo`** (operations on
    `MessageMaps`, facts on `MessageMapInfo`; decision 3): `Save` takes several ranges (duplicate blocks once),
    reports widening in optional warnings and takes **one model source for the whole message** (envelope and body,
    as `WriteData` does) instead of the body's values only; a `Plan` with the same arguments returns the blocks
    without writing, each with a description by its map (`IMessageMap.DescribeBlock`). *Why:* generic for every kind
    of map (a dump is one block); closes the envelope gap of `Save`; *Describe transfer* is no longer address-only.
    One source rather than a separate envelope source: the clients have one model of the message.
17. **Two shapes of one model source, two methods (2026-11-10):** `Save(ranges, source, …)` takes a model of the
    root's values (the console's `DeviceState`, the exchange's state), `SaveMessages(ranges, source, …)` a model of
    the whole message (the Schema Editor's instance; the map supplies the block roles and the device values the root
    fixes, the model the rest). `Plan` is shared; the map orders the blocks (`IMessageMap.Order`, by address for an
    address map) and describes them (`IMessageMap.DescribeBlock`). `AddressMapAdapter` keeps only the read
    (`Deserialize`, see the Device todo); `PlanData`, `WriteData`, `DataPlan`, `EncodedDataMessage`, `MapOccurrence`
    and its `IsMap`/`ItemCount`/`MapRoots` are removed; `DeviceValues.NameOfRole` is internal again (the editor no
    longer reads device values from its role fields). *Why:* the console has no model of an envelope, the editor's
    model is the envelope; each method keeps one source and the shapes never mix.
18. **The Schema Editor's read loses two cases of `AddressMapAdapter.Deserialize`:** reading the plan of a map root
    directly (raw map bytes without an envelope; ends the map's repeats) is removed, as is the fallback for a message
    the map does not recognise (read into the largest group at the address, with a warning): such a message is a
    plain error with the reason. *Why:* not a typical use case; the Schema Editor works on one schema at a time, so
    messages of another model are very unlikely.
19. **`MessageMaps.ReadMessage(root, message, sysEx, sink, device)`: the mirror of `SaveMessages`.** The map recognises
    the message (`IMessageMap.Match`; a near miss fails with its reason) and the body the model selects for the data
    open choice is narrowed to the recognised block and wrapped by the map (`IMessageMap.WrapBody`: an address map
    ends its repeats there; a dump's body is the whole root). The Schema Editor reads a message with a map body
    through it (with no device values: any device id) and everything else with the serializer. `AddressMapAdapter`
    and `AddressMaps` are removed, with `AddressMap.WrapMap`/`LargestGroupAt` and `AddressProtocol.FindMapRoots`.
    *Why:* the rest of `Deserialize` was generic map knowledge; both apps now use only the generic `MessageMaps` API.

## Messages that are received but not accepted (2026-11-09)

Issue: every SysEx from the port is tried against the messages a transfer can receive; a message that does not fit is
dropped without a trace (`AddressMap.TryRead` catches the serializer's exceptions, `MessageExchange.OnReceived` drops
what nobody recognises). A reply with e.g. a byte outside `validBytes` ends in a timeout that cannot be told apart
from a device that does not answer. The serializer throws on a mismatch; the map treats every exception as "not this
message".

1. **Silent until the envelope and the device match; reported from there on.** Not our message (constants differ)
   or another device (device values differ): silent, a port carries other traffic. The envelope and device match but
   the message fails (checksum, address not in the map, invalid data, no block fits), or it is recognised but outside
   the running block: reported, with the reason. *Why:* a near miss is a problem to show; other traffic is not.
2. **Only the running transfer reports them** (in its result), no event on `MessageExchange` for messages outside a
   transfer. *Why:* the transfer is where a missing reply is noticed.
3. **Jacobi.BinarySerializer reports a complete failure: one exception type** (deriving from
   `InvalidOperationException`) with the direction, a category, the node (path and instance), the byte position, the
   source, and the expected and actual value where they apply. The engine fills in the location by wrapping at the
   boundary of each node and processor call; a processor can report its own category. *Why:* a general purpose library;
   a caller (here: the map telling "not this message" from a near miss) needs facts, not message text.
4. **`Category` is a string, not an enum.** The engine uses its own fixed categories (e.g. a constant that does not
   match, invalid data, end of data, schema); a processor can give its own specific value. *Why:* no telling what
   future processors need.
5. **`Origin` says who reported it: the engine (internal errors) or the processor key** (`sys.crc`, `midi.string`).
   *Why:* together with `Category` it identifies the failure without a central list. Named `Origin`, not `Source`:
   `Exception.Source` already exists (the assembly name).
6. **The engine's categories are string constants in one place:** `expectedValue`, `invalidData` (read),
   `invalidValue` (write), `endOfData`, `schema`; the engine's source is `engine` (a processor key always has a dot). A
   processor that gives no category gets the general one of the direction (`invalidData`, `invalidValue`) with its key
   as the source. *Why:* the existing processor throws need no change. The decisions about Jacobi.BinarySerializer are
   kept in this file.
7. **The block a message arrived during lists what it did not accept** (`BlockResult.Unaccepted`, each with why); a
   block that did not complete names the first in its reason (e.g. after "timeout in turn 'request' …"). *Why:* the
   console shows the reason without changes; the near miss is usually the cause of the timeout.

## How a group is cut into blocks (2026-11-09)

Issue: the GX-700 *Display* lines must be requested individually; a request for both lines times out. With the `range`
unit (Roland RQ1/DT1) `AddressMap.Split` joins neighbouring fields into one block; group boundaries are no reason to
split, and `midi.atomic` only forbids a split inside a group. The schema could not say "per member".

1. **One property, `midi.transfer="atomic|individual"`, replaces `midi.atomic`** (no backward compatibility).
   `atomic`: the group is always one block; `individual`: its members are transferred on their own. Without it the
   message's unit and `midi.maxDataSize` decide. *Why:* both are settings of the same thing; one property rules out
   having both on a group. `transfer` rather than `split`: `split="atomic"` reads as the opposite of what it means.
2. **A value is about the group's own level:** `atomic` forbids a cut inside the group; `individual` requires a cut at
   each boundary of its direct members, and at the group's own start and end (else the first or last member joins the
   neighbours outside). Per field below is reached by nesting `individual`. *Why:* symmetrical; each group decides for
   itself.
3. **On groups and repeats, not on fields** (an error there), for every value. The members of a repeat are its items:
   `atomic` transfers all items in one block, `individual` each item on its own. *Why:* the rules are those of the
   property, not per value; a repeat such as the GX-700 effect chain must be sent whole without an extra group level
   (which would change the paths).
4. **Nesting: a contradiction is an error, a redundancy is not.** `individual` inside `atomic` is an error (a cut that
   is forbidden); `atomic` inside `atomic` is allowed and has no effect (was an error); `atomic` inside `individual`
   is a member that goes whole. A value without effect is not reported (a debug output may come later). *Why:* reuse of
   nodeDefs with atomic sections inside an atomic group.

## A dashboard refers to its rig (2026-11-09)

Issue: a dashboard file did not say what it depends on; the user had to open the right rig first. The idea was to
reference every schema and exchange document (files, and resources of assemblies) from the dashboard.

1. **The dashboard refers only to the rig it was made with (`rig`, `Dashboard.RigPath`), not to the documents.** The
   rig has the schema sources and, through its assemblies, the exchange files. *Why:* simpler; the rig stays the one
   place that decides what is loaded.
2. **The rig is opened only when the references do not resolve:** the open rig does not have the dashboard's devices
   with their map roots (`Dashboard.DeviceProblems`, also used by `BindingResolver`), or no rig is open. Replacing an
   open rig is confirmed; a rig that is not found is located by the user. *Why:* a dashboard is portable between rigs
   that have its devices.
3. **An open rig with the devices that is another file than the reference is used; the notifications say so (info).**
   *Why:* nothing happens silently, but nothing is in the way either.
4. **A located rig updates the reference** (the dashboard counts as changed), and saving writes the open rig as the
   reference, relative to the dashboard file (`FilePaths`, also used for the source paths). *Why:* the file follows
   where the user keeps the rig.

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
   *Revised (2026-11-10):* `Load`/`Save` go on `MessageMaps`, `IMessageMap` stays internal (see *The data of a root
   for a client*).
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
    `midi.repeat`~~, `midi.atomic`, `midi.access`~~; revised by decision 57) on a node of the root, or a data message with an `address` role that
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
