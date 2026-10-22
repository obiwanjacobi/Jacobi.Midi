# Reverse-Engineering Workbench - UI/UX Proposal

Status: proposal - reviewed; decisions are listed at the end.

## Purpose

The workbench helps to build a schema for a device that has no (or poor) SysEx documentation.
The user loads one or more dumps of the device, compares them, and from the stream of bytes
builds a schema step by step: fields with a data type and processors, grouped into messages.
While a field is being defined, its logical value (the value model) is shown directly, for every loaded dump.

The core technique is *differential capture*: dump the same patch repeatedly while changing one parameter
on the device. The bytes that differ between those dumps are the bytes of that parameter.

## Current state

- `Views/ReverseEngineeringWorkbenchView.axaml` exists but is not shown anywhere (not used in `MainView`).
  It imports hex text as single `SysExMessage`s, compares the *first two* messages byte by byte in a side list,
  and adds a selected byte range to the schema with `AddSelectedBytes` (name + data type).
- Its state lives in `MainViewModel` (`SysExMessages`, `ComparedBytes`, `SelectByte`, `HexInput`, `ReverseField*`).
- The Match part of the instantiate dialog (`SysExMatchViewModel`) already loads `.syx` files,
  splits them into messages (`SysExBuffer.Split`), receives SysEx from the MIDI bar and reads (deserializes)
  a buffer into the instance tree (`InstanceValueSink`).

This proposal replaces the existing workbench view and reuses the Match, node editor (`NodeFacets`) and
data type (`DataTypeOptions`, `DataTypeValueValidator`) parts.

## Concepts

| Term | Meaning |
|---|---|
| **Capture** | One dump as received or loaded: an ordered list of SysEx messages (F0 … F7). A `.syx` file, pasted hex or a MIDI receive. |
| **Baseline** | The capture all others are compared to. All highlighting is drawn on the baseline. |
| **Variant** | Any other capture. It carries a *change note*: free text that tells what was changed (e.g. `reverb level 40`). |
| **Message slot** | A position in the dump that the messages of all captures are aligned on: the message index ("message 3"). Bytes are compared per slot. Each SysEx message stays separate; message bodies are never joined. |
| **Workbench session** | The captures, their notes, byte annotations and the target schema document; saved (self-contained) so work can be resumed. |
| **Target schema** | The schema document (a normal open document of the editor) that the workbench adds fields to. |
| **Unknown span** | A run of bytes in a message that no schema field describes yet. |

## Layout

The workbench needs width (hex grid + field panel), so it is a **mode** of the main window, not a dialog:
a clear `Schema | Workbench` switch in the toolbar that replaces the whole content area below it. In Workbench mode the left panel holds the captures and the schema outline,
the centre holds the byte view, and the right panel the field being defined.

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ ☰  [Schema | Workbench]   GX-700.xml  •  session: gx700-patch.workbench          MIDI bar ●  │
├───────────────────────┬──────────────────────────────────────────────┬───────────────────────┤
│ CAPTURES        + ▾   │ Slot 1 · DT1 · addr 00 00 00 00 · 77 bytes   │ FIELD                 │
│ ★ patch1 (baseline)   │      00 01 02 03 04 05 06 07 08 09 0A 0B …   │ Name   ReverbLevel    │
│   14 msgs · 912 B     │ 0000 F0 41 00 79 12 00 00 00 00 4E 01 02 …   │ Type   sys.uint8   ▾  │
│ ● reverb=40   2 B Δ   │ 0010 07 08 09 0A 0B 0C 0D 56 49 4E 54 41 …   │ Bytes  slot 9 · 23–23 │
│ ● reverb=80   2 B Δ   │      ── ── ── ── ── ── ── V  I  N  T  A      │ Processors        +   │
│ ○ gain=10     3 B Δ   │ 0020 57 49 4E 03 07 00 00 00 64 00 00 00 …   │ ─────────────────     │
│   (hidden)            │ …                                            │ VALUES                │
│                       ├──────────────────────────────────────────────┤ patch1        0       │
│ SLOTS                 │ INTERPRET bytes 23–23 (slot 9)               │ reverb=40    40  ✓    │
│  1 addr 00000000 ··Δ  │           patch1 reverb=40 reverb=80 gain=10 │ reverb=80    80  ✓    │
│  2 addr 00000100      │ uint8        0      40      80       0       │ gain=10       0       │
│  …                    │ int8 (7b)    0      40     -48       0       │                       │
│  9 addr 00000900 ·Δ   │ bits      0000000 0101000 1010000 0000000    │ ⚠ none                │
│  …                    │ ascii        ·      (       P        ·       │                       │
│ SCHEMA OUTLINE        │ ▸ uint16 7b MSB/LSB with byte 24 …           │ [Define field]  ⌫    │
├───────────────────────┴──────────────────────────────────────────────┴───────────────────────┤
│ Matched 14/14 messages · slot 9: 41 of 88 bytes described · 2 unknown spans                  │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

### Left: Captures and slots

- **Captures list.** One row per capture: the label (file name, or `Received 12:34:56`), the change note,
  and a diff badge (`2 B Δ` = bytes that differ from the baseline). The ★ marks the baseline (context menu: *Set as baseline*).
  A capture can be hidden from the comparison (○) without removing it, e.g. a dump that was taken by mistake.
- Add a capture with `+`: *Load .syx…*, *Paste hex…*, *Receive* (see [Capturing from the device](#capturing-from-the-device)).
- Selecting a variant **focuses** it: the highlight shows only its differences, and its values are emphasised in the
  interpret strip and the Values list. With no variant focused, the highlight shows the union of all differences.
- **Slots list.** The message slots of the baseline (by index; the address is shown once the frame defines one) and a marker `Δ` when any visible variant differs
  in that slot. This answers "which message changed?" for dumps with many messages.
  Selecting a slot scrolls the byte view to it.
- **Schema outline.** The schema tree of the target schema (compact, read-only here) with a coverage indicator per group.
  Selecting a field selects its bytes in the byte view; the full node editor stays in Schema mode.

### Centre: Byte view (the baseline)

Only the baseline is drawn; the other captures appear as overlays and in the interpret strip.
This keeps the UI to one hex grid regardless of the number of captures.

- A classic hex grid: 16 bytes per row, an offset column, and an ASCII column (printable characters).
  All messages of the capture are in one scrolling view, each slot starts with a **slot header**
  (`Slot 9 · 88 bytes`, plus the message group and address once the frame defines them). F0 and F7 are drawn dimmed.
- Offsets are relative to the message (that is what a schema describes); the capture offset is in the tooltip.
- **Layers** (each can be toggled in a small legend above the grid):
  1. *Difference* - background tint on bytes that differ in the focused variant (or any visible variant).
     The intensity grows with the number of variants in which the byte differs. A byte that changes in
     **every** variant, whatever was changed, is marked as *probably derived* (checksum, counter) with a hatched tint,
     so it does not distract.
  2. *Coverage* - a coloured underline per schema field that describes the byte; the field name is shown
     as a label below the first byte of the field (as the `V I N T A` row in the sketch, which shows the ASCII
     interpretation of a string field). Unknown spans have no underline.
  3. *Changed bits* - for a byte that differs, a tiny 7-dot bar under the hex value shows which bits changed.
     This makes bit fields (several switches packed into one byte) visible without opening anything.
- **Tooltip** on a byte: the value in every capture (`patch1 00 · reverb=40 28 · reverb=80 50`), the field it belongs to
  and any annotation.
- **Selection**: click, shift+click / shift+arrows to extend, double-click selects the whole unknown span or field.
  The selection drives the interpret strip and the Field panel.
- **Annotations**: a short note on a byte or range (`probably tone A/B?`) that is not a field yet.
  Shown as a corner mark; kept in the session, not in the schema.
- When a variant has a **different length** in a slot (e.g. a variable-length name), byte-wise comparison stops at the
  first difference in length; the slot header shows `length 88 → 90` and the rest of the slot is compared
  after aligning on the longest common subsequence (shown with a different tint, as it is a guess).

### Centre bottom: Interpret strip

For the current selection: a table with a row per **candidate interpretation** and a column per capture
(baseline first, focused variant highlighted). It answers "what data type makes the change notes come true?"

| Row | Selection |
|---|---|
| `uint8`, `int8`, 7-bit signed | 1 byte |
| `uint16` big/little endian, 14-bit MSB/LSB (`midi.sysexbyte`), nibble-pair (4-bit hi/lo) | 2 bytes |
| `bits` (binary) | 1-2 bytes |
| `string` (ASCII) | any length, shown first when all bytes are printable |
| `bytes` (hex) | always, as last row |

- When the variants have a numeric change note, the strip checks every row against the noted values and puts the
  rows that match first, with ✓. If a row matches after an offset or factor, it says so: `uint8 · shift −64 ✓`
  (the value is stored with an offset of 64) - this proposes the data type def `Scale`/`Shift` too.
- Clicking a row (or the arrow ▸ on it) pre-fills the Field panel with that data type (and processor, e.g. `midi.sysexbyte`).
- Rows show at most a handful of captures; with more captures the strip turns into the **sweep table** (see below).

### Right: Field panel

Defines the field for the selection (or edits the field that is selected), in place:

- Name, data type (`DataTypeOptions`, editable combo), byte length (from the selection), processors (same picker as the node editor).
  For a bit field: bit offset and bit length, picked by clicking the bits in a bit grid of the byte.
- **Values**: the logical value of this field in *every* capture, produced by actually reading the capture with the
  draft schema (see [Live values](#live-values-value-model)). Next to each value the change note is compared:
  ✓ when it matches, ⚠ when not. Data type def `Options` labels are shown when there are any (`2 · Hall`).
- Read problems are shown here, at the field they occur in (e.g. a processor that fails on this byte range).
- *Define field* inserts the field into the target schema (see [From bytes to schema](#from-bytes-to-schema)).
  ⌫ turns a field back into an unknown span.

## From bytes to schema

The schema has to describe each message from start to end, or the serializer cannot read it.
To let the user define fields in any order (the field found by a diff is usually somewhere in the middle),
the workbench keeps every message body **fully described** with placeholder fields:

1. **Frame first.** For a new target schema, the first step is the message envelope: header, framing and checksum.
   The workbench proposes it from the captures:
   - the common prefix of all messages (`F0 41 00 79 12`) → constant fields (`value="0x41"`), with a known
     manufacturer id named (`RolandId`);
   - the byte before F7 that changes in every variant → a checksum candidate; the workbench tries the known checksum
     processors (`roland.checksum`, `yamaha.checksum`, `sys.crc`) over the plausible ranges and proposes the one that fits
     all captures, including its scope group;
   - bytes after the header that are different for each slot but equal across captures → an address or index
     (`midi.role="addressStart"`). This only names the slots; they stay aligned by index.

   Alternatively the user includes an existing vendor schema (e.g. `Roland`) and picks the message group to use (e.g. `DT1`).
   After the frame the user works on the message **body** only; framing bytes are drawn dimmed.
2. **Unknown spans are placeholder fields.** Each unknown span is a `sys.bytes` field with a fixed byte length,
   named `Wip_<offset>`. The name alone marks it as work in progress; there is no extra property.
3. **Define field** splits the placeholder: `Wip_20 (8 bytes)` → `Wip_20 (3)`, `ReverbLevel (1)`, `Wip_24 (4)`.
   Removing a field merges it back into its neighbouring unknown spans.
4. **Slots become groups.** Messages with a different body layout (different slots / addresses) get their own group
   (one per slot key), under a choice selected by the address. Slots with the same layout (e.g. 4 identical effect blocks)
   can be turned into a repeat from the Slots list (*Same layout as slot 3*).
5. **Grouping fields.** Selecting a range that spans several fields offers *Group…*, e.g. to put a checksum scope
   around them or to apply a processor (7-bit packing) to the group.

All changes go through the normal draft model (`EditNode` / `NodeFacets`) of the target schema, so the Schema mode
shows them immediately and *Save* writes the schema as usual.

## Live values (value model)

After every change of the target schema (debounced), the workbench reads **all** visible captures with the draft schema,
the same way Match does (`InstanceValueSink`), slot by slot. Dumps are small, so this is cheap.
The result feeds:

- the Values list of the Field panel and the interpret strip (for a defined field),
- the coverage layer (which bytes belong to which field),
- the status bar (`Matched 14/14 messages · slot 9: 41 of 88 bytes described`).

A message that fails to read is marked in the Slots list; the byte view shows a marker at the position where reading stopped.

**Byte positions.** To draw the coverage layer and to select a field's bytes, the workbench needs to know which input bytes
a field was read from. `IValueSink.SetField` gets a `FieldContext` and a `LogicalField`, but no byte position.
The first version works **without** serializer support: the workbench computes the positions itself from the schema
(byte lengths in schema order). That works for the fixed-size fields the workbench creates; fields of variable size or below
processors that transform the bytes (7-bit packing, nibbles) get the range of their whole group.
A read trace in Jacobi.BinarySerializer (e.g. a service via `ValueContext.Services`) is a possible later solution; it is on the to-do list.

## Capturing from the device

The quickest loop is: change one parameter on the device → capture → type the new value.

- **Receive** collects SysEx messages from the MIDI bar connection until no SysEx arrives for a short idle time
  (e.g. 500 ms), so a multi-message dump becomes one capture. (SysEx7 via `SysEx7Assembler`, as Match does now.)
- **Request** (optional): when the schema has a request message (`midi.message="request"`), send it before receiving.
  Until then the user triggers the dump on the device.
- **Auto-capture**: while on, every received dump becomes a new variant without pressing anything.
- After a capture, the user types the change note (free text) in the captures list.

### Sweeps

A series of variants with the same parameter (`Reverb Level = 0, 10, 20, … 100`) is a **sweep**.
For a sweep the interpret strip turns into a table *noted value → raw bytes → decoded value* plus a small
line plot of raw against noted value. The plot makes typical encodings recognisable at a glance:
a straight line (`uint8`), a line with an offset (`Shift`), a saw tooth at 128 (14-bit MSB/LSB over two bytes),
steps (the parameter uses a smaller range than the field), a jump at the middle (signed).
The workbench proposes the data type and `Scale`/`Shift` that fit the sweep.

## Session persistence

A workbench session is saved as its own, **self-contained** file next to the target schema (proposal: `<name>.workbench.json`):
the captures (embedded as hex - most captures come live from MIDI and have no file), labels, change notes, baseline,
hidden flags and the annotations. The schema itself is saved as a normal schema document; the session only refers to it.

## Keyboard

Arrow keys move the selection, Shift+arrows extend it, `Tab` / `Shift+Tab` jump to the next/previous differing byte,
`Ctrl+Tab` to the next unknown span, `Enter` defines the field, `F2` renames it, `Ctrl+1…9` focuses a variant.

## Phasing

1. **Compare.** Captures (load / paste), message split into slots, baseline + variants with change notes, difference layer
   on the baseline, Slots list with Δ, interpret strip, byte tooltip. Replaces the current view and its `MainViewModel` members
   with a `Workbench/` folder and a `WorkbenchViewModel`. Session persistence.
2. **Build.** Frame proposal (header constants, checksum detection), unknown spans as placeholder fields, *Define field*,
   Field panel with live values from the serializer, coverage layer (byte positions computed by the workbench).
3. **Device loop.** Receive with idle timeout, auto-capture, request message, sweeps with plot and type/scale proposal,
   bit-field editing, repeat-from-slots.

## Decisions

1. **Placement:** a mode of the main window, with a clear `Schema | Workbench` switch.
2. **Placeholder fields:** allowed in the target schema while work is in progress; marked by name only (`Wip_<offset>`).
3. **Slot alignment:** by message index. Alignment by a key (e.g. the address) may come later.
4. **Read trace:** first try without; the workbench computes byte positions itself. The serializer problem is on the to-do list.
5. **Session file:** self-contained; captures are embedded.
6. **Multi-message bodies:** SysEx messages are always kept separate; the address is only known after analysis, so bodies are not joined.
7. **Notes are free text:** change notes and annotations are not parsed. Structured data (name, data type) is entered when a field is defined.
   Checking interpretations against the change notes (✓ in the interpret strip, sweeps) therefore needs another way to enter the noted value; see the to-do list.
8. **Session title:** a new session asks for a title (e.g. 'GX-700'). In phase 2 the schema document gets that name when the first schema node is created,
   so work can also continue on an existing schema document without the saved session.
