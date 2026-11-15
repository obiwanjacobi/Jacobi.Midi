# Property Exchange

The parameters of a device that describes itself with MIDI-CI Property Exchange (M2-103): its resources are used with
the same value model as the schema roots, through a schema document generated from what the device describes. The
design decisions are in [docs/decisions.md](../../../docs/decisions.md), *Parameter sources other than message maps*
(decisions 33-40); the plan in [parameter-sources-plan.md](../parameter-sources-plan.md).

MIDI-CI itself comes from the platform, behind `IMidiCapabilityInquiry` in Core (`IMidiApi.CreateCapabilityInquiry`):
discovery, and Get, Set and Subscribe of a resource as a JSON header and a body.

## The generated schema

`PropertyResource.FromResourceList` reads the `ResourceList` of a device; `PropertySchemaGenerator.Generate` makes a
schema document of it, with the JSON Schemas the entries refer to (`midi+jsonschema://<id>`, from the `JSONSchema`
resource). Each JSON resource with a schema becomes a root group with `midi.role="pe"` and `midi.resource`:

| JSON Schema | Schema |
| ----------- | ------ |
| `object` | a group of its `properties` |
| `array` | a repeat (with a count when `minItems` equals `maxItems`) of the properties of its `items`, or of the field `item` |
| a resource that is an array / a value | a root with the repeat `items` / the field `value` |
| `integer`, `number`, `string`, `boolean` | `sys.int64`, `sys.decimal`, `sys.string`, `sys.bool` |
| `minimum`, `maximum`, `enum` | a data type def with the facets or options |
| a key that is not a valid name | a valid name, with the key in `midi.key` |
| `description` (else `title`) | `midi.doc` |

So a JSON Pointer is a path with an instance: `/pitchEnvelope/rates/0` of `X-ProgramEdit` is
`XProgramEdit.pitchEnvelope.rates.item` with instance `[0]`. The document is never serialized: it gives the
structure, the paths and the value domains. It can be saved as a schema file (and edited, e.g. to add the domains a
device leaves out); the client decides.

What cannot be generated is reported in the notes of the result: a resource without a schema (and without a
published one, see below), data that is not JSON, a `$ref` that is not given, a schema without a single `type`.

A `pe` root has no message map: `MessageMaps` refuses it (`MidiProperties.IsPropertyRoot`).

The standard resources (`DeviceInfo`, `ChannelList`, `ProgramList`, `ChCtrlList`) have published schemas, which a
device does not repeat: they are embedded (`StandardSchemas`, from the MIT licensed MIDI 2.0 Workbench of MIDI2.dev).

## Using a device

`PropertyExchangeDevice.Discover(capabilityInquiry)` finds the responder that supports Property Exchange and reads
its `ResourceList`; `GenerateSchema(name)` makes the document (reading the JSON Schemas the entries refer to). Given to
a `DeviceSession` (its `properties`), a `RequestData` of a `pe` root reads the resource with a Get, whole: the sink gets
all of it.

## Not yet

- Set (partial or full) and Subscribe.
- `resId` (an entry of a resource, by a string key): such a resource is reported as not available.
- Arrays whose length only the data gives, in the clients (item counts).
- A structure from the data when the JSON Schema has none.
