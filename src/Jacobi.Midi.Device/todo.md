# Device - To Do

Known gaps and loose ends of the address map and the message exchange (see [Addressing/readme.md](Addressing/readme.md) and [Exchange/readme.md](Exchange/readme.md)).

## Address map

- [x] **A data message carries any aligned block, not only one group occurrence.** `AddressMap.CreateData` and `Decode` only handle one whole group: a few fields, one field of a larger group, or neighbouring groups in one message throw `NotSupportedException`. This also fixes `MessageExchange.Send` with a `range` message whose blocks span more than one group.
  - [x] Let an open choice target a range in Jacobi.BinarySerializer: `FromTarget`/`FromInstance` to `ToTarget`/`ToInstance` (`OpenChoiceSourceResult`/`OpenChoiceSinkResult`, `CreateOpenChoiceRange`), each a group or a field.
  - [x] Accept any aligned block in `CreateData`/`Decode` (`GroupTarget` becomes a block target), the recognition of received data (`TryRead`, `GroupTargets`) and `AddressMapAdapter` (it rejects a client's body narrowed to a range with `NotSupportedException`).

## Message exchange

- [ ] **Message exchange for schemas without an address map** (a small redesign of the API). The exchange (`MessageExchange`, `DeviceSession`) is built on the address map: it finds, builds and recognises every message through it (`MessageExchange.MapOf`, `AddressMap.SelectBlocks`, `AddressProtocol.CreateRequest`, `AddressMap.TryReadData`). A device whose dumps are ordinary schemas (e.g. Korg dumps selected by a function code, see `docs/analysis`) cannot be exchanged. Decide what a request and a unit of transfer are without addresses, then split the types below into a generic part (exchange) and an address part (address map). The exchange vocabulary is already generic (`TransferResult`, `BlockResult`, `BlockStatus`, `NodeAccess`, `ExchangeMessageKind`); these types are generic in concept but address-bound in content:
  - `AddressBlock(AddressRange Addresses, PlanRange Nodes)`: the unit of transfer, the nodes one message carries. Without a map there is no address: an optional address, or a generic block plus an address block.
  - `AddressMessage`: a tagged message (`midi.message`) with its kind, interval, maximum data size, device value roles and `data` open choice (generic), plus the `address`/`size` roles and `CreateRequest(addresses)` (address).
  - `AddressUnit` (`midi.unit`): `field` and `atomic` are generic (one field; one indivisible group or a whole dump), `range` (contiguous addresses) is address-bound.
  - `AddressProtocol` (internal): finding the requests, data messages and replies of an envelope, recognising replies and `midi.exchange` are generic; which data messages carry the map and the address space are address-bound.
  - `Transfer`, `ExchangeMessage`, `ReceivedData`: generic names, but they carry the types above and follow from them.
  - Strictly address map, unchanged: `AddressMap`, `AddressMaps`, `AddressLayout`, `AddressLayoutNode`, `AddressOccurrence`, `AddressRange`, `AddressSpace`, `AddressMatch`, `AddressMapAdapter`, `MapOccurrence`, `DataPlan`, `EncodedDataMessage`, `AddressMapException`, `AddressMapProblem`, `AddressMapProblemSeverity`, `AddressMapValidator`, `AddressMapTags`.
- [ ] **Dependent areas of the session state.** A transfer to one area can change another one on the device without the device telling: a write to the GX-700 *Sound Change Request* (or a program change) replaces the temporary area, storing a patch changes a memory item. Describe these dependencies in the schemas (a `midi.*` tag on the trigger, naming the nodes it changes) and let `DeviceSession` invalidate those nodes after the transfer completed, in the place where it updates its state after a send.

- [ ] **The message interval should be the pause on the wire.** `midi.messageInterval` is waited after the connection accepted the message, but a MIDI 1.0 (DIN) port takes about 0.32 ms per byte (31250 baud) to transmit it: the 77 byte Common section of the GX-700 takes ~25 ms, so the next message may follow without the 20 ms pause the device needs. Add the transmission time of the message to the pause (or make it configurable per connection), in one place for `MessageExchange` and the Schema Editor's *Send*.
- [ ] **Message exchange patterns** beyond the built-in `oneWay` and `acknowledged` protocols (`ExchangeProtocols`), as state machines (`ITransferMachine`) and later configurable.
  - [ ] Roland handshake protocol: WSD/RQD → ACK → DAT → ACK … EOD, ERR → resend, RJC → abort, with retries and timeouts.
  - [ ] Framed transactions: Yamaha Bulk Header → blocks → Bulk Footer (stored Live Set Sounds of the CP88, CK88, YC).
  - [ ] Commands without data: write/store requests (Korg Program Write Request, Yamaha Store To Flash) with their completed/error replies.
  - [ ] Device modes: requests that are only accepted in some modes (Korg i5M, DS-8 panel modes), with mode request/change messages.
  - [ ] A hook for a manual operation on the device (e.g. "press the bulk dump key"): the exchange asks the client to prompt the user and waits for the data or a cancel.
