# Address Map Proposal

Status: implemented for the Roland GP-8 (steps 1-5 of section 8) in `Jacobi.Midi.Device` (`Addressing`, `Exchange`).
Section 9 lists where the implementation differs from the design below.

An address map lets a device schema say where each setting lives in the device's address space. The client works
with **schema nodes only**: it says, as a range of the execution plan (`PlanRange`), which data it wants to receive
or send. The mechanism works out which requests to send (Roland RQ1, Yamaha Parameter/Dump Request) and how to
interpret the data messages that come back (Roland DT1, Yamaha Parameter Change/Bulk Dump).

The mechanism is driven by **schema definitions only**. Everything vendor specific is in the schemas: the message
layout, framing and checksum (already there today), plus `midi.*` tags that say what the nodes *mean*. There is no
vendor code (no Roland or Yamaha protocol class).

Principles:

- The API assumes nothing but the `PlanRange` it gets to receive or send, plus the value model for it. Every transfer, including listening for data the device sends on its own, starts with a range and ends when the range is exhausted (for a send: when there are no more logical fields to add to the stream).
- Before an exchange starts, the device is known (its device values, e.g. the Roland device id) and so is how to reach it (the MIDI session and endpoint). The exchange opens its own connection.
- The device map and its envelopes are linked by the open choice contract (`memberNodeDef`), as the BinarySerializer requires. Which envelope message is used for a range is decided with the device values (e.g. `modelId`).
- The open choice restriction stays: `Target` only addresses a group. A data message carries one group occurrence of the map, selected with `Target` and `TargetInstance` (see 5).
- One property has one meaning: `midi.addressStart` is always absolute.

## 1. Schema properties

All tags are free-form node properties in the `midi` namespace. The compiler leaves node properties untouched, so
there is no BinarySerializer change.

The values of `midi.role` are the names of the properties they map to (without `midi.`), so a role is resolved with a
single lookup: role `R` gets its value from `midi.R` in the map/device context.

### 1.1 Device schema (address map)

| Property | On | Meaning |
|----------|----|---------|
| `midi.addressStart` | the map root and any node below it that is not inside a repeat | The **absolute** start address of the node. Optional below the root: a node without it follows its previous sibling (previous start + previous size); the first member starts where its parent starts. Not allowed inside a repeat item (all items would get the same address). A nodeDef with an `addressStart` that is used twice gives overlapping addresses, which validation reports. |
| `midi.addressSize` | any addressed node | The number of addresses the node occupies. On a **repeat** it is the size of one item, which is also the **stride** between items. Optional: the default is the computed size (the encoded byte count, one address per data byte). It must be ≥ the computed size; the addresses after the content are reserved. This is how gaps inside repeated or reused blocks are expressed. |
| `midi.atomic` | group, repeat item, map root | `true`: the group is **indivisible**; it is always received and sent as a whole (Yamaha dump blocks; a device that only does complete dumps has an atomic root). |
| `midi.access` | any addressed node, inherited | `readwrite` (default), `read` (e.g. Yamaha System Information) or `write` (trigger addresses such as XG System On, Store To Flash). Receive never requests `write` nodes; send never writes `read` nodes. |
| `midi.device.<name>` | map root | A device value that is fixed for the device type, e.g. `midi.device.modelId="0x13"`. The client can add or override values (`DeviceValues`). |
| `midi.unit`, `midi.maxDataSize`, `midi.messageInterval` | map root | Override the envelope's message settings for this device (see 1.2), e.g. `midi.unit="field"` for the GP-8, which accepts one parameter per DT1. |

The map root conforms to the envelope contract with `nodeDef` (e.g. `nodeDef="roland.messageBody"`), as any open
choice body does.

**Address notation** (`midi.addressStart`, `midi.addressSize`):

1. Remove all `-` and `_` (they only make the value readable: `0x40-00`, `0x03_00_10_00`). Spaces are not used, because a space-separated value reads as a list in XML.
2. With a `0x` prefix or an `H` postfix the value is hex, otherwise decimal: `0x4000`, `4000H`, `64`.
3. The number is the **wire form** as in the manual: one byte per address byte. `0x40-00` is the address bytes `40 00`. The map converts it to a linear address in the 7-bit digit space (`40 00` → 0x2000). Each byte must be below 2^addressBits (0x80 by default), otherwise validation reports it.

Values below 0x80 are the same in wire form and linear, so small sizes can be written in decimal (`50`). Larger
values are best written in hex with separators, as in the manual: decimal `256` is the wire bytes `01 00`, which is
128 addresses.

### 1.2 Envelope schema (vendor messages)

| Property | On | Meaning |
|----------|----|---------|
| `midi.message` | a message group | `request` (RQ1, Parameter Request, Dump Request) or `data` (DT1, Parameter Change, Bulk Dump). |
| `midi.role` | envelope field or open choice | Which value the node holds: `addressStart`, `addressSize`, `device.<name>` or `data` (see the table below). |
| `midi.unit` | a message group | What one message covers. Only needed on a message without an `addressSize` role, where the target decides the size: `field` (exactly one field: Yamaha Parameter Request/Change) or `atomic` (exactly one atomic group: Yamaha Dump Request/Bulk Dump). The default, `range`, is any contiguous range of addresses (Roland RQ1/DT1). |
| `midi.addressBits` | the `addressStart` field | Bits per address byte; default 7. The number of address bytes is the field's `byteLength`. |
| `midi.maxDataSize` | a message group | The largest number of data bytes in one message; default no limit. |
| `midi.messageInterval` | a message group | Milliseconds to wait after sending this message (Roland advises at least 20 ms after a DT1). |

| `midi.role` | Value on write | On read |
|-------------|----------------|---------|
| `addressStart` | the transferred block's absolute start address | the address of the data |
| `addressSize` | the transferred block's size | — (request only) |
| `device.<name>` | the client's `DeviceValues`, else the map root's `midi.device.<name>` | must match, otherwise the message is for another device or map and is ignored |
| `data` | the data bytes of the block (open choice with `memberNodeDef`) | the data bytes |

Everything else is already in the schema: constants (`value`) are written by the engine and a mismatch on read means
"not this message"; a byte count (`byteSize` ref) is derived; checksum and framing are processors.

**Finding the messages of a map.** The *data* messages are the groups tagged `midi.message="data"` that contain an
open choice with `midi.role="data"` whose `memberNodeDef` is the map root's `nodeDef`. The *request* messages are the
groups tagged `midi.message="request"` in the same envelope root as a matching data message. A request carries no
data, so it has no open choice.

### 1.3 Examples

Roland Type IV envelope (`RolandType4.xml`) with tags:

```xml
<group name="Message">
  <processors>
    <processor name="midi.sysex"/>
    <processor name="sys.bytepacker" byteorder="big"/>
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
            <choice name="Data" memberNodeDef="messageBody" midi.role="data"/>
          </members>
        </group>
      </members>
    </group>
  </members>
</group>
```

GP-8 device map (the patch layout is a group nodeDef `patch` of 0x32 addresses, without addresses of its own):

```xml
<group name="AddressMap" nodeDef="roland.messageBody" midi.device.modelId="0x13"
       midi.addressStart="0x00-00" midi.unit="field">
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

Yamaha envelope (CP88 family): four messages. The message type is the high nibble of the device number byte, a
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
```

Yamaha device map (CP88, sketch) with atomic dump blocks and access; all addresses absolute:

```xml
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

### 1.4 Validation

Done on the `ExecutionPlan`s (map and envelopes) when the `AddressMap` is created. All problems are reported together,
each with its node path. Details follow during implementation; at least:

- No overlapping addresses, constant sizes and counts, sizes large enough, addresses in **plan order** (so a `PlanRange` maps to one ascending address span), addresses within the address space, no `midi.addressStart` inside a repeat item.
- At least one data message carries the map; each message has an `addressStart` role; an `addressSize` role only on `range` requests; a `data` role only on data messages.
- An `atomic` unit requires atomic groups in the map; atomic groups are not nested in each other.
- Every `device.<name>` role has a value: from the map root, or it is reported as required from the client.

## 2. Layers

```text
client ── PlanRange + IValueSink/IValueSource ──► MessageExchange       (Jacobi.Midi.Device)
                                                     │ uses
                                                     ├─ AddressMap       nodes ⇄ addresses, atomic groups, encode/decode data bytes
                                                     ├─ AddressProtocol  envelope messages ⇄ (address, size, data), from the envelope tags
                                                     └─ ISysExPort       (internal) complete SysEx messages over the exchange's own connection
```

All in `Jacobi.Midi.Device`, all generic.

- **`AddressMap`** knows nothing about the wire. It turns a `PlanRange` into address blocks (widened to atomic groups), an address range back into a `PlanRange`, and encodes/decodes the **data bytes** of a block with the serializer.
- **`AddressProtocol`** is the set of tagged messages that can carry the map's contract. It fills them (address, size, device values, data bytes) and recognises incoming data messages.
- **`MessageExchange`** connects the two over its own MIDI connection. It picks the messages for a range, sends them, collects the replies per block (a device may split one reply over several messages), decodes each completed block, and does the read-modify-write for atomic groups.
- **`MessageExchangeFactory`** creates exchanges from a MIDI session: it opens a private connection per exchange with the exchange's receive handler.

## 3. Client usage

```csharp
// once per application: exchanges open their own connections on the session; the address maps are deduced
// from the serializer's schemas when a range is used (and cached per plan)
var factory = new MessageExchangeFactory(session, serializer);
factory.PrepareAll();   // optional: build and validate all address maps up front

// once per device instance: what device (values the schema does not fix) and how to reach it
var device = new DeviceValues { ["deviceId"] = 0x00 };
await using var exchange = await factory.Create(endpointId, device, options: null, ct);

// the client only works with plan ranges
var plan = serializer.GetPlan(new SchemaName("Gp8.AddressMap"));

// receive memory patch 6 (instance 5) into a value model
var item = new InstancePath([5]);
var patch = plan.CreateRange("AddressMap.Memory.Patch", item, "AddressMap.Memory.Patch", item);
var result = await exchange.Receive(patch, valueSink, ct);
// result.Blocks: per block whether it was completed or timed out

// send one setting: the transfer ends when the source has supplied the last field of the range
var rate = plan.CreateRange("AddressMap.Temporary.Phaser.Rate", "AddressMap.Temporary.Phaser.Rate");
await exchange.Send(rate, valueSource, ct);

// data the device sends on its own (front panel changes), for the temporary patch, until cancelled
var temporary = plan.CreateRange("AddressMap.Temporary");
await exchange.Listen(temporary, valueSink, ct);
```

The value model sees the same paths and instances as with a normal read or write of the map plan: the root, the groups
leading to the range (with their repeat items), then the fields. The range may be **widened**:

- to whole addresses: asking for one bit field of a bit-packed group also delivers the other fields of that byte;
- to whole atomic groups: on receive, the sink gets all fields of an atomic group the range overlaps.

## 4. Types

Namespace `Jacobi.Midi.Device.Addressing` (map and protocol) and `Jacobi.Midi.Device.Exchange` (exchange).

### 4.1 Addresses

```csharp
/// <summary>
/// A contiguous range of linear addresses (one address is one encoded data byte).
/// </summary>
public readonly record struct AddressRange(long Start, long Size)
{
    public long End => Start + Size;
    public bool Contains(long address);
    public bool Contains(AddressRange other);
    public bool Overlaps(AddressRange other);
}

/// <summary>
/// How an address is encoded on the wire: <see cref="ByteCount"/> bytes of <see cref="BitsPerByte"/> bits each
/// (GP-8: 2 x 7, most Roland devices: 4 x 7, Yamaha: 3 x 7). Converts between the wire form and linear addresses.
/// Taken from the envelope's <c>addressStart</c> field.
/// </summary>
public readonly record struct AddressSpace(int ByteCount, int BitsPerByte = 7)
{
    public long Capacity { get; }
    public long ToLinear(ReadOnlySpan<byte> wire);
    public void ToWire(long address, Span<byte> wire);
    /// <summary>
    /// Parses the schema notation into a linear address: removes '-' and '_', reads hex ("0x" prefix or "H" postfix)
    /// or decimal, and converts the wire form (one byte per address byte) to linear ("0x40-00" → 0x2000).
    /// </summary>
    public long Parse(string text);
    /// <summary>Formats a linear address in wire form, as in the manual ("0x40-00").</summary>
    public string Format(long address);
}
```

### 4.2 The map

```csharp
/// <summary>
/// The address layout of one address-map root: converts between schema node ranges (<see cref="PlanRange"/>) and
/// address ranges, knows the atomic groups, and encodes/decodes the data bytes of an address block.
/// Created from the root's <see cref="ExecutionPlan"/> and validated against it, together with the envelope messages
/// that can carry its contract. Immutable and thread-safe; create it once per device type.
/// </summary>
public sealed class AddressMap
{
    /// <summary>
    /// Builds and validates the map and finds its messages (open choices whose memberNodeDef is the root's nodeDef);
    /// throws an <see cref="AddressMapException"/> with all problems found.
    /// </summary>
    public static AddressMap Create(SchemaSet schemas, Serializer serializer, SchemaName root);

    public SchemaName Root { get; }
    public ExecutionPlan Plan { get; }
    /// <summary>The messages that can carry this map.</summary>
    public AddressProtocol Protocol { get; }
    /// <summary>The device values fixed by the map root (<c>midi.device.*</c>).</summary>
    public DeviceValues Device { get; }
    /// <summary>The addresses the map covers (from the root's start to its end).</summary>
    public AddressRange Range { get; }

    /// <summary>A range over one node occurrence (a group with all its fields, or one field).</summary>
    public PlanRange CreateRange(SchemaPath path, InstancePath instance = default);
    /// <summary>A range from one node occurrence up to and including another (in plan order).</summary>
    public PlanRange CreateRange(SchemaPath fromPath, InstancePath fromInstance, SchemaPath toPath, InstancePath toInstance);

    /// <summary>The addresses a node range occupies (widened to whole addresses, gaps included).</summary>
    public AddressRange GetAddressRange(PlanRange range);

    /// <summary>
    /// Widens a node range to the atomic groups it overlaps; returns the range unchanged when it overlaps none.
    /// </summary>
    public PlanRange WidenToAtomic(PlanRange range);

    /// <summary>
    /// Splits a node range into the blocks to transfer with the given message: per field, per atomic group, or in
    /// contiguous parts split at unmapped gaps and at the message's maximum data size, never inside a field or an
    /// atomic group. Nodes the access excludes (write-only on receive, read-only on send) are left out.
    /// </summary>
    public IReadOnlyList<AddressBlock> Split(PlanRange range, AddressMessage message);

    /// <summary>Which nodes an address range targets (e.g. the address and size of a received data message).</summary>
    public AddressMatch Resolve(AddressRange range);

    /// <summary>Writes the data bytes of a block from the value model.</summary>
    public void Encode(AddressBlock block, IValueSource source, IBufferWriter<byte> data);
    /// <summary>Reads the data bytes of a block into the value model; returns the number of bytes read.</summary>
    public long Decode(AddressBlock block, ReadOnlySequence<byte> data, IValueSink sink);
}

/// <summary>
/// A unit of transfer: an address range plus the node range whose data it holds.
/// </summary>
public sealed record AddressBlock(AddressRange Addresses, PlanRange Nodes);

/// <summary>
/// The result of resolving an address range against a map: the block of whole fields it covers, and whether the
/// range started or ended inside a field or touched unmapped addresses.
/// </summary>
public sealed class AddressMatch
{
    public AddressRange Requested { get; }
    /// <summary>The covered fields; null when nothing in the range is mapped.</summary>
    public AddressBlock? Block { get; }
    /// <summary>True when the range starts and ends on field boundaries, so <see cref="Block"/> can be decoded from the data.</summary>
    public bool IsAligned { get; }
    public IReadOnlyList<AddressRange> Unmapped { get; }
}
```

### 4.3 Protocol (from the envelope tags)

```csharp
/// <summary>
/// The addressed-data messages that can carry one open choice contract (memberNodeDef), found through their
/// <c>midi.*</c> tags: the requests and data messages, how to fill them and how to recognise them.
/// Generic: all vendor specifics are in the schemas. Shared by all maps that conform to the contract.
/// </summary>
public sealed class AddressProtocol
{
    /// <summary>The open choice contract (the nodeDef the map roots conform to).</summary>
    public SchemaName Contract { get; }
    public AddressSpace AddressSpace { get; }
    public IReadOnlyList<AddressMessage> Requests { get; }
    public IReadOnlyList<AddressMessage> DataMessages { get; }

    /// <summary>A request for the addresses, as a complete SysEx message.</summary>
    public byte[] CreateRequest(AddressMessage request, AddressRange addresses, DeviceValues device);
    /// <summary>
    /// Selects the body group (device map) for the device values read from an envelope: of the groups that conform
    /// to the contract, the one whose <c>midi.device.*</c> values all match. None or more than one is an error.
    /// </summary>
    public SchemaName SelectBody(DeviceValues values);
}

// on AddressMap (the data messages need the layout of the map):

/// <summary>
/// A data message with the values of the block, as a complete SysEx message. The block must be one group
/// occurrence of the map (e.g. a patch): the message body is written for that group.
/// </summary>
public byte[] CreateData(AddressMessage message, AddressBlock block, IValueSource source, DeviceValues device);

/// <summary>
/// Recognises a data message for this map and device: the constants and checksum match, the device values read
/// select this map and match the device, and the data is one group of the map.
/// </summary>
public bool TryReadData(ReadOnlyMemory<byte> sysEx, DeviceValues device, out ReceivedData data);

/// <summary>
/// A data message that was recognised for a map: its address, the device values in it and the block of the map it
/// carries. Decode reads its values into a value model.
/// </summary>
public sealed class ReceivedData
{
    public AddressMessage Message { get; }
    public long Address { get; }
    public DeviceValues Device { get; }
    public AddressBlock Block { get; }
    public long Decode(IValueSink sink);
}

/// <summary>
/// One tagged message group of an envelope (<c>midi.message</c>) with its settings and the nodes that have a role.
/// </summary>
public sealed class AddressMessage
{
    public string Name { get; }
    public AddressMessageKind Kind { get; }
    public AddressUnit Unit { get; }
    /// <summary>The largest number of data bytes in one message; 0 means no limit.</summary>
    public int MaxDataSize { get; }
    public TimeSpan Interval { get; }
    /// <summary>The range over the envelope plan that writes or reads this message.</summary>
    public PlanRange Range { get; }
    /// <summary>The envelope nodes by role (<c>addressStart</c>, <c>addressSize</c>, <c>device.modelId</c>, <c>data</c>, ...).</summary>
    public IReadOnlyDictionary<string, NodeInfo> Roles { get; }
}

/// <summary>
/// Whether a message asks for data or carries data.
/// </summary>
public enum AddressMessageKind
{
    Request,
    Data,
}

/// <summary>
/// What one message covers: any contiguous addresses, exactly one field, or exactly one atomic group.
/// </summary>
public enum AddressUnit
{
    Range,
    Field,
    Atomic,
}

/// <summary>
/// The device values (<c>device.&lt;name&gt;</c> roles) by name, e.g. <c>deviceId</c> or <c>modelId</c>: the values a
/// map root fixes for the device type, plus the values the client supplies for the device instance.
/// </summary>
public sealed class DeviceValues : IEnumerable<KeyValuePair<string, long>>
{
    public long this[string name] { get; set; }
    public bool TryGet(string name, out long value);
    /// <summary>These values with the other's values added or overriding them (client over map root).</summary>
    public DeviceValues With(DeviceValues other);
}

/// <summary>
/// The address and data bytes of a received data message.
/// </summary>
public readonly record struct AddressedData(long Address, ReadOnlyMemory<byte> Data)
{
    public AddressRange Addresses => new(Address, Data.Length);
}
```

### 4.4 Exchange

```csharp
/// <summary>
/// Creates message exchanges on a MIDI session. Each exchange gets its own connection to the endpoint, with the
/// exchange as the receive handler.
/// </summary>
public sealed class MessageExchangeFactory(IMidiSession session)
{
    public Task<MessageExchange> Create(AddressMap map, string endpointId, DeviceValues device,
        MessageExchangeOptions? options, CancellationToken ct);
}

/// <summary>
/// Transfers schema node ranges to and from one device over its own connection: sends the requests or data messages
/// for a range, decodes the data that comes back and does the read-modify-write for atomic groups.
/// Disposing it closes the connection.
/// </summary>
public sealed class MessageExchange : IAsyncDisposable
{
    public AddressMap Map { get; }

    /// <summary>
    /// Requests the data of the range (widened to atomic groups) and decodes each completed block into the sink.
    /// Blocks that are not answered within the timeout are reported in the result, not thrown.
    /// </summary>
    public Task<AddressTransferResult> Receive(PlanRange range, IValueSink sink, CancellationToken ct);

    /// <summary>
    /// Encodes the range from the source and sends it as data messages. An atomic group the range only partly covers
    /// is completed with the device's current data first and written back as a whole. The transfer ends when the
    /// source has supplied the last logical field of the range.
    /// </summary>
    public Task<AddressTransferResult> Send(PlanRange range, IValueSource source, CancellationToken ct);

    /// <summary>
    /// Decodes data messages the device sends on its own into the sink, for the nodes of the range, until cancelled.
    /// Sends nothing. Data outside the range, or not aligned to whole fields, is ignored.
    /// </summary>
    public Task Listen(PlanRange range, IValueSink sink, CancellationToken ct);
}

/// <summary>
/// Runtime settings of an exchange that do not belong in the schema.
/// </summary>
public sealed record MessageExchangeOptions
{
    /// <summary>The UMP group the device is on.</summary>
    public byte Group { get; init; }
    /// <summary>How long to wait for the data of a request.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// The outcome of a transfer per block.
/// </summary>
public sealed record AddressTransferResult(IReadOnlyList<AddressBlockResult> Blocks)
{
    public bool IsComplete { get; }
}

/// <summary>
/// The outcome of one block of a transfer.
/// </summary>
public sealed record AddressBlockResult(AddressBlock Block, AddressBlockStatus Status, Exception? Error = null);

/// <summary>
/// What happened to a block of a transfer.
/// </summary>
public enum AddressBlockStatus
{
    Completed,
    TimedOut,
    Failed,
}

/// <summary>
/// Sends and receives complete SysEx messages (F0 … F7). Internal: implemented over the exchange's own
/// <see cref="IMidiConnection"/>, and by a fake in tests.
/// </summary>
internal interface ISysExPort
{
    ValueTask Send(ReadOnlyMemory<byte> sysEx, CancellationToken ct);
    /// <summary>Raised for each complete SysEx message received; may be raised on a background thread.</summary>
    event SysExReceivedHandler? Received;
}

internal delegate void SysExReceivedHandler(ReadOnlyMemory<byte> sysEx);
```

The connection-based port splits outgoing messages with `SysEx7Assembler.FromSystemExclusive` and assembles incoming
ones with a `SysEx7Assembler` on the options' group. Its packet handler is the `MidiPacketReceivedHandler` the factory
passes to `IMidiSession.Connect`.

## 5. How a range is transferred

**Choosing the messages.** The device values select the messages: a data message is only used if its
`device.<name>` roles have values (for reading, the received values must match them). For each part of the range,
the exchange picks the message that needs the fewest messages: a `range` message where one exists, else `atomic`
messages for atomic groups, else `field` messages. A unit on the map root overrides the envelope's.
`map.Split(range, message)` then gives the blocks for that message.

**Receive:**

1. The range is widened to the atomic groups it overlaps (`map.WidenToAtomic`).
2. Per block, `protocol.CreateRequest(request, block, device)` is sent.
3. Received messages go through `protocol.TryReadData`. Data inside a pending block's addresses is copied into that block's buffer at its offset. A reply split over several messages (Yamaha: 512-byte packets), or split inside a field, is reassembled.
4. When a buffer is complete, `map.Decode(block, buffer, sink)` runs. That is `serializer.Deserialize(block.Nodes, …)` over the map plan. A timeout marks the block `TimedOut`.

**Send:**

1. For each atomic group the range only partly covers, the exchange first gets the group's current data (a receive of the group, or the data it already holds) into a buffer.
2. Per block, `map.Encode(block, source, buffer)` (`serializer.Serialize(block.Nodes, …)`) writes the source's fields into the buffer, over the current data for atomic groups.
3. `protocol.CreateData(message, block, buffer, device)` is sent, with the message interval between messages.
4. The transfer is finished when the last block of the range is sent, which is when the source has supplied the last logical field of the range.

**Listen:**

- Received data messages go through `protocol.TryReadData` and `map.Resolve(data.Addresses)`. The part that is aligned and inside the range is decoded into the sink. A reply to a pending `Receive` is not also delivered to a listener.

**The data is the device map, in one serializer call.** There is no opaque data buffer: the open choice of a data
message selects the device map as its body and narrows it to one group occurrence with `Target` and `TargetInstance`
(e.g. `AddressMap.Memory.Patch` with `[1]` for the DT1 at `40 40`). `TargetInstance` was added to
`OpenChoiceSinkResult`/`OpenChoiceSourceResult` in Jacobi.BinarySerializer for this.

- **Which body.** The groups that conform to the open choice's `memberNodeDef` are filtered on the `device.*` values read from the envelope (against their `midi.device.*`); exactly one must remain, otherwise the message is not for this map.
- **Which group (reading).** The length of the data is only known from the message as a whole, not when the open choice is entered. The map tries the group occurrences that start at the address, largest first, with a sink that discards the values: a group that is too large runs out of data, one that is too small does not end at the checksum. The group whose read consumes the whole message is the one; the client's sink only gets that final read.
- **Writing.** `CreateData` needs a block that is exactly one group occurrence. With the GP-8 every transfer is a whole (atomic) patch, so this holds.

Verified while implementing: `Deserialize(PlanRange, …)` / `Serialize(PlanRange, …)` over the map plan read and write
the range as a fresh message (a whole patch decodes from its 50 data bytes, and a DT1 written back is identical to the
dump).

## 6. Dump-only devices and future scenarios

### Dump-only devices (covered by atomic)

A device that can only exchange complete dumps, for both read and write, has an **atomic map root**. The mechanism
above then does the rest: every receive gets the whole dump, and every send completes the dump with the current data
and writes it back as a whole when the range is exhausted. Keeping the received dump in the exchange (an image of
the device state) avoids receiving it again for every range. That image is also where data for unaligned and
fragmented messages is collected. It is an optimisation of the same mechanism, not a separate scenario.

### Manual operation on the device (later)

Some devices only send a dump after a manual operation (e.g. pressing the bulk key). A receive then has to wait for
the user instead of timing out. Ideally the exchange asks the client to prompt the user and waits for the data or a
cancel. The details are for later.

### Yamaha Bulk Header/Footer indirection (not in scope now)

The CP88, CK88 and YC store Live Set Sounds without addresses of their own: a stored sound is transferred as Bulk
Header `0E pp 0n`, the blocks at the edit buffer addresses, then Bulk Footer `0F pp 0n` (see `yamaha.md`). This needs a
repeat whose items share the content addresses and are selected by a header address with its own strides. It needs its
own design.

### Bridging gaps (later, when needed)

`range` requests are split at unmapped gaps. If a device turns out to accept, or require, one request over a whole
area including reserved addresses, an option to bridge gaps can be added.

## 7. Decisions and open questions

Decided:

- The RQ1 `Data` open choice in `RolandType4.xml` was a copy-paste error; it has been removed.
- `midi.addressStart` is always absolute. Relative addresses, if ever needed, get their own property.
- Address notation: `-` and `_` are removed, then a `0x` prefix or `H` postfix means hex, otherwise decimal. The number is always the wire form (no spaces).
- No gap bridging for now.
- Unsolicited data is received with a range too (`Listen`); exchanges are created by a factory on the MIDI session, with their own connection.
- `midi.unit` is one tag for what one message carries (a range, one field or one atomic group), on envelope messages (Yamaha) and as a device override on the map root (GP-8). The name may change later.

No open questions left for the start of the implementation.

## 8. Implementation order

- [x] 1. `AddressRange`, `AddressSpace` (parse/format the notation) with unit tests.
- [x] 2. `AddressMap` over the GP-8 map: address layout from the plan (`midi.addressStart`/`midi.addressSize`, repeats, bit-packed groups), validation, `CreateRange`, `GetAddressRange`, `Resolve`, `WidenToAtomic`, `Split`.
- [x] 3. The BinarySerializer checks of section 5, then `Encode`/`Decode` with `PlanRange` over the map plan.
- [x] 4. Tag `RolandType4.xml` and `Gp8.xml`; `AddressProtocol` (message discovery through `memberNodeDef`, roles, device values) with `CreateRequest`/`CreateData`/`TryReadData`, tested against known RQ1/DT1 bytes.
- [x] 5. `MessageExchange` (receive, send with read-modify-write, listen) over a fake `ISysExPort`, then `MessageExchangeFactory` with the connection-based port.
- [ ] 6. A Yamaha envelope and a CP88 (or XG) map to prove `field`/`atomic` units and `midi.access`.

## 9. Implementation notes

Where the implementation differs from the design above:

- `CreateData`/`TryReadData` are on `AddressMap`, not on `AddressProtocol`: they need the layout of the map to pick the target group. `TryReadData` returns a `ReceivedData` that decodes on demand. `Encode`/`Decode` of raw data bytes do not exist (there is no opaque buffer).
- `AddressProtocol.CreateRequest` takes the `AddressRange` to request, and `AddressProtocol.SelectBody` selects the body group by device values.
- A data message carries **one group occurrence** (open choice `Target` is a group). A block of a few fields of a group, or one field whose group has more fields, cannot be sent yet (`NotSupportedException`). This matters for devices that take one parameter per message (Yamaha Parameter Change).
- The read-modify-write of `Send` keeps the device's current values in a value store (by path and instance) and copies the client's values over them by writing and reading the client's range with the map plan.
- `AddressMap` and `AddressProtocol` are internal: the map is **deduced from the range** (`range.ExecutionPlan`) on first use and cached per plan (`AddressMaps`). This uses two additions to Jacobi.BinarySerializer: `Serializer.Schemas` (to find the envelopes) and `ExecutionPlan.Name` (the root the plan was created for). A range of a plan created without a name (`ExecutionPlan.Create(SchemaGroup, …)`) cannot be used.
- An exchange is for one device (connection and device values), not for one map: a device with several map roots works through one exchange. `MessageExchangeFactory.Prepare(root)` / `PrepareAll()` build and validate maps up front; `PrepareAll` finds the address-map roots as the roots whose nodeDef is the contract of a data open choice, and throws an `AddressMapException` for the first invalid one (like `Serializer.PrepareAll`).
- The device values the messages need (`deviceId` for the GP-8) are checked when a map is first used by an exchange (`InvalidOperationException`).
- `sys.bytepacker` was removed from `RolandType4.xml` and the GP-8 patch: it reversed the bytes the bit slicer had already put in big-endian order.
- The GP-8 `Turbo` parameter (0x0A) is a 0-100 value in the schema now: the bulk dump has 0, 32, 78 and 100 there (marked `[TODO]` to verify against the manual).
