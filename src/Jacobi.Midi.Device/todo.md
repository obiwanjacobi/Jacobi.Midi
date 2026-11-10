# Device - To Do

Known gaps and loose ends of the address map and the message exchange (see [Addressing/readme.md](Addressing/readme.md) and [Exchange/readme.md](Exchange/readme.md)).

## General

- [ ] Add universal sysex definitions to midi.schema.xml
- [ ] Add documentation to schema fields (and groups?) `midi.doc`. Copy text from the manual to show in editor and/or console.
- [x] consolidate midi.atomic and midi.unit? Add an opposite of atomic: individual (per field)? Allow midi.unit on groups? *Done:* `midi.transfer="atomic|individual"` replaces `midi.atomic` (see [docs/decisions.md](../../docs/decisions.md), *How a group is cut into blocks*); `midi.unit` stays a message setting.

## Device messages

- [x] **`IMessageMap.Load` and `Save`**: read the data of a root from SysEx messages and write a range of it as SysEx
  messages, without a connection or exchange file, e.g. for dump files (see [docs/decisions.md](../../docs/decisions.md),
  *Message exchange in the console*, decisions 5, 7 and 8). Built once from `DataMessages`, `TryRead`, `Split` and
  `Write`, so every map has them; public on `MessageMaps` (with the lookup of the root a message carries), the facts
  on `MessageMapInfo`, `IMessageMap` stays internal (*The data of a root for a client*, decision 3). Replaces `AddressMapAdapter.ReadData`/`EncodeData` (used by the console's
  `DeviceState.LoadDump`/`SaveDump`), so the console's dump files work for dump messages too. Open: which data
  message `Save` writes when the root has more than one (offline there is no exchange file to name it): one message
  per call, an optional data message with the first one the map can split the range for as the default; or the map's
  own choice per block, as `AddressMap.SelectBlocks` does now. The Schema Editor's use of `AddressMapAdapter`
  (`PlanData`, `WriteData`) needs the same refactoring for dump messages. *Done:* `MessageMaps.Load` and
  `MessageMaps.Save` with an optional data message (decision 9 of *The data of a root for a client*); the console's
  dump files use them; `AddressMapAdapter.ReadData`/`EncodeData` are removed. The Schema Editor is on its own todo.

- [x] **`MessageMaps.Save` of a carried dump writes only constants and device values in the envelope.** `DumpMap.Write`
  gets the body's values only (`EnvelopeBody` with the client's source); an envelope field that is neither a
  constant nor a role (e.g. a free byte a device expects) has no value, so the write fails. The address map passes the
  client's source for the whole message (`AddressMapAdapter.WriteData`, `EnvelopeSource` with `client`). Decide how a
  client supplies envelope values to `Save` (e.g. an optional source of the message's values next to the body's), for
  every kind of map; the Schema Editor writes a carried dump through `Save` (decision 11 of *The data of a root for a
  client* in [docs/decisions.md](../../docs/decisions.md)). *Decided (decision 16):* one model source for the whole
  message; `Save` with several ranges and warnings, and a `Plan` with a description per block, replacing
  `AddressMapAdapter.PlanData`/`WriteData`. *Done:* `MessageMaps.SaveMessages` (the model of the whole message) next to `Save` (the root's values), and `Plan`; the Schema Editor uses them.

- [x] **`AddressMapAdapter.Deserialize` mixes map rules with Schema Editor policy.** Generic (stays in Midi.Device):
  placing the data of a recognised message at its block of the map and ending the map's repeats (`TryReadData`,
  `WrapBody`). Editor policy: the fallback for a message the map does not recognise (read into the largest group at
  the address, with a warning) and accepting any device id. Decide on a generic read of one message into a model of
  the whole message on `MessageMaps` (the mirror of `Save` with one model source) that reports "not recognised", so
  the fallback can move to the Schema Editor; then `AddressMapAdapter` can go. *Done:* `MessageMaps.ReadMessage`; the fallback and the read of a map root plan are dropped (decision 18), `AddressMapAdapter` and `AddressMaps` are removed (decision 19).

## Message exchange

- [x] **Message exchange for schemas without an address map** (a small redesign of the API). The exchange (`MessageExchange`, `DeviceSession`) is built on the address map: it finds, builds and recognises every message through it (`MessageExchange.MapOf`, `AddressMap.Split`, `AddressMap.Message`, `AddressProtocol.CreateMessage`, `AddressMap.TryReadData`). The exchange files are already generic: they name messages and events, no addresses. A device whose dumps are ordinary schemas (e.g. Korg dumps selected by a function code, see `docs/analysis`) cannot be exchanged. The design is decided (see [docs/decisions.md](../../docs/decisions.md), *Device messages without an address map*): a base layer of device messages (`Jacobi.Midi.Device.Messages`) with message maps per root (`IMessageMap`), created by an ordered chain of providers (`IMessageMapProvider`: address first, the default dump provider last); the exchange and the session work on the interfaces only. Each stage below builds and passes the tests on its own:
  - [x] Stage 1: `AddressProtocol` finds the messages that carry a root with the open-choice lookups of Jacobi.BinarySerializer (`SchemaSet.FindOpenChoices`, `SchemaSet.FindDocument`, `ExecutionPlan.FindOpenChoices`, `GetOpenChoiceBodies`) instead of its own matching (`CarriesContract`, `IsDataOf`, `Qualify`, `SameName`). No change in behaviour.
  - [x] Stage 2: generic names and types. `AddressMessage` becomes `DeviceMessage` (name, range, roles; `midi.unit` and `midi.maxDataSize` stay with the address map); a block is a `PlanRange` (`BlockResult.Block`, `ReceivedData.Block`; `AddressBlock` stays inside the address map and `DataPlan`); `DeviceValues` and `FieldValueStore` move to `Jacobi.Midi.Device.Messages`; the shared helpers move out of `AddressProtocol`: finding the carriers of a root, `SelectBody`, the device values (`MissingDeviceValues`, `DeviceFor`), writing and reading the envelope (`EnvelopeSource`/`EnvelopeSink` for the `device.*` and `data` roles; the `address`/`size` roles filled by the address map).
  - [x] Stage 3: the interfaces, with `AddressMap` as the only map. `IMessageMap` (`Root`, `Device`, `DataMessages`, `Message(name)`, `IsData(message)`, `Validate(range, splitBy)`, `Split`, `Write`, `TryRead`, `IsComplete(block, received)`, `CreateState()`), `IMessageMapProvider.TryCreate`, the registry (today's `AddressMaps`) with the provider chain, the cache per plan and `Validate(range, splitBy, messages, device)`; `IDeviceState` (sink and source, `Contains`, `CopyTo`, merging a send and what is missing, `Clear(range)`, `Clear()`); `ReceivedData` decodes itself (no `Decode` on the map). `MessageExchange` keeps only the exchange checks (no address terms; the split message is the first sent message, else the first received data message) and `DeviceSession` only orchestrates (`_states` by map, no `WidenToAtomic`/`FieldsOf`/`CopyValues`). No change in behaviour.
  - [x] Stage 4: `DumpMap` for a root that is a message itself, with tests: one block (the whole root), complete after one message, a dump state per root. Add the universal Identity Request/Reply to `Midi.schema.xml` (see *General*) as the example.
  - [x] Stage 5: `DumpMap` for a root carried as the body of an open choice, with tests: the carriers found with the serializer lookups, the body selected by device values. A root carried only inside another body is reported as not supported.
  - [x] Stage 6: documentation: a readme for `Messages`, the readmes of `Exchange` and `Addressing` (*devices without addresses are not a concern of the address map* no longer holds), `exchange-definitions.md` (the stage 1 notes on the content binding) and `docs/analysis/korg.md`.
- [ ] **Dependent areas of the session state.** A transfer to one area can change another one on the device without the device telling: a write to the GX-700 *Sound Change Request* (or a program change) replaces the temporary area, storing a patch changes a memory item. Describe these dependencies in the schemas (a `midi.*` tag on the trigger, naming the nodes it changes) and let `DeviceSession` invalidate those nodes after the transfer completed, in the place where it updates its state after a send.
- [ ] **`midi.role="data"` is redundant on a message with one open choice.** When the exchange file names the data message, its open choice is the one that carries the range; the role only tells which open choice is meant when a message has more than one. It is still used to find the data messages without an exchange file (the reverse lookup: which messages can carry a root, e.g. for `Listen` and offline data). Decide whether the role becomes optional (required only on a message with more than one open choice) and update the checks (`AddressProtocol`, `MessageMapValidator`, `AddressMapTags`) and the schemas.

- [ ] **The message interval should be the pause on the wire.** The `messageInterval` of the exchange file is kept from the moment the connection accepted the previous message, but a MIDI 1.0 (DIN) port takes about 0.32 ms per byte (31250 baud) to transmit it: the 77 byte Common section of the GX-700 takes ~25 ms, so the next message may follow without the 20 ms pause the device needs. Add the transmission time of the message to the pause (or make it configurable per connection), in one place for `MessageExchange` and the Schema Editor's *Send*.
- [ ] **Message exchange patterns**: configurable, in exchange files, built up in steps (see [Exchange/readme.md](Exchange/readme.md) and the decisions in [docs/decisions.md](../../docs/decisions.md), *Exchange definitions*).
  - [x] Step 1: the patterns of the old code protocols (one way, acknowledged) as exchange files (`ExchangeDefinitions`) of a map root, run by `MessageExchange.Execute` per block (`ExchangeMachine`); standard names (`ExchangeNames`); `midi.message`, `midi.exchange` and `midi.messageInterval` removed from the schemas; the session's `RequestData`/`SendData`/`Execute`; the first block that does not complete ends a transfer; the Roland GP-8 and GX-700 files; the console and the Schema Editor load the files of their assemblies.
  - [x] The exchange files in XML (`*.exchange.xml`) and JSON (`*.exchange.json`), read into the same tree so the checks are the same; the formats described by `Exchange/exchange.xsd` and `Exchange/exchange.schema.json`.
  - [x] Stage 2: `state` renamed to `turn` (`ExchangeTurn`); retries with a limit per block (`on/@retry`), the reason names the retries.
  - [ ] `DeviceSession.Execute` for an exchange that both sends and receives data: today it throws `NotSupportedException`. Apply both state rules: complete the atomic groups from the state before sending, and put the received data into the state.
  - [x] Stage 3: `before` and `after`: optional turns once per call, around the blocks. Their messages are built for the whole range (`address` = its start, `size` = its size), no `data` message or `receive`; a `goto` stays in its part; `after` only runs when all blocks completed; `TransferResult.Before`/`After` count in `IsComplete`. Fixed messages (no `address`, `size` or `data` role) can be sent in any turn. *Done:* `ExchangeDefinition.Before`/`After`, read from XML and JSON (`exchange.xsd`, `exchange.schema.json`), run by `MessageExchange.Execute` as one transfer; a message name may be a root that is a message itself (`document.root`). Covers:
    - [ ] Yamaha Bulk Header → blocks → Bulk Footer (stored Live Set Sounds of the CP88, CK88, YC). Also needs the Bulk Header/Footer indirection of the address map (`docs/analysis/yamaha.md`).
    - [ ] Store requests after the data (Korg Program Write Request, Yamaha Store To Flash) with their completed/error replies.
    - [x] GX-700 Sound Change Request after a write to the temporary area: a fixed message in the GX-700 schema (`Gx700.SoundChangeRequest`, a root of its own next to the address map: done), sent in the `after` of an exchange of its own: `sendTemporary` in `Gx700.exchange.xml`.
  - [ ] Stage 4: prompts for a manual operation (e.g. "press the bulk dump key"): `receive/@prompt` (literal text), shown through `ExchangeData.Prompt` (`IExchangePrompt.Show(text, ended)`) on entry of the turn and ended when the turns with that prompt are left; cancel through the `CancellationToken`; `turn/@responseTimeout`; a check that a `Prompt` is given.
  - [ ] Roland handshake for sending: two levels of blocks (WSD per section, DAT per piece): WSD/RQD → ACK → DAT → ACK … EOD, ERR → resend, RJC → abort. Receiving with the handshake fits today, once its messages are in the schemas.
  - [ ] Device modes: requests that are only accepted in some modes (Korg i5M, DS-8 panel modes). The app switches the mode with an exchange of its own name before the exchange that needs it.
  - [ ] Commands without data and without a range (a store request on its own), with a standard name. Design the API first.
  - [ ] Listen patterns: what the device starts (a dump after a panel key, waiting for an ack), started by an app call.
  - [ ] Capabilities: which exchanges a root has, and whether they can run with the given device values and data (built on `Validate`).
  - [ ] A choice between messages per block in an exchange file (today fixed in `AddressMap.PlanBlocks`).
  - [ ] An `after` that always runs, also after a failed block (e.g. to restore a device mode).
  - [ ] A run-time override of the response timeout.
  - [ ] The Schema Editor's *Send* through the runner, so the time between messages is kept in one place.
- [ ] **Windows Midi API - use of EndpointTerminalBlocks** is discuraged in favor of using function blocks.
- [ ] **Detect the receive group of a device.** The send and receive group of a device can differ (`MidiGroupPair`): a MIDI 1.0 DIN in/out pair is two independent cables, and a MIDI 2.0 function block can be one-way. The cabling is not in the endpoint's blocks, so only a round trip finds it: `DeviceIdentity` sends the Identity Request on the send group, listens on **all** groups and returns each reply with the group it arrived on. The Schema Editor (MIDI bar) and the Console (device panel) offer a *Detect* that fills in the receive group from it.

---

## Address map

- [x] **A data message carries any aligned block, not only one group occurrence.** `AddressMap.CreateData` and `Decode` only handle one whole group: a few fields, one field of a larger group, or neighbouring groups in one message throw `NotSupportedException`. This also fixes `MessageExchange.Send` with a `range` message whose blocks span more than one group.
  - [x] Let an open choice target a range in Jacobi.BinarySerializer: `FromTarget`/`FromInstance` to `ToTarget`/`ToInstance` (`OpenChoiceSourceResult`/`OpenChoiceSinkResult`, `CreateOpenChoiceRange`), each a group or a field.
  - [x] Accept any aligned block in `CreateData`/`Decode` (`GroupTarget` becomes a block target), the recognition of received data (`TryRead`, `GroupTargets`) and `AddressMapAdapter` (it rejects a client's body narrowed to a range with `NotSupportedException`).
