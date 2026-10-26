# Address map

An address map says where each setting of a device lives in the device's address space. With it, the
[message exchange](../Exchange/readme.md) turns a range of schema nodes into the requests to send (Roland RQ1, Yamaha
Parameter/Dump Request) and interprets the data messages that come back (Roland DT1, Yamaha Parameter Change/Bulk
Dump).

The mechanism is driven by **schema definitions only**. Everything vendor specific is in the schemas: the message
layout, framing and checksum, plus `midi.*` tags that say what the nodes *mean*. There is no vendor code.

- A **device schema** describes the settings of a device as an ordinary schema root, the *map root*, tagged with addresses.
- A **vendor envelope schema** describes the messages (request, data, replies) and tags the nodes that hold the address, the size, the device values and the data.
- The map root and its envelopes are linked by the open choice contract: the map root declares the `nodeDef` that is the `memberNodeDef` of the envelope's data open choice.

The address map is internal: it is deduced from the plan of a range (`range.ExecutionPlan`) when the range is first
used, and cached per plan (`AddressMaps`). Clients only see `PlanRange`s and the public types below.

Devices without addresses (e.g. Korg dumps selected by a function code) are not a concern of the address map: their
dumps are ordinary schemas.

## Schema properties

All tags are free-form node properties in the `midi` namespace; the BinarySerializer leaves node properties
untouched. A `device.<name>` role gets its value from `midi.device.<name>` in the map or device context; the
`address`, `size` and `data` roles get theirs from the transferred block.

### Device schema (address map)

| Property | On | Meaning |
|----------|----|---------|
| `midi.address` | the map root and any node below it that is not inside a repeat | The **absolute** start address of the node. Optional: the map root starts at address 0 without it; a node below the root without it follows its previous sibling (previous start + previous size); the first member starts where its parent starts. Not allowed inside a repeat item (all items would get the same address). |
| `midi.stride` | any addressed node | The number of addresses the node occupies: the distance from its start to the start of the next node. On a **repeat** it is the size of one item, the distance between items. Optional: the default is the computed size (the encoded byte count, one address per data byte). It must be ≥ the computed size; the addresses after the content are reserved. This is how gaps inside repeated or reused blocks are expressed. |
| `midi.repeat` | repeat | The number of items the repeat has in the address space. The repeat's `count` states how many items a dump holds and is checked by the serializer; a device map does not know that, so its repeats leave `count` out (read until the end of the data) and give `midi.repeat`. Optional: without it a constant `count` is used (a fixed list inside a patch). Not allowed on other nodes. |
| `midi.atomic` | group, repeat item, map root | `true`: the group is **indivisible**; it is always received and sent as a whole (a GP-8 patch, Yamaha dump blocks; a device that only does complete dumps has an atomic root). |
| `midi.access` | any addressed node, inherited | `readwrite` (default), `read` (e.g. Yamaha System Information) or `write` (trigger addresses such as XG System On, Store To Flash). Receive never requests `write` nodes; send never writes `read` nodes. |
| `midi.device.<name>` | map root | A device value that is fixed for the device type, e.g. `midi.device.modelId="0x13"`. The client can add or override values (`DeviceValues`). |
| `midi.unit`, `midi.maxDataSize`, `midi.messageInterval`, `midi.exchange` | map root | Override the envelope's message settings for this device (see below). A `midi.unit` override applies to the data messages only. |

Sizes come from the plan: a field's constant `byteLength`, else the fixed size of its data type, else its
`midi.stride`. A bit-packed group (`sys.bitpacker`) occupies its `byteLength` (or its bits rounded up to bytes);
its fields share the group's addresses. A repeat needs a `midi.repeat` or a constant `count`.

### Address notation

`midi.address` and `midi.stride` are written as in the manual:

1. All `-` and `_` are removed; they only make the value readable: `0x40-00`, `0x03_00_10_00`. Spaces are not used, because a space-separated value reads as a list in XML.
2. With a `0x` prefix or an `H` postfix the value is hex, otherwise decimal: `0x4000`, `4000H`, `64`.
3. The number is the **wire form**: one byte per address byte. `0x40-00` is the address bytes `40 00`. It is converted to a linear address in the 7-bit digit space (`40 00` → 0x2000). Each byte must be below 2^addressBits (0x80 by default).

Values below 0x80 are the same in wire form and linear, so small sizes can be written in decimal (`50`). Larger
values are best written in hex with separators: decimal `256` is the wire bytes `01 00`, which is 128 addresses.

### Envelope schema (vendor messages)

| Property | On | Meaning |
|----------|----|---------|
| `midi.message` | a message group | `request` (RQ1, Parameter Request, Dump Request), `data` (DT1, Parameter Change, Bulk Dump), or a reply of the device: `ack`, `nak`, `error` (e.g. Korg DATA LOAD COMPLETED / ERROR, FORMAT ERROR). A reply has no address, size or data. |
| `midi.role` | envelope field or open choice | Which value the node holds: `address`, `size`, `device.<name>` or `data` (see below). |
| `midi.unit` | a message group | What one message covers. Only needed on a message without a `size` role, where the target decides the size: `field` (exactly one field: Yamaha Parameter Request/Change) or `atomic` (exactly one atomic group: Yamaha Dump Request/Bulk Dump). The default, `range`, is any contiguous range of addresses (Roland RQ1/DT1). |
| `midi.addressBits` | the `address` field | Bits per address byte; default 7. The number of address bytes is the field's `byteLength`. |
| `midi.maxDataSize` | a message group | The largest number of data bytes in one message; default no limit. |
| `midi.messageInterval` | a message group | Milliseconds to wait after sending this message (Roland advises at least 20 ms after a DT1). |
| `midi.exchange` | envelope root | The exchange protocol: `oneWay` or `acknowledged` (see the [message exchange](../Exchange/readme.md)). Optional: without it, an envelope with `ack`/`nak` replies is `acknowledged`, otherwise `oneWay`. |

| `midi.role` | Value on write | On read |
|-------------|----------------|---------|
| `address` | the transferred block's absolute start address | the address of the data |
| `size` | the transferred block's size | — (request only) |
| `device.<name>` | the client's `DeviceValues`, else the map root's `midi.device.<name>` | must match, otherwise the message is for another device or map and is ignored |
| `data` | the values of the block (open choice with `memberNodeDef`) | the values of the block |

Everything else is already in the schema: constants (`value`) are written by the engine and a mismatch on read means
"not this message"; a byte count (`byteSize` ref) is derived; checksum and framing are processors.

**Finding the messages of a map.** The *data* messages are the groups tagged `midi.message="data"` that contain an
open choice with `midi.role="data"` whose `memberNodeDef` is the map root's `nodeDef`. The *requests* and *replies*
are the groups tagged `request`, `ack`, `nak` or `error` in the same envelope root as a matching data message.

## Examples

The schemas in the repository:

- [TestDevice.cs](../../Jacobi.Midi.Tests/Addressing/TestDevice.cs): a complete envelope and device map without vendor schemas (the test schemas): 2 address bytes, a `sys.crc` (CRC-7) checksum, an atomic patch nodeDef, a non-atomic group and a repeat with reserved addresses.
- [Roland.xml](../../Jacobi.Midi.Roland/Schemas/Roland.xml): the Roland Type IV envelope (RQ1, DT1). `Message16`, `Message24` and `Message32` are the same messages with 2, 3 and 4 address bytes, each with its own open choice contract (`messageBody16`, `messageBody24`, `messageBody32`); a device map conforms to one address size only.
- [Gp8.xml](../../Jacobi.Midi.Roland/Schemas/Gp8.xml): a device map with 2 address bytes; a temporary patch and a repeat of 128 memory patches with a stride larger than the patch.
- [Gx700.xml](../../Jacobi.Midi.Roland/Schemas/Gx700.xml): a device map with 4 address bytes; atomic sections inside a patch, the same sections without `midi.atomic` in the individual (per parameter) temporary area, a write-only trigger address (Sound Change Request) and a read-only area (display).

A minimal stand-alone example (a made-up device, not in the repository). The envelope has a request and a data
message with a 2 byte address; the checksum and framing processors are left out:

```xml
<!-- envelope schema 'Acme' -->
<nodeDefs>
  <group name="body"/>
  <field name="address" datatype="sys.uint16" byteLength="2">
    <processor name="midi.sysexbyte"/>
  </field>
</nodeDefs>
<members>
  <group name="Messages">
    <members>
      <group name="Request" midi.message="request">
        <members>
          <field name="Command" datatype="sys.uint8" value="0x01"/>
          <field name="Model" datatype="sys.uint8" midi.role="device.model"/>
          <field name="Address" nodeDef="address" midi.role="address"/>
          <field name="Size" nodeDef="address" midi.role="size"/>
        </members>
      </group>
      <group name="Data" midi.message="data" midi.messageInterval="10">
        <members>
          <field name="Command" datatype="sys.uint8" value="0x02"/>
          <field name="Model" datatype="sys.uint8" midi.role="device.model"/>
          <field name="Address" nodeDef="address" midi.role="address"/>
          <choice name="Body" memberNodeDef="body" midi.role="data"/>
        </members>
      </group>
    </members>
  </group>
</members>
```

The device map conforms to the envelope's `body` and fixes the device value `model`. `Edit` starts at 0 and
`Global` follows it; the 16 programs start at wire address `10 00` (linear 0x0800), 0x20 addresses apart:

```xml
<!-- device schema, includes 'acme' -->
<nodeDefs>
  <group name="program" midi.atomic="true">
    <members>
      <field name="Level" datatype="sys.uint8"/>
      <field name="Name" datatype="sys.string" byteLength="8">
        <processor name="midi.string"/>
      </field>
    </members>
  </group>
</nodeDefs>
<members>
  <group name="AddressMap" nodeDef="acme.body" midi.device.model="0x2A">
    <members>
      <group name="Edit" nodeDef="program" midi.stride="0x00-10"/>
      <group name="Global" midi.access="read">
        <members>
          <field name="Version" datatype="sys.uint8"/>
        </members>
      </group>
      <repeat name="Programs" midi.address="0x10-00" midi.stride="0x00-20" midi.repeat="16">
        <members>
          <group name="Program" nodeDef="program"/>
        </members>
      </repeat>
    </members>
  </group>
</members>
```

Yamaha envelope and map (CP88 family, a sketch, not yet in the repository; see
[yamaha.md](../../../docs/analysis/yamaha.md)). The message type is the high nibble of the device number byte, a
constant in a bit-packed group:

```xml
<group name="ParameterRequest" midi.message="request" midi.unit="field">
  <members>
    <field name="YamahaId" datatype="sys.uint8" value="0x43"/>
    <group name="TypeAndDevice" byteLength="1">
      <processors><processor name="sys.bitpacker"/></processors>
      <members>
        <field name="DeviceNumber" datatype="sys.uint8" bits="4" midi.role="device.deviceNumber"/>
        <field name="Type" datatype="sys.uint8" bits="4" value="3"/>
      </members>
    </group>
    <field name="GroupId" datatype="sys.uint16" byteLength="2" midi.role="device.groupId"/>
    <field name="ModelId" datatype="sys.uint8" midi.role="device.modelId"/>
    <field name="Address" nodeDef="sysex21" midi.role="address"/>
  </members>
</group>
<group name="ParameterChange" midi.message="data" midi.unit="field"> ... Type = 1, Address, Data (role data) </group>
<group name="DumpRequest" midi.message="request" midi.unit="atomic"> ... Type = 2, Address </group>
<group name="BulkDump" midi.message="data" midi.unit="atomic" midi.maxDataSize="512" midi.messageInterval="120">
  ... Type = 0, ByteCount (byteSize ref of the data), Address, Data (role data), checksum processor ...
</group>

<group name="AddressMap" nodeDef="yamaha.messageBody"
       midi.device.groupId="0x7F1C" midi.device.modelId="0x08" midi.address="0x00-00-00">
  <members>
    <group name="StoreToFlash" midi.address="0x0D-00-00" midi.access="write"> ... </group>
    <group name="SystemCommon" midi.atomic="true" midi.address="0x20-00-00"> ... </group>
    <group name="MasterEq" midi.atomic="true" midi.address="0x20-40-00"> ... </group>
    <group name="Common" midi.atomic="true" midi.address="0x46-00-00"> ... </group>
    <repeat name="Zone" midi.address="0x4A-00-00" midi.stride="0x00-01-00" midi.repeat="4">
      <members>
        <group name="Zone" nodeDef="zone" midi.atomic="true" />
      </members>
    </repeat>
  </members>
</group>
```

## How the data is read and written

**Node ranges and addresses.** Addresses only increase in plan order, so a `PlanRange` covers one ascending address
span. A range is **widened** to whole addresses (asking for one bit field of a bit-packed group also delivers the
other fields of that byte) and, for a transfer, to the **atomic groups** it overlaps. It is then split into the blocks
to transfer with a message: per field (`field`), per atomic group (`atomic`), or in contiguous parts split at
unmapped gaps and at the message's maximum data size, never inside a field or an atomic group (`range`). Nodes the
access excludes (write-only on receive, read-only on send) are left out.

**The data is the device map, in one serializer call.** There is no opaque data buffer: the open choice of a data
message selects the device map as its body and narrows it to one group occurrence with `Target` and `TargetInstance`
(e.g. `AddressMap.Memory.Patch` with `[1]` for the DT1 at `40 40`).

- **Which body.** The groups that conform to the open choice's `memberNodeDef` are filtered on the `device.*` values read from the envelope (against their `midi.device.*`); exactly one must remain, otherwise the message is not for this map.
- **Which group (reading).** The length of the data is only known from the message as a whole, not when the open choice is entered. The map tries the group occurrences that start at the address, largest first, with a sink that discards the values: a group that is too large runs out of data, one that is too small does not end at the checksum. The group whose read consumes the whole message is the one; the client's sink only gets that final read (`ReceivedData.Decode`).
- **Ending the repeats.** The repeats of a map have no `count` (a dump may hold fewer items), so the value model ends them (`IOpenRepeatSink`/`IOpenRepeatSource` of the BinarySerializer). The map wraps the client's scope for the body (`AddressMap.WrapBody`): a repeat ends after the item that holds the target group (or the last node of a range), after its `midi.repeat` items, or at the end of the data. A client that reads or writes a map body with its own open choice model must wrap its scope the same way.
- **Tools with their own value model.** `AddressMapAdapter` lets a client that reads and writes arbitrary messages into its own value model (e.g. the Schema Editor's instantiate, match and workbench) work with the maps. Without address maps in the schemas its reads are plain serializer reads.
  - *Item counts:* `ItemCount` gives the number of items of a map repeat in the address space, so the client can show them all.
  - *Reading:* a read with a map root plan (a dump of the map) ends the repeats after `midi.repeat` items or at the end of the data. When the client selects a map root as the body of an open choice, the adapter narrows the body to the group occurrence the message carries, recognised as `TryReadData` does (any device id for now). When the message is not recognised, the largest group at the address is read as far as the data goes, with a warning.
  - *Roles:* `DescribeRole` tells how an envelope field with a role gets its value when a message carries the map (calculated, fixed by the map root), or null when the client supplies it.
  - *Writing:* the client selects map nodes (`MapOccurrence`: a group, a repeat or one repeat item, by path and instance). `PlanData` gives the blocks and addresses, `WriteData` the messages: split as the message exchange does (one data message, its unit and maximum data size, widened to atomic groups), each with the address and size of its block. The client's value model supplies the whole message: the device values the map root does not fix come from the envelope, the data from the body.
- **Writing.** A data message carries a block that is exactly one group occurrence (with the GP-8 every transfer is a whole, atomic patch).
- **Replies** (`ack`, `nak`, `error`) are recognised by their constants and device values alone.

## Validation

The map and its envelopes are validated when the map is first used (or prepared, see the
[message exchange](../Exchange/readme.md)). All problems are reported together in an `AddressMapException`, each
an `AddressMapProblem` located at its node (the document and the node path from a root, node defs expanded):

- **Layout**: addresses in plan order without overlap, no `midi.address` inside a repeat item, constant field sizes, repeat counts (`midi.repeat` or a constant `count`), `midi.repeat` only on repeats, `midi.stride` not smaller than the content, valid address notation and access values, atomic groups not nested, the map within the address space. Choices and virtual fields (`byteOffset`) are not supported in a map.
- **Envelope**: at least one data message carries the map; requests and data messages have an `address` field with a constant `byteLength` and agree on the address space; a `size` role only on a `range` request, which needs one; a `data` role only on data messages; replies have no address, size or data; the envelopes agree on `midi.exchange`.
- **Device values**: the device values the messages need (`deviceId` for the GP-8) are checked when an exchange first uses the map; they come from the map root or the client.

Tools that show problems while a schema is written (the Schema Editor) use `AddressMapValidator.Validate`: it checks
all tags of all schemas of a serializer, also those that no map uses, and reports errors, warnings and information:

- **Tags on their own** (`AddressMapTags.Validate`, also used per property): a known `midi.*` name and a valid value.
- **Roles in context**: a role is used inside a message (`midi.message`, a group inside an envelope root, not nested); `data` only on an open choice, the other roles only on fields.
- **Fulfilment**: a data message without a map root that conforms to its contract (information); a `device.<name>` role the map root does not fix, so the client supplies it (information); a `midi.device.<name>` on a map root that no message uses (warning).
- **Every map** with its envelopes, as above.

## Public types

Namespace `Jacobi.Midi.Device.Addressing`.

| Type | Purpose |
|------|---------|
| `AddressRange` | A contiguous range of linear addresses (one address is one encoded data byte). |
| `AddressSpace` | How an address is encoded on the wire (byte count × bits per byte); converts between wire form and linear addresses and parses/formats the notation. |
| `AddressBlock` | A unit of transfer: an address range plus the node range (`PlanRange`) whose data it holds. |
| `AddressMatch` | The result of resolving an address range: the block of whole fields it covers, whether it is aligned to field boundaries, and the unmapped addresses. |
| `AddressMessage`, `AddressMessageKind`, `AddressUnit` | A tagged message of an envelope with its kind (request, data, ack, nak, error), unit, maximum data size, interval and role nodes. |
| `AddressAccess` | The access of an addressed node (`midi.access`). |
| `DeviceValues` | The device values by name (`deviceId`, `modelId`, ...): those of the map root plus those of the client. |
| `ReceivedData` | A recognised data message: its address, device values and block; `Decode` reads its values into a value model. |
| `AddressMapException`, `AddressMapProblem`, `AddressMapProblemSeverity` | All problems found in a map and its envelopes, each located at its schema node. |
| `AddressMapValidator`, `AddressMapTags` | Validate the use of the tags in all schemas, and the tags on their own (names, descriptions, values, the role of a node), for tools such as the Schema Editor. |
| `AddressMapAdapter`, `MapOccurrence`, `DataPlan`, `EncodedDataMessage` | Reads and writes any message with the client's value model and lets the address maps in the schemas give the item counts, place the data of a message and calculate the addresses of the data messages for a selection of map nodes (for tools such as the Schema Editor). |

## Limitations

- A data message carries one group occurrence: a block of a few fields of a group, or one field whose group has more fields, cannot be sent yet (`NotSupportedException`). This matters for devices that take one parameter per message (Yamaha Parameter Change).
- Data that starts or ends inside a field (an unaligned or fragmented message) is not decoded.
- `range` requests are split at unmapped gaps; there is no option to bridge them.
- The Yamaha Bulk Header/Footer indirection (stored Live Set Sounds transferred through the edit buffer addresses) is not supported.
- Several address maps over the same parameters (e.g. a device that also accepts XG and GS) are modelled as separate maps (copies).
