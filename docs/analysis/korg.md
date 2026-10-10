# Korg — SysEx analysis

Overview

Korg devices use the 0x42 manufacturer id for many public SysEx messages. Korg formats often include model IDs and may use unique addressing for parameter groups. Some Korg devices have richer command sets for partial edits.

Header format

- Messages commonly start with 0xF0 0x42 followed by device/model and command bytes, payload and 0xF7 terminator.

Addressing

- Korg frequently uses parameter addresses composed of several bytes (bank, parameter number) rather than raw offsets. Some models accept queries for a parameter id and return its value.

Packing patterns

- Standard 7-bit-safe SysEx encoding applies. Korg formats may use multi-byte contiguous fields or split-values (MSB/LSB) depending on parameter resolution.
- Bitfields are used for compact flag storage.

Checksum

- Some Korg formats include checksums; algorithm varies by family and should be specified in the profile when present.

Fragmentation / multi-packet flows

- Large data transfers (patch dumps) may be split across messages and include sequence or block counters. Reassembly must respect ordering.

Partial-op flows (request/response)

- Korg devices commonly provide single-parameter read/write commands; profiles should include request/response templates for these operations.

Example dumps

# Example: single-line SysEx message (hex tokens)
# F0 42 12 34 56 78 F7

Links & references

- Add Korg device manuals or community references into `docs/analysis/docs/` and link them here.

Notes

- Profile authors should prefer parameter-id templates for Korg devices when available and include checksum/fragmentation primitives if the model requires them.

