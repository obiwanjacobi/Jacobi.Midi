# Novation — SysEx analysis

Overview

Novation devices (e.g., synths and grid controllers) often use the 3-byte manufacturer id blocks. Some products expose concise parameter request/response pairs and others favor bulk dump semantics.

Header format

- 0xF0 followed by manufacturer id bytes, device/model and command bytes, payload and 0xF7.

Addressing

- Parameter-addressing and offset-based dumps both exist; newer products often use parameter IDs.

Packing patterns

- 7-bit-safe encoding applies. Values may be encoded in contiguous bytes or split into MSB/LSB pairs for higher resolution. Bitfields are used for flags and options.

Checksum

- Checksum usage varies; check model-specific docs. Provide checksum spec in profiles when required.

Fragmentation / multi-packet flows

- Large dumps may be fragmented; sequence numbers or block identifiers may be used.

Partial-op flows (request/response)

- Many Novation products support small parameter read/write exchanges; profile templates should model these flows when available.

Example dumps

# Example: single-line SysEx message (hex tokens)
# F0 00 20 33 01 02 03 04 F7

Links & references

- Add Novation manuals and community resources to `docs/analysis/docs/` and reference them here.

Notes

- Profiles should support both parameter-id templates and offset-based mappings; include checksum and fragmentation primitives when needed.
