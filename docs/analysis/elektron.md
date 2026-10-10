# Elektron — SysEx analysis

Overview

Elektron devices (e.g., Octatrack, Digitakt) typically use the manufacturer's 3-byte ID range and have well-documented parameter and bulk-dump protocols. They often support both parameter-level operations and full-state dumps.

Header format

- Manufacturer 3-byte ID (e.g., 0x00 0x20 0x6B) may appear after 0xF0. Payload typically includes device/model id and command identifiers.

Addressing

- Elektron uses parameter id addressing in many APIs; some flows use explicit offsets for larger dumps.

Packing patterns

- 7-bit-safe payloads apply. Elektron often uses contiguous byte sequences for values and may use MSB/LSB pairs for higher resolution parameters. Bitfields are used for flags.

Checksum

- Some flows have checksums; the algorithm is device-specific and should be documented per profile.

Fragmentation / multi-packet flows

- Large dumps are fragmented and include sequence information. Profiles must support reassembly across multiple SysEx messages.

Partial-op flows (request/response)

- Elektron devices often support targeted read/write commands with request/response exchanges; profile templates should capture these.

Example dumps

# Example: single-line SysEx message (hex tokens)
# F0 00 20 6B 01 02 03 F7

Links & references

- Add Elektron developer notes or manual extracts into `docs/analysis/docs/` and reference them here.

Notes

- Profiles for Elektron should favor parameter-id templates and explicit checksum/fragmentation descriptors when needed.

