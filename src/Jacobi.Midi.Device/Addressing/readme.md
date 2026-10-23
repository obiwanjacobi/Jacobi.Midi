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
untouched. The values of `midi.role` are the names of the properties they map to (without `midi.`), so a role is
resolved with a single lookup: role `R` gets its value from `midi.R` in the map or device context.

### Device schema (address map)

| Property | On | Meaning |
|----------|----|---------|
| `midi.addressStart` | the map root and any node below it that is not inside a repeat | The **absolute** start address of the node. Optional: the map root starts at address 0 without it; a node below the root without it follows its previous sibling (previous start + previous size); the first member starts where its parent starts. Not allowed inside a repeat item (all items would get the same address). |
| `midi.addressSize` | any addressed node | The number of addresses the node occupies. On a **repeat** it is the size of one item, which is also the **stride** between items. Optional: the default is the computed size (the encoded byte count, one address per data byte). It must be ≥ the computed size; the addresses after the content are reserved. This is how gaps inside repeated or reused blocks are expressed. |
| `midi.atomic` | group, repeat item, map root | `true`: the group is **indivisible**; it is always received and sent as a whole (a GP-8 patch, Yamaha dump blocks; a device that only does complete dumps has an atomic root). |
| `midi.access` | any addressed node, inherited | `readwrite` (default), `read` (e.g. Yamaha System Information) or `write` (trigger addresses such as XG System On, Store To Flash). Receive never requests `write` nodes; send never writes `read` nodes. |
| `midi.device.<name>` | map root | A device value that is fixed for the device type, e.g. `midi.device.modelId="0x13"`. The client can add or override values (`DeviceValues`). |
| `midi.unit`, `midi.maxDataSize`, `midi.messageInterval`, `midi.exchange` | map root | Override the envelope's message settings for this device (see below). A `midi.unit` override applies to the data messages only. |

Sizes come from the plan: a field's constant `byteLength`, else the fixed size of its data type, else its
`midi.addressSize`. A bit-packed group (`sys.bitpacker`) occupies its `byteLength` (or its bits rounded up to bytes);
its fields share the group's addresses. A repeat needs a constant `count`.

### Address notation

`midi.addressStart` and `midi.addressSize` are written as in the manual:

1. All `-` and `_` are removed; they only make the value readable: `0x40-00`, `0x03_00_10_00`. Spaces are not used, because a space-separated value reads as a list in XML.
2. With a `0x` prefix or an `H` postfix the value is hex, otherwise decimal: `0x4000`, `4000H`, `64`.
3. The number is the **wire form**: one byte per address byte. `0x40-00` is the address bytes `40 00`. It is converted to a linear address in the 7-bit digit space (`40 00` → 0x2000). Each byte must be below 2^addressBits (0x80 by default).

Values below 0x80 are the same in wire form and linear, so small sizes can be written in decimal (`50`). Larger
values are best written in hex with separators: decimal `256` is the wire bytes `01 00`, which is 128 addresses.

### Envelope schema (vendor messages)

| Property | On | Meaning |
|----------|----|---------|
| `midi.message` | a message group | `request` (RQ1, Parameter Request, Dump Request), `data` (DT1, Parameter Change, Bulk Dump), or a reply of the device: `ack`, `nak`, `error` (e.g. Korg DATA LOAD COMPLETED / ERROR, FORMAT ERROR). A reply has no address, size or data. |
| `midi.role` | envelope field or open choice | Which value the node holds: `addressStart`, `addressSize`, `device.<name>` or `data` (see below). |
| `midi.unit` | a message group | What one message covers. Only needed on a message without an `addressSize` role, where the target decides the size: `field` (exactly one field: Yamaha Parameter Request/Change) or `atomic` (exactly one atomic group: Yamaha Dump Request/Bulk Dump). The default, `range`, is any contiguous range of addresses (Roland RQ1/DT1). |
| `midi.addressBits` | the `addressStart` field | Bits per address byte; default 7. The number of address bytes is the field's `byteLength`. |
| `midi.maxDataSize` | a message group | The largest number of data bytes in one message; default no limit. |
| `midi.messageInterval` | a message group | Milliseconds to wait after sending this message (Roland advises at least 20 ms after a DT1). |
| `midi.exchange` | envelope root | The exchange protocol: `oneWay` or `acknowledged` (see the [message exchange](../Exchange/readme.md)). Optional: without it, an envelope with `ack`/`nak` replies is `acknowledged`, otherwise `oneWay`. |

| `midi.role` | Value on write | On read |
|-------------|----------------|---------|
| `addressStart` | the transferred block's absolute start address | the address of the data |
| `addressSize` | the transferred block's size | — (request only) |
| `device.<name>` | the client's `DeviceValues`, else the map root's `midi.device.<name>` | must match, otherwise the message is for another device or map and is ignored |
| `data` | the values of the block (open choice with `memberNodeDef`) | the values of the block |

Everything else is already in the schema: constants (`value`) are written by the engine and a mismatch on read means
"not this message"; a byte count (`byteSize` ref) is derived; checksum and framing are processors.

**Finding the messages of a map.** The *data* messages are the groups tagged `midi.message="data"` that contain an
open choice with `midi.role="data"` whose `memberNodeDef` is the map root's `nodeDef`. The *requests* and *replies*
are the groups tagged `request`, `ack`, `nak` or `error` in the same envelope root as a matching data message.

## Examples

A complete, stand-alone envelope and device map (no vendor schemas) are the test schemas in
[TestDevice.cs](../../Jacobi.Midi.Tests/Addressing/TestDevice.cs): 2 address bytes, a `sys.crc` (CRC-7) checksum, an
atomic patch nodeDef, a non-atomic group and a repeat with reserved addresses.

Roland Type IV envelope ([Roland.xml](../../Jacobi.Midi.Roland/Schemas/Roland.xml)), the messages with a 2 byte address.
`Message24` and `Message32` are the same messages with 3 and 4 address bytes (`sysex24`/`messageBody24`,
`sysex32`/`messageBody32`); a device map conforms to one address size only:

```xml
<group name="Message16">
  <processors>
    <processor name="midi.sysex"/>
  </processors>
  <members>
    <group name="RQ1" midi.message="request">
      <members>
        <field name="RolandId" datatype="sys.uint8" value="0x41"/>
        <field name="DeviceId" datatype="sys.uint8" midi.role="device.deviceId"/>
        <field name="ModelId" datatype="sys.uint8" midi.role="device.modelId"/>
        <field name="Command" datatype="sys.uint8" value="0x11"/>
        <group name="ChecksumScope">
          <processors>
            <processor name="roland.checksum"/>
          </processors>
          <members>
            <field name="Address" nodeDef="sysex16" midi.role="addressStart"/>
            <field name="Size" nodeDef="sysex16" midi.role="addressSize"/>
          </members>
        </group>
      </members>
    </group>
    <group name="DT1" midi.message="data" midi.messageInterval="20">
      <members>
        <field name="RolandId" datatype="sys.uint8" value="0x41"/>
        <field name="DeviceId" datatype="sys.uint8" midi.role="device.deviceId"/>
        <field name="ModelId" datatype="sys.uint8" midi.role="device.modelId"/>
        <field name="Command" datatype="sys.uint8" value="0x12"/>
        <group name="ChecksumScope">
          <processors>
            <processor name="roland.checksum"/>
          </processors>
          <members>
            <field name="Address" nodeDef="sysex16" midi.role="addressStart"/>
            <choice name="Data" memberNodeDef="messageBody16" midi.role="data"/>
          </members>
        </group>
      </members>
    </group>
  </members>
</group>
```

GP-8 device map ([Gp8.xml](../../Jacobi.Midi.Roland/Schemas/Gp8.xml)); the patch layout is an atomic group nodeDef
`patch` of 0x32 addresses without addresses of its own:

```xml
<group name="AddressMap" nodeDef="roland.messageBody16" midi.device.modelId="0x13">
  <members>
    <group name="Temporary" nodeDef="patch" midi.addressSize="0x00-40" />
    <repeat name="Memory" count="128" midi.addressStart="0x40-00" midi.addressSize="0x00-40">
      <members>
        <group name="Patch" nodeDef="patch" />
      </members>
    </repeat>
  </members>
</group>
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
    <field name="Address" nodeDef="sysex21" midi.role="addressStart"/>
  </members>
</group>
<group name="ParameterChange" midi.message="data" midi.unit="field"> ... Type = 1, Address, Data (role data) </group>
<group name="DumpRequest" midi.message="request" midi.unit="atomic"> ... Type = 2, Address </group>
<group name="BulkDump" midi.message="data" midi.unit="atomic" midi.maxDataSize="512" midi.messageInterval="120">
  ... Type = 0, ByteCount (byteSize ref of the data), Address, Data (role data), checksum processor ...
</group>

<group name="AddressMap" nodeDef="yamaha.messageBody"
       midi.device.groupId="0x7F1C" midi.device.modelId="0x08" midi.addressStart="0x00-00-00">
  <members>
    <group name="StoreToFlash" midi.addressStart="0x0D-00-00" midi.access="write"> ... </group>
    <group name="SystemCommon" midi.atomic="true" midi.addressStart="0x20-00-00"> ... </group>
    <group name="MasterEq" midi.atomic="true" midi.addressStart="0x20-40-00"> ... </group>
    <group name="Common" midi.atomic="true" midi.addressStart="0x46-00-00"> ... </group>
    <repeat name="Zone" count="4" midi.addressStart="0x4A-00-00" midi.addressSize="0x00-01-00">
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
- **Writing.** A data message carries a block that is exactly one group occurrence (with the GP-8 every transfer is a whole, atomic patch).
- **Replies** (`ack`, `nak`, `error`) are recognised by their constants and device values alone.

## Validation

The map and its envelopes are validated when the map is first used (or prepared, see the
[message exchange](../Exchange/readme.md)). All problems are reported together in an `AddressMapException`, each
with its node path:

- **Layout**: addresses in plan order without overlap, no `midi.addressStart` inside a repeat item, constant field sizes and repeat counts, `midi.addressSize` not smaller than the content, valid address notation and access values, atomic groups not nested, the map within the address space. Choices and virtual fields (`byteOffset`) are not supported in a map.
- **Envelope**: at least one data message carries the map; requests and data messages have an `addressStart` field with a constant `byteLength` and agree on the address space; an `addressSize` role only on a `range` request, which needs one; a `data` role only on data messages; replies have no address, size or data; the envelopes agree on `midi.exchange`.
- **Device values**: the device values the messages need (`deviceId` for the GP-8) are checked when an exchange first uses the map; they come from the map root or the client.

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
| `AddressMapException` | All problems found in a map and its envelopes. |

## Limitations

- A data message carries one group occurrence: a block of a few fields of a group, or one field whose group has more fields, cannot be sent yet (`NotSupportedException`). This matters for devices that take one parameter per message (Yamaha Parameter Change).
- Data that starts or ends inside a field (an unaligned or fragmented message) is not decoded.
- `range` requests are split at unmapped gaps; there is no option to bridge them.
- The Yamaha Bulk Header/Footer indirection (stored Live Set Sounds transferred through the edit buffer addresses) is not supported.
- Several address maps over the same parameters (e.g. a device that also accepts XG and GS) are modelled as separate maps (copies).
