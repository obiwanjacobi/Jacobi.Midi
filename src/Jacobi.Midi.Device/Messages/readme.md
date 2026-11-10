# Device messages

The base layer of the Device project: how the data of a schema root is transferred in messages. Everything a device
does is described in the schemas; this layer reads them. The [message exchange](../Exchange/readme.md) is a layer on
top (the patterns: which message is sent, what comes back, timing), and the [address map](../Addressing/readme.md) is
one kind of message map. The base layer is meant to work without an exchange file too (e.g. reading or writing the
data of a device offline); not everything for that is built yet.

The reasons behind this design are in [decisions.md](../../../docs/decisions.md) (*Device messages without an address
map*).

## Message maps

A **message map** (`IMessageMap`) says, for one root, which messages carry its data, how a range of the root is
split into **blocks** (the nodes one message asks for or carries, a `PlanRange` of the root's plan), how a message for
a block is written and how a message of the device is recognised, and when a block is complete. A map is stateless: a
call never depends on earlier calls (caches for speed aside).

A client never picks a map: it passes a `PlanRange`, and the map of the range's root is created on first use and
cached per plan (`MessageMaps`). An ordered chain of **providers** (`IMessageMapProvider`) creates it: the first
provider that claims the root wins.

| Order | Provider | Claims a root | Map |
| ----- | -------- | ------------- | --- |
| 1 | `AddressMapProvider` | with an address-map tag (`midi.address`, `midi.stride`, `midi.repeat`, `midi.transfer`, `midi.access`) on one of its nodes, or carried by a data message with the role `address` | `AddressMap` (see [Addressing](../Addressing/readme.md)) |
| 2 | `DumpMapProvider` | every other root | `DumpMap` (below) |

A provider claims a root by the tags it owns and then reports the problems of those tags itself: a broken address
map gets address-map errors, it is not passed on to the dump map. The chain is fixed in Midi.Device; it becomes
configurable when a provider of another assembly is needed.

## Messages

A **message** (`DeviceMessage`) is a group of the schemas with the nodes that have a role (`midi.role`): the facts
the schema gives. It has no kind: an exchange file gives it its part in a pattern, and the map says what it means for
its root (`IsData`; the address settings of an address map). Messages are found in two ways (`DeviceMessages`):

- **Data messages**, by their data: a group that holds an open choice with `midi.role="data"` whose `memberNodeDef`
  is the root's nodeDef carries the root (with the open-choice lookups of Jacobi.BinarySerializer). A root that is a
  message itself is its own data message.
- **Any other message**, by the schema name an exchange file gives it (document, root and path, e.g.
  `Roland.Message16.RQ1`). A message without a data open choice is recognised by its constants and checksums and by
  the device values in it (`DeviceMessages.TryRecognise`).

The roles `device.<name>` are filled from the **device values** (`DeviceValues`): those the root fixes
(`midi.device.<name>`) with the client's over them. When several roots conform to the same contract, the device
values read from a message select the body (`ConformingBodies`). The roles that depend on a block (the `address` and
`size` of an address map) are filled by the map.

A recognised data message is a `ReceivedData`: the message, its device values and the block it carries;
`Decode(sink)` reads its values into a value model, in the way of the map that recognised it. Recognising and decoding
are separate: data is recognised on the receive thread, and only the data of a completed block reaches the client.

Matching a message of the device (`IMessageMap.Match`) has three outcomes (`MessageMatch`): **recognised**, **not
this message** (other traffic: silent), or a **near miss**: the envelope and the device match, but the message cannot
be read; its `Problem` says why (the reason and location of the serializer's `SerializationException`). The line
between the last two is drawn in one place, `MessageMatch.Failed`: once a data message's body is selected (its
envelope and device values match) every failure is a near miss; before that, a constant that differs (the engine's
`expectedValue`), a device value that differs or a rejection by the value model is not this message, any other
failure of the serializer is a near miss. `TryRead` is `Match` without the problem.

## Dump map

The default map (`DumpMap`) transfers a root as a whole, without addresses. Two kinds of root:

- **A root that is a message itself**, e.g. a function-code dump written out as one message group (an example:
  [DumpMapTests.cs](../../Jacobi.Midi.Tests/Messages/DumpMapTests.cs)).
- **A root carried as the body of a message's open choice**, e.g. a device's program in a vendor envelope, the body
  selected by the device values (an example: [CarriedDumpTests.cs](../../Jacobi.Midi.Tests/Messages/CarriedDumpTests.cs)).

Any range of the root is one block: the whole root. A block is complete after one message, and any message can split
the range (it is the whole root anyway).

Limitations, for now:

- A dump is **sent whole, from the client's values**: a send of part of the root is not completed from the device
  state or the device; a source that does not supply every value gets an `InvalidOperationException`.
- A root that is only carried **inside another body** (nested open choices) is not supported.
- Messages that select **one item** of a repeat by an index (e.g. the Korg R3 program number) or a scope (Prophecy:
  one program, a bank, all) need a provider of their own; see [korg.md](../../../docs/analysis/korg.md).

## Device state

A `DeviceSession` keeps the state of the device per root in an `IDeviceState` that the root's map creates
(`IMessageMap.CreateState`). It is the sink and source of the exchange, and decides what it holds (`Contains`), gives
values to a client (`CopyTo`), takes the client's values for a send (`ClientValues`, `Completes`, `Merge`) and forgets
values (`Clear`). The address map keeps the values per field (with the access of each field and the atomic groups);
the dump map keeps the root as a whole. Field values are kept in a `FieldValueStore` (by path and instance of the
root's plan), which a client can use as its own value model too.

## Validation

`MessageMaps.Prepare` and `PrepareAll` create maps up front so schema problems surface early; `MessageMaps.Validate`
checks a transfer before anything is sent: the device values the messages need are known, and the map can split the
range for the message (`IMessageMap.Validate`). A map that is not valid throws a `MessageMapException` with all its
problems (`MessageMapProblem`), each located at a schema node, whatever the kind of map.

A client outside Midi.Device sees a map through `MessageMaps.Info(root)`: a `MessageMapInfo`, a read-only view of
what a client needs to know about a root, whatever the kind of map (for now: its `Kind`, the `ItemCount` of a repeat
as the map transfers it, `DescribeRole` (how the map supplies a role field of a message that carries the root), the device values the root fixes and the `RequiredDeviceValues` the client supplies, e.g.
`deviceId`). The map's operations stay internal. A map that is not valid throws a `MessageMapException` whose `Kind`
is the kind of map that claimed the root.

Without a connection or exchange file (e.g. dump files), a client reads and writes the data of a root with
`MessageMaps.Load` (the data messages of the root in a list of SysEx messages, decoded into a sink; the others are
reported) and `MessageMaps.Save` (ranges written as one data message per block, from a model of the root's values;
the data message is optional: by default the first one whose device values are known). `MessageMaps.SaveMessages`
does the same from a model of the whole message (envelope and body, e.g. an editor's instance), and
`MessageMaps.Plan` returns the blocks without writing (`TransferPlan`, each block described by its map, e.g. its
address and size). `MessageMaps.ReadMessage` is the mirror of `SaveMessages`: it reads one data message into a model
of the whole message, the body narrowed to the block the map recognises (or it fails with the reason).
`MessageMaps.FindCarriedRoot` tells which root a data message
of the device carries (e.g. to select the body of an open choice before reading a message).
`Kind` (`MessageMapKind`) tells how the data of the root is carried: `Address` (in parts, by address), `CarriedDump`
(whole, as the body of a data message's open choice) or `MessageDump` (the root is a message itself). A client decides
with it what to offer for a root. A `MessageDump` root must be a complete SysEx message (`midi.sysex` on the root); a
root that is neither carried nor a SysEx message has no valid map.

## Types

Namespace `Jacobi.Midi.Device.Messages`.

| Type | Purpose |
| ---- | ------- |
| `MessageMaps` | The message maps of a serializer's roots, created on first use and cached; `Info(root)`, `Load` and `Save` for a client. |
| `MessageMapInfo` | A read-only view of the map of one root: its kind, the device values it fixes and the ones the client must supply. |
| `MessageMapValidator` | Validates the `midi.*` tags of all schemas and the message map of every root, whatever its kind (address map, carried dump, dump that is a message itself), for tools such as the Schema Editor. |
| `TransferPlan`, `PlannedBlock` | How ranges of a root are transferred (`MessageMaps.Plan`): the data message and its blocks, each with a description. |
| `MessageMapKind` | How the data of a root is carried in messages: `Address`, `CarriedDump` or `MessageDump`. |
| `DeviceValues` | The device values by name (`deviceId`, `modelId`, ...): those of the root plus those of the client. |
| `FieldValueStore` | A flat value model of field values by path and instance of a root's plan. |
| `DeviceMessage` | A message group of the schemas with its role nodes. |
| `ReceivedData` | A recognised data message: its device values and block; `Decode` reads its values into a value model. |
| `MessageMapException`, `MessageMapProblem`, `MessageMapProblemSeverity` | All problems found in a map and its messages, each located at its schema node. |
| `MidiProperties` | The `midi.*` tags (properties) of the schemas: their names and descriptions (`Descriptors`), the check of a tag on its own (`IsTag`, `Validate`) and the role (`RoleOf`, `IsData`) and documentation (`DocOf`, `midi.doc`) of a node, for tools such as the Schema Editor; their use in all schemas is validated by `MessageMapValidator`. |

Internal: `IMessageMap`, `IMessageMapProvider`, `IDeviceState`, the members of `MessageMaps` that hand out an `IMessageMap`,
`DumpMap`, `DumpMapProvider`, `DumpDeviceState`, and the shared helpers `DeviceMessages`, `ConformingBodies`,
`EnvelopeSource`/`EnvelopeSink`, and the helpers of `MidiProperties` that read the tags from nodes.
