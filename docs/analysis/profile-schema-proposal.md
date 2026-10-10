# Profile Schema Proposal (YAML fragments)

This document shows example YAML fragments illustrating how the extended profile schema can express packing primitives and message flows.

Top-level structure (example)

```yaml
manufacturer: ExampleCorp
model: ExampleSynth 1000
metadata:
  sysexHeader: "F0 00 20 33"
fields:
  - name: MasterVolume
	type: uint7
	description: "Master volume (0..127)"
	packing:
	  addressing: offset
	  offset: 0
	  length: 1
	  primitive: Midi7BitGroup
  - name: Osc1Pitch
	type: uint16
	description: "Oscillator 1 pitch (0..16383)"
	packing:
	  addressing: offset
	  offset: 1
	  length: 2
	  primitive: MSBLSB
	  primitiveParams:
		groupWidth: 7
  - name: Flags
	type: bitflag
	packing:
	  primitive: BitField
	  primitiveParams:
		offsetBits: 24  # byte 3 * 8
		lengthBits: 3
		bitOrder: msb

partials:
  readSettingTemplate: "F0 00 20 33 10 {offset} {length} F7"
  writeSettingTemplate: "F0 00 20 33 11 {offset} {data} F7"

messageFlows:
  dump:
	type: fragmented
	fragmentation:
	  maxPayload: 240
	  seqOffset: 2
	  seqLength: 1
	request:
	  - "F0 00 20 33 30 00 {seq} F7"
	response:
	  - expect: "F0 00 20 33 31 {seq} <payload> F7"

checksums:
  - name: default
	type: sum-mod-128
	range: 1..-2  # from byte 1 to the byte before last

```

Explanation

- `packing.primitive` references a canonical primitive from `primitives.md`. `primitiveParams` supplies arguments.
- `partials` provides convenience templates for single-field read/write operations; templates may include placeholders.
- `messageFlows.dump` specifies a multi-step fragmented flow for full-patch dumps. The `request` array lists messages to send in order; `response` lists expected response forms.
- `checksums` can be declared globally or per-flow and referenced by templates.

Notes on templates

- Templates should be validated by the ProfileLoader. Prefer explicit byte arrays but allow concise hex-string templates for readability.
- Profiles may optionally include `adapter` entries referencing compiled code for complex behaviors.


