# Roland — SysEx analysis

Overview

Roland devices commonly use the public 0x41 manufacturer id and often incorporate vendor-specific multi-byte headers. Many instruments support both per-parameter request/response messages and large patch dump flows. Typical messages include device id and model/command bytes near the start of the payload.

Header format

- Messages begin with 0xF0 and terminate with 0xF7. The manufacturer id 0x41 follows the start byte for Roland devices.
- After the manufacturer id follows a device id (or channel), a model id or sub-id, and then a command or payload-specific header.
- Some families add extra sub-ids or status bytes immediately after the manufacturer id to select protocol variants.

Addressing

- Parameters are addressed either by absolute byte offset within a full patch dump or by parameter ID in request messages. Different Roland product families use one or both styles.
- When using offsets, the addressing is typically zero-based within the dump image; requests often carry a multi-byte offset or parameter index.
- Multi-device setups rely on the device id byte for routing; expect 0x7F or 0x7E-like values to indicate broadcast or special addressing in some tools.

Packing patterns

- SysEx enforces 7-bit-safe payloads: bytes between F0 and F7 are restricted to 0x00..0x7F. Large binary values are commonly split into 7-bit groups.
- Multi-byte numeric values may be stored as MSB/LSB pairs (14-bit values) or as contiguous 8-bit bytes depending on the parameter.
- Bitfields are used extensively: single bytes may contain multiple boolean flags or small enumerations.
- For transferring arbitrary binary (e.g., sample data), some tools use 7-bit packing schemes (e.g., group 7 bytes into 8 or similar) — check device-specific docs.

Checksum

- Many Roland implementations use a simple additive checksum or a masked-sum checksum placed near the end of the payload. The exact algorithm is device-specific.
- A common pattern is: checksum = (128 - (sum of payload bytes & 0x7F)) & 0x7F, but implementations vary — validate with known dumps.
- When building tools, provide a pluggable checksum routine so you can test different algorithms against device responses.

Fragmentation / multi-packet flows

- Large patch dumps are frequently fragmented into multiple SysEx messages. Fragments commonly include a sequence number, block index, or offset to enable reassembly.
- Reassembly strategy: collect fragments by model/device id and reassemble by block index or offset; verify completeness with size metadata or checksum.
- Some devices also support resume/retry controls for interrupted transfers; implement timeouts and retransmit heuristics when interacting with hardware.

Partial-op flows (request/response)

- Many Roland devices implement a read-request/response flow for individual parameters: host sends a compact request (including parameter id/offset) and device replies with a short value packet.
- Other devices only support full dump and write operations; in those cases, read-modify-write may require requesting the full image, patching, and writing back.
- When implementing parameter editors, support both single-parameter messages and full-dump workflows and prefer using device-supported read ops when available.

Example dumps

# Example: single-line SysEx message (hex tokens)
# F0 41 10 42 12 00 7F F7

Links & references

- Roland SysEx references and device manuals are available from manufacturer support pages and community-maintained resources. Add PDFs or device-specific notes to docs/analysis/docs/ for offline reference.

Notes

- When authoring device profiles, implement both offset-based mapping for full dumps and parameter-id templates for per-parameter operations. Provide configurable checksum and fragmentation handling primitives.
