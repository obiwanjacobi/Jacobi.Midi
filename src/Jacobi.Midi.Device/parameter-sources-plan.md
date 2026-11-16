# Plan: parameter sources in Midi.Device

An overview of the design work for device parameters that are not in a message map: what is decided, what is known,
which questions are open and in what order to work on them. The decisions themselves are recorded in
[docs/decisions.md](../../docs/decisions.md), *Parameter sources other than message maps*; this file only refers to them.

Started from the console todo, *Parameter sources and targets* ([todo](../Jacobi.Midi.Console/todo.md)). The focus is
the Midi.Device API; how the console uses it comes last.

## Terms

- **Parameter**: a setting of the device that the user reads and changes (a filter cutoff, a patch level), as in the
  console (`ParameterNode`, `ParameterReference`). For a message map it is a field of the schema; for channel
  messages a controller; for PE a property of a resource.
- **Device value**: a value needed to talk to the device, not a setting of it (e.g. `deviceId`), as in `DeviceValues`.
  The channel-message counterpart would be the group and channel; the PE counterpart the MUID.
- **Property** (PE): the MIDI-CI term for a value in the Property Data of a resource (M2-103).

## Goal

A client (console, Schema Editor, tools) can read, write and observe the parameters of a device, whichever way the
device exposes them, as logical values with a value domain (range, step, options), without knowing MIDI details.

## The three streams

| Stream | What it is | Description of the parameters | Read back | Status |
|--------|------------|-------------------------------|-----------|--------|
| Message maps | SysEx data described by schemas (address map, dump) | our schema files | yes (exchange `requestData`) | exists: `Messages`, `Addressing`, `Exchange` |
| Channel messages | CC, Program Change, RPN/NRPN, MIDI 2.0 registered/assignable and per-note controllers | our schema files: a tagged root of its own (decision 7) | no; only what the device sends | message types exist in Jacobi.Midi.Messages (`V2`); no parameter API |
| Property Exchange (PE) | MIDI-CI resources with JSON (or binary) data | the device itself, at run time (`ResourceList`, JSON Schema) | yes: Get, Subscribe | nothing yet |

Known constraints:

- All streams go through the UMP interface of Core; the MIDI 2.0 drivers convert for MIDI 1.0 devices (decision 2).
- Channel messages stay out of the message maps (decision 1), but are described in the schemas (decision 7).
- PE does not replace channel messages: where a CC or Program Change can do it, it must be used, also for a
  subscribed PE property (M2-103 §3.1, §11; decision 5). So the streams are merged by the client, or by a layer above them.
- PE runs on MIDI-CI, which is Universal SysEx: it works for MIDI 1.0 devices too.
- A PE resource of a manufacturer only has to give `title` and `type` in its JSON Schema: value domains may be missing (M2-103 §14.4).

## Open questions, most fundamental first

Each question is answered with the user in its own step; the answer goes into docs/decisions.md.

1. **A shared parameter model in Midi.Device, or not?** Do the three streams share one concept in Midi.Device
   (parameter identity, logical value, value domain, capabilities: read, write, observe), or does each stream have
   its own API and only the client merges them? This decides the shape of everything below.
   - Today the value model is in the console (`DeviceState`, `ParameterInfo`), built on the serializer's
     `ExecutionPlan`; Midi.Device has `IDeviceState` per message map.
2. **Where the description of channel-message parameters comes from.** Options to examine: a
   description file next to the schemas (as the exchange files are), the PE controller resources of the device,
   supplied by the client (e.g. a dashboard), or none (the client only names a channel and controller).
   Decided: the schemas after all, with free-form `midi.*` node properties as the address map does, in a tagged
   root of its own (decision 7, revises decision 3).
3. **One parameter, more than one stream.** The same parameter may be a SysEx address and a CC (or a PE property and
   a CC). Who knows that link, and who chooses the stream for a read or write?
4. **The channel-message API.** Open points:
   - [x] the tag on a field: one tag, kind + number; kinds CC, RPN, NRPN, PC (decisions 11, 12);
   - [x] the resolution of the device value: from the data type of the field, predefined or schema-local
     (decision 12);
   - [x] channel and group fixed on the root, overridden by the client (decision 14);
   - [x] the tag on the root and the field tag: `midi.role="cmd"`, `midi.cmd` (decision 15); the role is `cmd1` or
     `cmd2` (decision 19);
   - [x] the structure of a `cmd` root: groups and fields only, every field with `midi.cmd` (decision 16);
   - the up/down scaling (M2-104 appendix D.1): not needed for sending (a `cmd1` root sends its 7/14-bit value as
     is, a `cmd2` root has 32-bit physical values, decision 19); only when receiving the other form (decision 17):
     type 4 for a `cmd1` root is scaled down, type 2 for a `cmd2` root up;
   - [x] the logical/physical conversion of the data type outside a transfer: serialize a one-field range for now;
     a conversion API is on the Jacobi.BinarySerializer todo list (decision 13);
   - [x] recognising received messages in both UMP forms (MIDI 1.0 type 2, MIDI 2.0 type 4), assembling the
     multi-message parameters of type 2 (decision 17); unrecognised messages are ignored (decision 18);
   - [x] sending: the UMP form follows the root's role, `cmd1` → type 2 with the sequences built by the API,
     `cmd2` → type 4 (decision 19);
   - [x] the API itself: its types and members, and where it sits in Midi.Device.
     - [x] no offline layer: send and listen on a connection (decision 20);
     - [x] the APIs work on an `IMidiConnection`, created and shared outside them; `MessageExchange` gets a create on an existing connection (decision 22);
     - [x] receiving on an existing `IMidiConnection`: `PacketsReceived` event on the connection (decision 23);
       connections are created and opened in two steps (decision 24);
     - [x] value model: `Send(range, source)` and `Listen(range, sink)` on a connection with device values;
     - [x] types: namespace `Commands`, internal `CommandMap` (not an `IMessageMap`), public `CommandMessages`;
       problems as `MessageMapProblem` (decisions 25, 26, 27);
     - [x] the members of `CommandMessages`; a missing value in `Send` is an error (decision 28);
     - [x] a single helper for "is a command root", used by both sides (decision 29);
     - [x] the members of `CommandMap` (internal; settled while building). Built: namespace `Commands`, see
       [Commands/readme.md](Commands/readme.md).
5. **A MIDI-CI base.** Discovery, MUIDs, request ids, chunking, NAK/ACK and timeouts (M2-101). Needed before PE.
   Decided: the platform's MIDI-CI behind a Core abstraction (decision 33). Test partners: the in-box General MIDI
   synthesizer of Windows MIDI Services (a PE responder: get, subscribe, paging; no set) and a virtual device with a
   capability inquiry responder (sample `capability-inquiry-virtual-device` of the Windows MIDI repo).
6. **The PE API.** A schema generated from what the device describes, one `pe` root per resource; JSON Pointer as
   path with instance; JSON Schema as value domain (decisions 38-42). Open points:
   - [x] the generated schema (`PropertySchemaGenerator`, `PropertyResource`), with the published schemas of the
     standard resources (`StandardSchemas`); it can be saved as a schema file;
   - [x] discovery (`PropertyExchangeDevice`) and Get of a `pe` root through `DeviceSession` (whole resource);
   - Set (full or partial); needs a test device (the General MIDI synthesizer does not answer a Set);
   - Subscribe: following the changes of the device;
   - [x] `resId`: a resource with several entries, a choice `resId` without `selectedIndex` (decisions 46-52): the
     `resId`s from the links in the data, the generated choice, the Get per entry in `DeviceSession`;
   - a structure from the data when the JSON Schema has none;
   - non-JSON resources (`mediaTypes`), possibly decoded by a schema (now reported in the notes).
7. **The console.** How `DeviceState`, bindings and the parameter tree use the result. Open points:
   - [x] detecting a device on an endpoint, saving its generated schema, marking it as detected (decisions 43, 44);
   - [x] a device is a schema document: all its roots are the device's (decision 45);
   - [x] receiving `pe` roots (and sending command roots) without a dashboard exchange; an index variable on a repeat
     without a count is no error;
   - following the device (`DeviceSession.StateChanged`), see the console todo;
   - a detected device without its schema file: generate it again, see the console todo;
   - the item count of a repeat from the data, see the console todo.

## Steps

- [x] Question 1: shared parameter model or separate APIs. *Done:* a distinct API per stream; the client (the
  console) abstracts them (decision 6).
- [x] Question 2: description of channel-message parameters. *Done:* in the schemas, a tagged root of its own;
  channel and group are device values (decisions 7, 8, 9).
- [x] Question 3: one parameter in more than one stream. *Done:* the channel-message API works on plan ranges like
  the message maps; the client chooses (decision 10).
- [x] Question 4: channel-message API (first stream to build: smallest, needed by PE anyway). *Done:* `Commands`
  (`CommandMessages`); `MessageExchange` on an existing connection is in the [Device todo](todo.md).
- [x] Question 5: MIDI-CI base. *Done:* `IMidiCapabilityInquiry` in Core (`IMidiApi.CreateCapabilityInquiry`), on the
  Windows SDK's capability inquiry session (decisions 33-37). Checked against the General MIDI synthesizer: Discover,
  Get (`ResourceList`, `DeviceInfo`, `ProgramList` with paging, `ChCtrlList`, an unknown resource: status 404) and a
  subscription to `ChannelList` (a program change gives a `notify`). Found: a Set to the synthesizer gives no
  response after the timeout (its chart says NAK); the SDK subscribes by resource and resource id only.
- [ ] Question 6: PE API. Get works end to end against the General MIDI synthesizer, also of the entries of a
  resource (`resId`); Set and Subscribe are open (see question 6).
- [ ] Question 7: console integration. Detecting a device and reading its `pe` roots work; following the device
  and the item counts from the data are open (see question 7).

## References

- MIDI 2.0 specs (local download): M2-101 MIDI-CI, M2-103 Common Rules for Property Exchange, M2-104 UMP and MIDI 2.0
  Protocol, M2-115 Bit Scaling and Resolution.
- The published JSON Schemas of the standard resources (M2-105 `DeviceInfo`, `ChannelList`, `ProgramList`; M2-117
  `ChCtrlList`, `AllCtrlList`, `CtrlMapList`; M2-112 `StateList`): in the MIDI 2.0 Workbench of MIDI2.dev
  ("C:\Users\Marc\source\repos\MIDI2.0Workbench", `libs/schema`, MIT licensed); the first four are embedded in
  Midi.Device. The specification documents of these resources are not downloaded.
- Windows MIDI2 driver source and examples: "C:\Users\Marc\source\repos\_microsoft MIDI"
