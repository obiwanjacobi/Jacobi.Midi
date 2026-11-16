# Property Exchange

The parameters of a device that describes itself with MIDI-CI Property Exchange (M2-103): its resources are used with
the same value model as the schema roots, through a schema document generated from what the device describes. The
design decisions are in [docs/decisions.md](../../../docs/decisions.md), *Parameter sources other than message maps*
(decisions 33-63); the open work in the [Device todo](../todo.md), *Property Exchange*.

MIDI-CI itself comes from the platform, behind `IMidiCapabilityInquiry` in Core (`IMidiApi.CreateCapabilityInquiry`):
discovery, and Get, Set and Subscribe of a resource as a JSON header and a body.

## The generated schema

`PropertyResource.FromResourceList` reads the `ResourceList` of a device; `PropertySchemaGenerator.Generate` makes a
schema document of it, with the JSON Schemas the entries refer to (`midi+jsonschema://<id>`, from the `JSONSchema`
resource). Each JSON resource with a schema becomes a root group with `midi.role="pe"`, named after the resource
(`midi.key` holds the resource name when it is not a valid name):

| JSON Schema | Schema |
| ----------- | ------ |
| `object` | a group of its `properties` |
| `array` | a repeat (with a count when `minItems` equals `maxItems`) of the properties of its `items`, or of the field `item` |
| a resource that is an array / a value | a root with the repeat `items` / the field `value` |
| `integer`, `number`, `string`, `boolean` | `sys.int64`, `sys.decimal`, `sys.string`, `sys.bool` |
| `minimum`, `maximum`, `enum` | a data type def with the facets or options |
| a key that is not a valid name | a valid name, with the key in `midi.key` |
| `description` (else `title`) | `midi.doc` |
| a resource with `canSet` `none` (the default) | the root has `midi.access="read"` |
| a resource with `canSet` `full` | `midi.transfer="atomic"` on the root, or on each entry (`resId`) of a resource with entries: a Set sends all of it |
| a resource with entries (`resId`s) | the root holds a choice `resId` without a selected index: a group per `resId` (`midi.key` when it is not a valid name) with the structure of the resource (the nodeDef `<root>_entry`) |

So a JSON Pointer is a path with an instance: `/pitchEnvelope/rates/0` of `X-ProgramEdit` is
`XProgramEdit.pitchEnvelope.rates.item` with instance `[0]`. The document is never serialized: it gives the
structure, the paths and the value domains. It can be saved as a schema file (and edited, e.g. to add the domains a
device leaves out); the client decides.

What cannot be generated is reported in the notes of the result: a resource without a schema (and without a
published one, see below), data that is not JSON, a `$ref` that is not given, a schema without a single `type`.

No inquiry lists the entries (`resId`s) of a resource: a device tells them with the links in its data (M2-103 §13),
e.g. a `ChannelList` entry links to the `ProgramList` entry of its channel. `PropertyExchangeDevice.GenerateSchema`
reads every resource that needs no `resId` and collects them (`PropertyLinks`). Every resource that is linked to gets
the choice `resId`, with the `resId`s found, also when it does not require one; a resource that requires one and is not
linked to is a plain root. The generator says so in `midi.doc`, for the author who touches up the schema. E.g. the
title of the fourth program of the melodic entry: `ProgramList.resId.melodic.items.title`, instance `[3]`.

A `pe` root has no message map: `MessageMaps` refuses it (`MidiProperties.IsPropertyRoot`).

The standard resources (`DeviceInfo`, `ChannelList`, `ProgramList`, `ChCtrlList`) have published schemas, which a
device does not repeat: they are embedded (`StandardSchemas`, from the MIT licensed MIDI 2.0 Workbench of MIDI2.dev).

## Using a device

`PropertyExchangeDevice.Discover(capabilityInquiry)` finds the responder that supports Property Exchange and reads
its `ResourceList`; `GenerateSchema(name)` makes the document (reading the JSON Schemas the entries refer to, and the
data with the links to the entries). Given to a `DeviceSession` (its `properties`), a `RequestData` of a `pe` root works
per block: the whole resource, or each entry the range covers (a Get with its `resId` in the header); the sink gets an
entry as the selected alternative of the choice `resId`. A resource that requires a `resId` without that choice is
reported as not available.

The session keeps the state of a `pe` root (`PropertyDeviceState`, an `IDeviceState` like that of a message map): what
the last Get of each block gave, held whole or not at all, in a `FieldValueStoreWithCounts` (the field values and the
number of items of each array, which the data gives, not the schema). A `RequestData` is answered from the state when it
holds the block (`SessionData.Session`), else the block is read with a Get into the state; the sink gets the block from
the state, with the item counts of the data. `Invalidate` forgets the blocks the range overlaps.

For a send (Set) the state works as that of an address map: `ClientValues` takes the
client's values of the range (a repeat has the number of items of the schema, of the source, else of the state: the
client cannot change the number of items yet); `Completes` widens the range to the atomic nodes it overlaps
(`midi.transfer="atomic"`, a resource set whole) and needs every value that can be written, from the client or the
state, of a block the state holds; `Merge` takes the client's values over the state, except the read-only ones.
The body of a Set comes from the state, by the rules of the table above in reverse (`PropertyData.ToJson`):
`Body(entry)` is the data of the whole block (for `canSet` `full`), `PartialBody(entry, range)` the values of the fields
in the range that can be written, by their JSON Pointer (`PropertyData.PointerOf`, relative to the block; for `canSet`
`partial` with `setPartial`). A block whose one member is the repeat `items` or the field `value` is a resource that is
an array or a value; a repeat whose one member is the field `item` is an array of values.

A `SendData` of a `pe` root (`SendProperties`) refuses a read-only root (`midi.access="read"`). It first reads the
blocks the state does not hold (their fields and items are only known from the data), then takes the client's values
of the range, reads what completes an atomic block (`Completes`), merges and sends a Set per block: the whole block
(`Body`) when it is atomic, else the values of the range (`PartialBody`) with `setPartial`. A block that is not set is
invalidated.

## Not yet

- Subscribe.
- The client changing the number of items of an array in a Set.
- Arrays whose length only the data gives, in the clients (item counts).
- A structure from the data when the JSON Schema has none.
- Data that is not JSON (`mediaTypes`): not supported (decision 63); the generator leaves such a resource out with a
  note.

## References

- MIDI 2.0 specs (local download): M2-101 MIDI-CI, M2-103 Common Rules for Property Exchange, M2-104 UMP and MIDI 2.0
  Protocol, M2-115 Bit Scaling and Resolution.
- The published JSON Schemas of the standard resources (M2-105 `DeviceInfo`, `ChannelList`, `ProgramList`; M2-117
  `ChCtrlList`, `AllCtrlList`, `CtrlMapList`; M2-112 `StateList`): in the MIDI 2.0 Workbench of MIDI2.dev
  ("C:\Users\Marc\source\repos\MIDI2.0Workbench", `libs/schema`, MIT licensed); the first four are embedded in
  Midi.Device (`StandardSchemas`).
- Windows MIDI Services source and samples: "C:\Users\Marc\source\repos\_microsoft MIDI" (the virtual device of
  [tools/Jacobi.Midi.VirtualDevice](../../../tools/Jacobi.Midi.VirtualDevice/readme.md) is built on its
  `capability-inquiry-virtual-device` sample).
