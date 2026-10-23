---
name: device-schema
description: Rules for creating or editing MIDI device schemas in XML (e.g. Gp8.xml, Gx700.xml, G50.xml in src/Jacobi.Midi.<Vendor>/Schemas) and vendor envelope schemas (Roland.xml): file layout, header comment, naming, data types, address map (midi.* properties), comments and tests. Use for any change to a *.xml schema under a Schemas folder or to Jacobi.Midi.Device/Midi.xml.
---

# MIDI device schemas (XML)

A device schema describes the SysEx data of one device: its parameters and, for address based devices, its
address map. It builds on a vendor envelope schema (`Roland.xml`: the messages), which builds on the base `midi`
schema (`Jacobi.Midi.Device/Midi.xml`: shared processor definitions).

Sources of truth, read them when a rule below is not enough:
- Schema format: `../Jacobi.BinarySerializer/src/Jacobi.BinarySerializer/Schema/readme.md` and `schema.xsd` next to it.
- Processors and their properties: `../Jacobi.BinarySerializer/src/Jacobi.BinarySerializer.Processors/readme.md`.
- Address map and `midi.*` properties: `src/Jacobi.Midi.Device/Addressing/readme.md`.
- Vendor conventions: `docs/analysis/<vendor>.md`.

Reference examples: `Gp8.xml` (address map, manual based) and `Gx700.xml` (address map, reverse engineered from
dumps) in `src/Jacobi.Midi.Roland/Schemas`, `G50.xml` (not address based) in `src/Jacobi.Midi.Yamaha/Schemas`.

## Location and naming

- File: `src/Jacobi.Midi.<Vendor>/Schemas/<Device>.xml`. The project embeds `Schemas\*.xml`. A new vendor project needs that `EmbeddedResource` and must be added to `TestHelpers.CreateSchemas` / `CreateProcessors` in `Jacobi.Midi.Tests/Schema`.
- `<schema name="...">` is the file name without extension, PascalCase without punctuation: `Gp8` for the GP-8, `Gx700` for the GX-700. References to it are case-insensitive (`roland.level`).
- nodeDefs and dataTypeDefs: camelCase (`patch`, `system`, `nibble8`, `upDown`, `switch100`).
- Members (groups, repeats, fields): PascalCase, after the parameter names in the manual (`CutoffFreq`, `EffectLevel`). Same name for the same thing across sections (`On`, `Type`, `EffectLevel`).
- Bytes whose meaning is unknown: `Unknown<offset>` with the hex offset in the section (`Unknown1A`), type `sys.uint8` or `sys.bytes` with `byteLength`.
- The address map root is `AddressMap`; a repeat of stored patches is `Memory` with an item `Patch`; the edit buffer is `Temporary`.

## Document layout

In this order, leave out empty sections:

```xml
<?xml version="1.0" encoding="utf-8"?>
<!-- header comment, see below -->
<schema name="Device">
  <includes>
    <include schema="midi"/>
    <include schema="roland"/>   <!-- every document whose definitions are used -->
  </includes>
  <nodeDefs> ... </nodeDefs>         <!-- reusable fields/groups: multi-byte values, patch, system -->
  <dataTypeDefs> ... </dataTypeDefs> <!-- device specific value types -->
  <members> ... </members>           <!-- the roots: AddressMap, or the message groups -->
</schema>
```

- Two-space indentation. Keep the existing self-closing style of the file (`"/>` or `" />`).
- Include every schema you reference with `<document>.<name>`: `midi` always, the vendor envelope when using its types or nodeDefs.
- Describe a patch, system block, etc. once as a nodeDef and use it from the map (`nodeDef="patch"`).

## Header comment

Every device schema starts with a comment that holds what the XML cannot say:

1. Device and document: `Roland GP-8 Guitar Effects Processor - memory map (Owner's Manual, MIDI Implementation v1.00).` If there is no manual, name the source (URL, author) and the dumps it was checked against, and say so: `There is no manual source yet.`
2. The envelope used and the device values: `Used with the Message32 messages of the Roland schema: Model-ID 0x79, Device-ID ...`
3. The message format as bytes: `F0 41 <dev> 79 12 <address:4> data <checksum> F7`.
4. The address layout bit by bit (or, without addresses, how a dump is selected), and what is atomic.
5. Value conventions: offsets, multi-byte encodings, lists that recur.
6. A pointer to the `midi.*` properties (`see src/Jacobi.Midi.Device/Addressing/readme.md`).

Keep the header in sync with the XML: when an address, size or id changes, change the header too.

## Comments on members

Put the comment on its own line **above** the member (GP-8 style), not trailing on the same line:

```xml
<!-- 0x02: 0-100 -->
<field name="Sens" datatype="roland.level"/>
<!-- 0x15 (MSB), 0x16 (LSB): 0-1000 -->
<field name="DTime" datatype="delayTime" byteLength="2"/>
<!-- 0x21-0x30: ASCII 32-127 -->
<field name="Name" datatype="sys.string" byteLength="16">
  <processor name="midi.string"/>
</field>
```

- Format: `<offset>: <stored range or values>`. The offset is hex, relative to the start of the nodeDef or section. Add the displayed value with `(= ...)` when it differs from the stored value: `0-100 (= -50 - +50)`.
- Enumerations: list the values (`0 = down, 100 = up`), or better, model them with a `sys.map` dataTypeDef (see `upDown` in `Gp8.xml`).
- A section or group gets a comment with its number and size: `<!-- Section 01: 8 bytes. -->`.
- Older files (`Gx700.xml`, `G50.xml`) use trailing, column-aligned comments. When editing a few lines there, keep the existing style and its alignment. Do not convert a whole file without asking.

## Unknown and unverified data

- Never invent offsets, ranges or names. What is not confirmed by a manual or a dump gets a `[TODO]` comment that says what is known: `<!-- [TODO] 0x4E in the dump (patch level?) -->`, `<!-- [TODO] verify ... against the manual. -->`.
- Keep the bytes in place with an `Unknown..` field so the layout and the round trip stay correct.
- Placeholder names (`Param1` ... in a `sys.map`) are allowed only with a `[TODO]`.

## Data types and processors

- Built-in types always need the `sys.` prefix (`sys.uint8`, `sys.uint16`, `sys.bool`, `sys.string`, `sys.bytes`).
- Reuse before you define: vendor types (`roland.level` 0-100, `roland.switch`), base processor definitions (`midi.sysexbyte`: 7 bits per byte big endian, `midi.string`: ASCII), vendor nodeDefs (`roland.sysex16/24/32`).
- Define a dataTypeDef in the device schema for device-specific ranges and enumerations: `basedOn`, `min`/`max`, and a `sys.map` processor (`to`, `default`, one property per value). Move a type to the vendor schema only once a second device needs it.
- Multi-byte values: give the field a `byteLength` and a field processor: `midi.sysexbyte` (7 + 7 bits, MSB first), `sys.bitslicer bits="4" byteorder="big"` (nibbles). Make it a nodeDef when it recurs (`sysex14`, `nibble8`).
- Bit flags in one byte: a group with `byteLength="1"` and `sys.bitpacker`, fields with `bits` (see `EffectSwiches1` in `Gp8.xml`).
- Fixed bytes (ids, commands, terminators): a field with `value`. The reader checks it, the writer supplies it.

## Address map (address based devices)

Full rules: `src/Jacobi.Midi.Device/Addressing/readme.md`. In short:

- The root `AddressMap` declares the envelope's open choice body nodeDef for its address size (`roland.messageBody16` for 2 address bytes, `messageBody32` for 4) and the fixed device values (`midi.device.modelId="0x13"`).
- `midi.addressStart` / `midi.addressSize` are in the **wire form** as written in the manual: hex, bytes separated by `-` (`0x40-00`, `0x00-01-00-00`). `40 00` on the wire is linear 0x2000. Do not write linear addresses.
- `midi.addressStart` is absolute. Leave it out where the node follows its previous sibling. Never put it inside a repeat item.
- `midi.addressSize` on a repeat is the size of one item: the stride. It may be larger than the content (reserved addresses).
- `midi.atomic="true"` on every group the device always transfers as a whole (one DT1 per patch or section). Do not nest atomic groups.
- Repeats need a constant `count`. No choices and no virtual fields (`byteOffset`) inside the map.
- `midi.access="read"` / `"write"` for read-only and trigger addresses.

## Non-address devices and envelopes

- Message groups sit under a root group with the `midi.sysex` processor (framing F0 ... F7).
- Put the bytes a checksum covers in a `ChecksumScope` group with the vendor checksum processor (`roland.checksum`, `yamaha.checksum`).
- Use `byteSize` with a `ref` for byte counts. When the writer cannot derive one, use a constant `value` and add a `[TODO]`.
- In an envelope schema, tag the messages with `midi.message` and the fields with `midi.role` (see the Addressing readme).

## Tests

Every device schema has tests in `src/Jacobi.Midi.Tests/Schema`:
- `<Device>SchemaTests`: the plan builds (`serializer.GetPlan(new SchemaName("<Device>.AddressMap"))`), and a read/write round trip of a real dump gives the original bytes.
- `<Device>AddressMapTests` (address based devices): the address space, messages, device values, address ranges of the map nodes, `TryReadData` / `Decode` / `CreateData` on dump messages. Write expected addresses as linear constants with the wire form in a comment (`0x2000; // wire 40 00`).
- Dump files: `src/Jacobi.Midi.Tests/Schema/<device>-sysex[-<part>].syx`, copied to the output by a `<None Update=...>` entry in the test csproj.

Run from `src/`: `dotnet test --project Jacobi.Midi.Tests` (never on the solution).

## Keeping things in sync

- If a schema change needs something the format or a processor does not support, stop and say so: that change starts in Jacobi.BinarySerializer.
- Readmes do not copy schema content verbatim: they link to the schema file, or use a stand-alone example of a made-up device. Check the prose in `Addressing/readme.md` and `docs/analysis` that describes a schema you change (addresses, sizes, names) and update it.
- The Schema Editor loads these files; keep to constructs the schema format documents.
