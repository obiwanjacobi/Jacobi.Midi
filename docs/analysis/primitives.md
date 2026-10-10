# Canonical Packing & Message Primitives

This document lists canonical primitives that profiles should expose to describe how logical settings map to device SysEx binary representations.

Purpose
- Provide a small set of orthogonal primitives that can express the majority of real-world SysEx packing patterns.
- Keep primitives composable so complex formats can be described by combining primitives.

Primitives

1. Midi7BitGroup(size)
   - Description: Encodes raw binary into 7-bit-safe groups for SysEx transport. `size` is the number of data bytes grouped per unit.
   - Parameters: size (int)

2. MSBLSB(pair)
   - Description: Represents values encoded as MSB/LSB pairs (commonly 14-bit values as two 7-bit bytes or two 8-bit bytes depending on device).
   - Parameters: ordering (e.g., MSB-first), groupWidth (7 or 8)

3. BitField(offset, length, bitOrder)
   - Description: A field packed at bit granularity inside one or more bytes.
   - Parameters: offset (bit offset from payload start), length (bits), bitOrder (msb|lsb)

4. MultiByte(endian)
   - Description: Multi-byte integer stored in contiguous bytes using `big` or `little` endianness.
   - Parameters: endian ("big"|"little"), signed (bool), width (bytes)

5. Checksum(type, range)
   - Description: Compute checksum over specified byte range. Common types: sum-mod-128, xor, crc-7, crc-16.
   - Parameters: type (string), range (start..end)

6. Fragmentation(maxPayload, seqBytes)
   - Description: Describes how a large payload is split into multiple SysEx messages and how sequence or block ids are encoded.
   - Parameters: maxPayload (int), seqOffset (byte offset), seqLength (bytes)

7. Template(message)
   - Description: A message template for request/response with placeholders for {offset}, {length}, {data}, {seq}.
   - Parameters: template string (hex tokens or binary representation)

8. Addressing(mode)
   - Description: How parameters are located: `offset` (absolute byte offset), `parameterId` (id-based), `bank/param` (two-level addressing).
   - Parameters: mode (string), field bytes (positions)

9. Escape to Adapter
   - Description: When declarative primitives are insufficient, profiles may reference a compiled C# adapter (IDeviceAdapter) that performs custom packing/unpacking.
   - Parameters: adapter identifier (assembly-qualified name)

Usage
- Compose primitives in FieldDescriptor.Packing (or a PackingSpec) to fully define how to read/write the logical setting.
- Include Template entries for partial read/write flows; use Fragmentation when needed for multi-packet dumps.

Examples

- 14-bit value stored as two 7-bit bytes (MIDI 7-bit groups): `MSBLSB(groupWidth=7)`
- Single-bit flag at bit 3 of byte offset 5: `BitField(offset=5*8+3, length=1, bitOrder=msb)`
- Full patch dump fragmented into 256-byte blocks with a 1-byte sequence id at offset 2: `Fragmentation(maxPayload=256, seqOffset=2, seqLength=1)`

Notes
- Profiles should include explicit checksum primitives when the device uses a non-trivial checksum algorithm. Don't assume checksums unless documented.
- The primitives list can be extended as new patterns are discovered; keep compatibility by adding optional parameters rather than changing semantics.

